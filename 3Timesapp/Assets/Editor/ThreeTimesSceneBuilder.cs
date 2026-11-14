#if UNITY_EDITOR
using System.Linq;
using Google.XR.ARCoreExtensions;
using Google.XR.ARCoreExtensions.Internal;
using ThreeTimes.AR;
using ThreeTimes.Core;
using ThreeTimes.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;

#pragma warning disable 618 // ARSessionOrigin e marcat învechit, dar ARCore Extensions 1.57 încă îl cere

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Faza 2: activează Geospatial, adaugă fundalul AR în URP, creează Panel Settings
    /// și construiește scena Main cu toată ierarhia.
    /// Meniu: Tools → 3Times → 2. Construiește scena Main
    /// </summary>
    public static class ThreeTimesSceneBuilder
    {
        const string Log = "[3Times Setup]";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string ConfigPath = "Assets/Settings/ARCoreExtensionsConfig.asset";
        const string PanelSettingsPath = "Assets/UI/AppPanelSettings.asset";
        const string ThemePath = "Assets/UI/AppTheme.tss";
        const string UxmlPath = "Assets/UI/App.uxml";

        [MenuItem("Tools/3Times/2. Construiește scena Main", priority = 2)]
        public static void Run()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                !EditorUtility.DisplayDialog("3Times", "Scena Main există deja. O reconstruiesc de la zero?", "Da", "Anulează"))
                return;

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            EnableGeospatial();
            var config = CreateExtensionsConfig();
            AddArBackgroundFeature("Assets/Settings/Mobile_Renderer.asset");
            AddArBackgroundFeature("Assets/Settings/PC_Renderer.asset");
            var panelSettings = CreatePanelSettings();
            BuildScene(config, panelSettings);

            AssetDatabase.SaveAssets();
            EditorUtility.DisplayDialog("3Times: faza 2 gata",
                "Scena Main e construită și pusă în Build Profiles.\n\n" +
                "Mai ai de făcut manual:\n" +
                "1. Project Settings → XR Plug-in Management → tab-ul Android → bifează Google ARCore\n" +
                "2. Project Settings → XR Plug-in Management → ARCore Extensions → lipește cheia API\n\n" +
                "Apoi apasă Play ca să vezi ecranele în modul simulare.", "OK");
        }

        // ------------------------------------------------------------ ARCore Extensions

        static void EnableGeospatial()
        {
            var settings = ARCoreExtensionsProjectSettings.Instance;
            settings.GeospatialEnabled = true;
            settings.Save();
            Debug.Log($"{Log} Geospatial activat în ARCore Extensions.");
        }

        static ARCoreExtensionsConfig CreateExtensionsConfig()
        {
            var config = AssetDatabase.LoadAssetAtPath<ARCoreExtensionsConfig>(ConfigPath);
            if (config == null)
            {
                config = ScriptableObject.CreateInstance<ARCoreExtensionsConfig>();
                AssetDatabase.CreateAsset(config, ConfigPath);
            }
            config.GeospatialMode = GeospatialMode.Enabled;
            EditorUtility.SetDirty(config);
            Debug.Log($"{Log} {ConfigPath} cu Geospatial Mode = Enabled.");
            return config;
        }

        // ------------------------------------------------------------ URP

        static void AddArBackgroundFeature(string rendererPath)
        {
            var data = AssetDatabase.LoadAssetAtPath<ScriptableRendererData>(rendererPath);
            if (data == null) { Debug.LogWarning($"{Log} Nu găsesc {rendererPath}"); return; }
            if (data.rendererFeatures.Any(f => f is ARBackgroundRendererFeature)) return;

            var feature = ScriptableObject.CreateInstance<ARBackgroundRendererFeature>();
            feature.name = "ARBackgroundRendererFeature";
            AssetDatabase.AddObjectToAsset(feature, data);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(feature, out string _, out long localId);

            var so = new SerializedObject(data);
            var features = so.FindProperty("m_RendererFeatures");
            var map = so.FindProperty("m_RendererFeatureMap");
            features.arraySize++;
            features.GetArrayElementAtIndex(features.arraySize - 1).objectReferenceValue = feature;
            map.arraySize++;
            map.GetArrayElementAtIndex(map.arraySize - 1).longValue = localId;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorUtility.SetDirty(data);
            Debug.Log($"{Log} AR Background Renderer Feature adăugat în {rendererPath}");
        }

        // ------------------------------------------------------------ UI Toolkit

        static PanelSettings CreatePanelSettings()
        {
            var panel = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panel == null)
            {
                panel = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panel, PanelSettingsPath);
            }
            panel.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(ThemePath);
            panel.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panel.referenceResolution = new Vector2Int(1080, 1920);
            panel.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panel.match = 0f;
            panel.clearColor = false;
            EditorUtility.SetDirty(panel);
            return panel;
        }

        // ------------------------------------------------------------ scena

        static void BuildScene(ARCoreExtensionsConfig config, PanelSettings panelSettings)
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            // Lumină pentru obiectele 3D (urme, fațade)
            var light = new GameObject("Directional Light").AddComponent<Light>();
            light.type = LightType.Directional;
            light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // ---- AR (inactiv până trec permisiunile)
            var ar = new GameObject("AR");

            var sessionGo = new GameObject("AR Session", typeof(ARSession), typeof(ARInputManager));
            sessionGo.transform.SetParent(ar.transform, false);

            var originGo = new GameObject("AR Session Origin", typeof(ARSessionOrigin),
                typeof(AREarthManager), typeof(ARAnchorManager));
            originGo.transform.SetParent(ar.transform, false);

            var offsetGo = new GameObject("Camera Offset");
            offsetGo.transform.SetParent(originGo.transform, false);

            var cameraGo = new GameObject("AR Camera", typeof(Camera), typeof(AudioListener),
                typeof(ARCameraManager), typeof(ARCameraBackground), typeof(TrackedPoseDriver));
            cameraGo.transform.SetParent(offsetGo.transform, false);
            cameraGo.tag = "MainCamera";
            var camera = cameraGo.GetComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.09f, 0.086f, 0.1f); // în Editor nu există imaginea camerei
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 1000f; // clădirile pot fi la zeci-sute de metri

            var poseDriver = cameraGo.GetComponent<TrackedPoseDriver>();
            var position = new InputAction("Position", binding: "<XRHMD>/centerEyePosition", expectedControlType: "Vector3");
            position.AddBinding("<HandheldARInputDevice>/devicePosition");
            var rotation = new InputAction("Rotation", binding: "<XRHMD>/centerEyeRotation", expectedControlType: "Quaternion");
            rotation.AddBinding("<HandheldARInputDevice>/deviceRotation");
            poseDriver.positionInput = new InputActionProperty(position);
            poseDriver.rotationInput = new InputActionProperty(rotation);

            var origin = originGo.GetComponent<ARSessionOrigin>();
            origin.Camera = camera;
            origin.CameraFloorOffsetObject = offsetGo;

            var extensionsGo = new GameObject("ARCore Extensions", typeof(ARCoreExtensions));
            extensionsGo.transform.SetParent(ar.transform, false);
            var extensions = extensionsGo.GetComponent<ARCoreExtensions>();
            extensions.Session = sessionGo.GetComponent<ARSession>();
            extensions.SessionOrigin = origin;
            extensions.CameraManager = cameraGo.GetComponent<ARCameraManager>();
            extensions.ARCoreExtensionsConfig = config;

            ar.SetActive(false);

            // ---- App (interfață și logică)
            var app = new GameObject("App", typeof(UIDocument), typeof(UIRouter),
                typeof(GeospatialTracker), typeof(MainScreenView), typeof(AppController));

            var document = app.GetComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);

            SetRef(app.GetComponent<GeospatialTracker>(), "earthManager", originGo.GetComponent<AREarthManager>());

            var mainView = app.GetComponent<MainScreenView>();
            SetRef(mainView, "router", app.GetComponent<UIRouter>());
            SetRef(mainView, "tracker", app.GetComponent<GeospatialTracker>());

            var controller = app.GetComponent<AppController>();
            SetRef(controller, "ui", app.GetComponent<UIRouter>());
            SetRef(controller, "mainView", mainView);
            SetRef(controller, "tracker", app.GetComponent<GeospatialTracker>());
            SetRef(controller, "arRoot", ar);

            EditorSceneManager.SaveScene(scene, ScenePath);
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
            Debug.Log($"{Log} Scena {ScenePath} salvată și pusă în Build Profiles.");
        }

        static void SetRef(Object target, string field, Object value)
        {
            var so = new SerializedObject(target);
            var prop = so.FindProperty(field);
            if (prop == null) { Debug.LogError($"{Log} Câmpul {field} lipsește din {target.GetType().Name}"); return; }
            prop.objectReferenceValue = value;
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
#endif
