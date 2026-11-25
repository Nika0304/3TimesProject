#if UNITY_EDITOR
using System.Linq;
using Google.XR.ARCoreExtensions;
using ThreeTimes.Past;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.XR.ARFoundation;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul, aspectul de epocă și „cerul pictat”:
    /// - pornește Streetscape Geometry în configurația ARCore Extensions (blocurile 3D ale clădirilor);
    /// - adaugă ARStreetscapeGeometryManager pe AR Session Origin;
    /// - creează materialul ThreeTimes/SkyMask și profilul de efecte (sepia, granulație, vignetă) + un Volume global;
    /// - adaugă PastLook și PastSkyMask pe App, legate între ele.
    /// Meniu: Tools → 3Times → 7. Trecutul: sepia și cerul pictat
    /// </summary>
    public static class ThreeTimesStep7
    {
        const string Log = "[3Times Setup]";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string ConfigPath = "Assets/Settings/ARCoreExtensionsConfig.asset";
        const string MaterialPath = "Assets/Materials/Past/SkyMask.mat";
        const string ProfilePath = "Assets/Settings/PastLookProfile.asset";

        [MenuItem("Tools/3Times/7. Trecutul: sepia și cerul pictat", priority = 7)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }

            // 1. Streetscape Geometry în configurația ARCore
            var config = AssetDatabase.LoadAssetAtPath<ARCoreExtensionsConfig>(ConfigPath);
            if (config == null)
            {
                EditorUtility.DisplayDialog("3Times", $"Nu găsesc {ConfigPath}.", "OK");
                return;
            }
            config.StreetscapeGeometryMode = StreetscapeGeometryMode.Enabled;
            EditorUtility.SetDirty(config);

            // 2. Materialul cerului
            var shader = Shader.Find("ThreeTimes/SkyMask");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc shader-ul ThreeTimes/SkyMask (Assets/Shaders/SkyMask.shader).", "OK");
                return;
            }
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Materials/Past");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.shader = shader;

            // 3. Profilul de efecte
            var profile = AssetDatabase.LoadAssetAtPath<VolumeProfile>(ProfilePath);
            if (profile == null)
            {
                profile = ScriptableObject.CreateInstance<VolumeProfile>();
                AssetDatabase.CreateAsset(profile, ProfilePath);
            }
            var color = Get<ColorAdjustments>(profile);
            color.saturation.Override(-65f);
            color.contrast.Override(12f);
            color.postExposure.Override(-0.1f);
            color.colorFilter.Override(new Color(1f, 0.87f, 0.68f));
            var grain = Get<FilmGrain>(profile);
            grain.type.Override(FilmGrainLookup.Medium3);
            grain.intensity.Override(0.5f);
            grain.response.Override(0.75f);
            var vignette = Get<Vignette>(profile);
            vignette.intensity.Override(0.38f);
            vignette.smoothness.Override(0.55f);
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            // 4. Scena
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var past = app != null ? app.GetComponent<PastManager>() : null;
#pragma warning disable CS0618
            var origin = Find<ARSessionOrigin>(scene);
#pragma warning restore CS0618
            var cameraManager = Find<ARCameraManager>(scene);
            var sun = Resources.FindObjectsOfTypeAll<Light>()
                .FirstOrDefault(l => l.gameObject.scene == scene && l.type == LightType.Directional);
            if (app == null || past == null || origin == null || cameraManager == null)
            {
                EditorUtility.DisplayDialog("3Times",
                    "Nu găsesc App cu PastManager, AR Session Origin sau AR Camera Manager în scena Main. " +
                    "Rulează întâi pasul 6.", "OK");
                return;
            }

            var streetscape = origin.GetComponent<ARStreetscapeGeometryManager>();
            if (streetscape == null) streetscape = origin.gameObject.AddComponent<ARStreetscapeGeometryManager>();

            var volumeGo = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "Past Look Volume");
            if (volumeGo == null) volumeGo = new GameObject("Past Look Volume");
            var volume = volumeGo.GetComponent<Volume>();
            if (volume == null) volume = volumeGo.AddComponent<Volume>();
            volume.isGlobal = true;
            volume.priority = 10f;
            volume.weight = 0f;
            volume.sharedProfile = profile;

            var look = app.GetComponent<PastLook>();
            if (look == null) look = app.AddComponent<PastLook>();
            Set(look, "past", past);
            Set(look, "cameraManager", cameraManager);
            Set(look, "volume", volume);
            Set(look, "sun", sun);

            var mask = app.GetComponent<PastSkyMask>();
            if (mask == null) mask = app.AddComponent<PastSkyMask>();
            Set(mask, "past", past);
            Set(mask, "look", look);
            Set(mask, "streetscape", streetscape);
            Set(mask, "sessionOrigin", origin);
            Set(mask, "maskMaterial", material);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{Log} Trecutul: Streetscape Geometry pornit, sepia + cerul pictat adăugate.");
            EditorUtility.DisplayDialog("3Times: sepia și cerul pictat",
                "Gata. Pe telefon, în Trecut:\n" +
                "• imaginea trece în sepia, cu granulație;\n" +
                "• partea Operei de azi care iese din fațada veche e acoperită cu „cer”.\n\n" +
                "În Jurnalul de test apare de unde vine forma clădirii (ARCore sau cutia de rezervă).", "OK");
        }

        static T Get<T>(VolumeProfile profile) where T : VolumeComponent
        {
            if (!profile.TryGet(out T component))
            {
                component = profile.Add<T>(true);
                component.name = typeof(T).Name;
                AssetDatabase.AddObjectToAsset(component, profile);
            }
            component.active = true;
            return component;
        }

        static T Find<T>(UnityEngine.SceneManagement.Scene scene) where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(c => c.gameObject.scene == scene);

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
