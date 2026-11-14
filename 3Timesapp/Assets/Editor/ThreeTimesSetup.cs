#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.PackageManager;
using UnityEditor.PackageManager.Requests;
using UnityEngine;
using UnityEngine.Rendering;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Configurarea inițială a proiectului 3Times, faza 1:
    /// curăță conținutul tutorialului din șablon, instalează pachetele AR
    /// și setează Player Settings pentru Android.
    /// Meniu: Tools → 3Times → 1. Configurează proiectul
    /// </summary>
    public static class ThreeTimesSetup
    {
        const string Log = "[3Times Setup]";

        static readonly string[] PackagesToAdd =
        {
            "com.unity.xr.arfoundation",
            "com.unity.xr.arcore",
            "https://github.com/google-ar/arcore-unity-extensions.git"
        };

        // Pachete din șablon de care aplicația nu are nevoie.
        static readonly string[] PackagesToRemove =
        {
            "com.unity.ai.assistant",
            "com.unity.ai.inference",
            "com.unity.cinemachine",
            "com.unity.learn.iet-framework",
            "com.unity.multiplayer.center"
        };

        // Conținutul tutorialului „Get Started” din șablonul Universal 3D.
        static readonly string[] AssetsToDelete =
        {
            "Assets/Tutorials",
            "Assets/Audio",
            "Assets/Materials",
            "Assets/Prefabs",
            "Assets/Skyboxes",
            "Assets/VFX",
            "Assets/SourceFiles",
            "Assets/UI/Remaining_Collectibles_UI.prefab",
            "Assets/Scenes/GetStarted_Scene.unity"
        };

        static AddAndRemoveRequest packageRequest;

        [MenuItem("Tools/3Times/1. Configurează proiectul", priority = 1)]
        public static void Run()
        {
            if (!EditorUtility.DisplayDialog("3Times: configurare",
                    "Scriptul:\n\n" +
                    "• șterge conținutul tutorialului din șablon (păstrează setările URP și fontul Inter)\n" +
                    "• instalează AR Foundation, ARCore XR Plugin și ARCore Extensions\n" +
                    "• setează Player Settings pentru Android\n\n" +
                    "La final Unity va cere o repornire (pentru Active Input Handling). Continui?",
                    "Da, configurează", "Anulează"))
                return;

            CleanTemplate();
            ConfigurePlayerSettings();
            InstallPackages();
        }

        // ------------------------------------------------------------ 1. curățare

        static void CleanTemplate()
        {
            EnsureFolder("Assets/Settings");
            EnsureFolder("Assets/Scenes");
            EnsureFolder("Assets/UI");
            EnsureFolder("Assets/UI/Fonts");

            // Păstrăm setările URP (sunt referite din Quality și Graphics Settings).
            const string templateSettings = "Assets/SourceFiles/Settings";
            if (AssetDatabase.IsValidFolder(templateSettings))
            {
                foreach (var guid in AssetDatabase.FindAssets("", new[] { templateSettings }))
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    if (AssetDatabase.IsValidFolder(path)) continue;
                    if (Path.GetDirectoryName(path)?.Replace('\\', '/') != templateSettings) continue;
                    string target = "Assets/Settings/" + Path.GetFileName(path);
                    string error = AssetDatabase.MoveAsset(path, target);
                    Debug.Log(string.IsNullOrEmpty(error) ? $"{Log} Mutat {path} → {target}" : $"{Log} Nu am mutat {path}: {error}");
                }
            }

            // Fontul Inter are diacritice românești; îl folosim în interfață.
            const string inter = "Assets/SourceFiles/Fonts/Inter-Variable.ttf";
            if (File.Exists(inter))
            {
                string error = AssetDatabase.MoveAsset(inter, "Assets/UI/Fonts/Inter-Variable.ttf");
                Debug.Log(string.IsNullOrEmpty(error) ? $"{Log} Font Inter mutat în Assets/UI/Fonts" : $"{Log} Font: {error}");
            }

            foreach (var path in AssetsToDelete)
            {
                if (AssetDatabase.IsValidFolder(path) || File.Exists(path))
                {
                    bool ok = AssetDatabase.DeleteAsset(path);
                    Debug.Log($"{Log} {(ok ? "Șters" : "NU am putut șterge")} {path}");
                }
            }

            // Scenele din Build Settings vor fi puse în faza 2 (scena Main).
            EditorBuildSettings.scenes = new EditorBuildSettingsScene[0];
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            string parent = Path.GetDirectoryName(path)?.Replace('\\', '/');
            AssetDatabase.CreateFolder(parent, Path.GetFileName(path));
        }

        // ------------------------------------------------------------ 2. Player Settings

        static void ConfigurePlayerSettings()
        {
            PlayerSettings.companyName = "UPT";
            PlayerSettings.productName = "3Times";

            var android = NamedBuildTarget.Android;
            PlayerSettings.SetApplicationIdentifier(android, "com.threetimes.app");
            PlayerSettings.SetScriptingBackend(android, ScriptingImplementation.IL2CPP);
            PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
            // Minimum API Level rămâne cel implicit al Unity 6.3 (peste cerința ARCore, API 24).
            PlayerSettings.Android.applicationEntry = AndroidApplicationEntry.Activity;

            PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
            PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { GraphicsDeviceType.OpenGLES3 });

            PlayerSettings.defaultInterfaceOrientation = UIOrientation.Portrait;

            // Active Input Handling = Both (Input.location și butonul Înapoi folosesc API-ul vechi).
            var projectSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (projectSettings != null)
            {
                var so = new SerializedObject(projectSettings);
                var prop = so.FindProperty("activeInputHandler");
                if (prop != null && prop.intValue != 2)
                {
                    prop.intValue = 2;
                    so.ApplyModifiedPropertiesWithoutUndo();
                    Debug.Log($"{Log} Active Input Handling = Both (necesită repornirea editorului)");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"{Log} Player Settings pentru Android setate.");
        }

        // ------------------------------------------------------------ 3. pachete

        static void InstallPackages()
        {
            Debug.Log($"{Log} Instalez pachetele AR. Poate dura câteva minute…");
            packageRequest = Client.AddAndRemove(PackagesToAdd, PackagesToRemove);
            EditorApplication.update += WaitForPackages;
        }

        static void WaitForPackages()
        {
            if (packageRequest == null || !packageRequest.IsCompleted) return;
            EditorApplication.update -= WaitForPackages;

            if (packageRequest.Status == StatusCode.Success)
            {
                foreach (var p in packageRequest.Result)
                    if (p.name.Contains("xr") || p.name.Contains("arcore"))
                        Debug.Log($"{Log} Pachet: {p.name} {p.version}");

                Debug.Log($"{Log} Gata faza 1. Treci pe Android: File → Build Profiles → Android → Switch Platform, apoi repornește editorul.");
                EditorUtility.DisplayDialog("3Times: faza 1 gata",
                    "Pachetele AR sunt instalate.\n\n" +
                    "1. File → Build Profiles → Android → Switch Platform\n" +
                    "2. Închide și redeschide Unity (pentru Active Input Handling)\n\n" +
                    "Apoi spune-i lui Claude că faza 1 e gata.", "OK");
            }
            else
            {
                Debug.LogError($"{Log} Instalarea pachetelor a eșuat: {packageRequest.Error?.message}");
                EditorUtility.DisplayDialog("3Times: eroare la pachete",
                    packageRequest.Error?.message ?? "Eroare necunoscută. Vezi Console.", "OK");
            }
            packageRequest = null;
        }
    }
}
#endif
