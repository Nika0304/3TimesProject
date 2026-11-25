#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.Past;
using ThreeTimes.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul, „trecerea în timp” și filmul vechi:
    /// - creează materialele ThreeTimes/OldFilm (zgârieturi, praf, pâlpâire) și ThreeTimes/GoldDust (praful auriu);
    /// - adaugă PastFilm și PastTransition pe App, legate de PastManager, PastLook, ecranul principal și camera AR.
    /// Are nevoie de pasul 7 (sepia). Meniu: Tools → 3Times → 8. Trecutul: trecerea în timp și filmul vechi
    /// </summary>
    public static class ThreeTimesStep8
    {
        const string Log = "[3Times Setup]";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string FilmPath = "Assets/Materials/Past/OldFilm.mat";
        const string DustPath = "Assets/Materials/Past/GoldDust.mat";

        [MenuItem("Tools/3Times/8. Trecutul: trecerea în timp și filmul vechi", priority = 8)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }

            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Materials/Past");
            var film = MaterialFor("ThreeTimes/OldFilm", FilmPath);
            var dust = MaterialFor("ThreeTimes/GoldDust", DustPath);
            if (film == null || dust == null) return;
            AssetDatabase.SaveAssets();

            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var past = app != null ? app.GetComponent<PastManager>() : null;
            var look = app != null ? app.GetComponent<PastLook>() : null;
            var cameraManager = Resources.FindObjectsOfTypeAll<ARCameraManager>()
                .FirstOrDefault(c => c.gameObject.scene == scene);
            var arCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : null;
            if (app == null || past == null || look == null || arCamera == null)
            {
                EditorUtility.DisplayDialog("3Times",
                    "Nu găsesc App cu PastManager și PastLook, sau camera AR, în scena Main. " +
                    "Rulează întâi pașii 6 și 7.", "OK");
                return;
            }
            var mainView = new SerializedObject(past).FindProperty("mainView").objectReferenceValue as MainScreenView;
            if (mainView == null) mainView = Resources.FindObjectsOfTypeAll<MainScreenView>()
                .FirstOrDefault(v => v.gameObject.scene == scene);

            var pastFilm = app.GetComponent<PastFilm>();
            if (pastFilm == null) pastFilm = app.AddComponent<PastFilm>();
            Set(pastFilm, "look", look);
            Set(pastFilm, "arCamera", arCamera);
            Set(pastFilm, "filmMaterial", film);

            var transition = app.GetComponent<PastTransition>();
            if (transition == null) transition = app.AddComponent<PastTransition>();
            Set(transition, "past", past);
            Set(transition, "film", pastFilm);
            Set(transition, "mainView", mainView);
            Set(transition, "dustMaterial", dust);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{Log} Trecutul: trecerea în timp și filmul vechi adăugate.");
            EditorUtility.DisplayDialog("3Times: trecerea în timp",
                "Gata. Pe telefon, când apare fațada:\n" +
                "• blițul proiectorului și o pâlpâire puternică;\n" +
                "• anul de sus se derulează până la 1900;\n" +
                "• fațada se țese din praf auriu, de jos în sus;\n" +
                "• apoi rămân zgârieturile, praful și pâlpâirea filmului vechi.\n\n" +
                "Sunetele de proiector se pun mai târziu, pe componenta PastTransition.", "OK");
        }

        static Material MaterialFor(string shaderName, string path)
        {
            var shader = Shader.Find(shaderName);
            if (shader == null)
            {
                EditorUtility.DisplayDialog("3Times", $"Nu găsesc shader-ul {shaderName} (în Assets/Shaders).", "OK");
                return null;
            }
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            EditorUtility.SetDirty(material);
            return material;
        }

        static void Set(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"{Log} Câmpul {field} lipsește din {target.GetType().Name}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }
    }
}
#endif
