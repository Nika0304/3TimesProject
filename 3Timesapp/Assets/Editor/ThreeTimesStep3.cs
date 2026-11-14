#if UNITY_EDITOR
using System.Linq;
using Google.XR.ARCoreExtensions;
using ThreeTimes.AR;
using ThreeTimes.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Pașii 3–4: adaugă în scena Main urmele (marcajul, materialul lui și componenta
    /// TraceManager) și trece proiectul pe noul Input System.
    /// Meniu: Tools → 3Times → 3. Adaugă urmele în scenă
    /// </summary>
    public static class ThreeTimesStep3
    {
        const string Log = "[3Times Setup]";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string MaterialPath = "Assets/Materials/TraceMarker.mat";
        const string PrefabPath = "Assets/Prefabs/TraceMarker.prefab";

        [MenuItem("Tools/3Times/3. Adaugă urmele în scenă", priority = 3)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var anchorManager = FindInScene<ARAnchorManager>();
            var earthManager = FindInScene<AREarthManager>();
            if (app == null || anchorManager == null || earthManager == null)
            {
                EditorUtility.DisplayDialog("3Times",
                    "Nu găsesc App, AR Anchor Manager sau AR Earth Manager în scena Main. " +
                    "Rulează întâi „2. Construiește scena Main”.", "OK");
                return;
            }

            var prefab = CreateMarkerPrefab();

            var manager = app.GetComponent<TraceManager>();
            if (manager == null) manager = app.AddComponent<TraceManager>();
            SetRef(manager, "anchorManager", anchorManager);
            SetRef(manager, "earthManager", earthManager);
            SetRef(manager, "tracker", app.GetComponent<GeospatialTracker>());
            SetRef(manager, "mainView", app.GetComponent<MainScreenView>());
            SetRef(manager, "router", app.GetComponent<UIRouter>());
            SetRef(manager, "markerPrefab", prefab);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            bool inputChanged = UseNewInputSystemOnly();
            AssetDatabase.SaveAssets();

            EditorUtility.DisplayDialog("3Times: pasul 3 gata",
                "Urmele sunt legate în scenă.\n\n" +
                (inputChanged
                    ? "Am trecut Active Input Handling pe „Input System Package”. Închide și redeschide Unity, apoi Build And Run.\n\n"
                    : "Poți da Build And Run.\n\n") +
                "Pornește camera → așteaptă „Localizat” → + Lasă o urmă.", "OK");
        }

        static T FindInScene<T>() where T : Component =>
            Resources.FindObjectsOfTypeAll<T>().FirstOrDefault(c => c.gameObject.scene.IsValid());

        static GameObject CreateMarkerPrefab()
        {
            EnsureFolder("Assets/Materials");
            EnsureFolder("Assets/Prefabs");

            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.SetColor("_BaseColor", new Color(1f, 0.541f, 0.431f)); // corai #FF8A6E
            EditorUtility.SetDirty(material);

            // Marcajul: o sferă de 30 cm pe un băț subțire, ca să se vadă și de la distanță.
            var root = new GameObject("TraceMarker");

            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Object.DestroyImmediate(sphere.GetComponent<Collider>());
            sphere.name = "Sphere";
            sphere.transform.SetParent(root.transform, false);
            sphere.transform.localScale = Vector3.one * 0.3f;
            sphere.GetComponent<MeshRenderer>().sharedMaterial = material;

            var pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            Object.DestroyImmediate(pole.GetComponent<Collider>());
            pole.name = "Pole";
            pole.transform.SetParent(root.transform, false);
            pole.transform.localScale = new Vector3(0.02f, 0.5f, 0.02f); // cilindrul are 2 m înălțime la scară 1
            pole.transform.localPosition = new Vector3(0f, -0.65f, 0f);
            pole.GetComponent<MeshRenderer>().sharedMaterial = material;

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            Object.DestroyImmediate(root);
            Debug.Log($"{Log} Prefab creat: {PrefabPath}");
            return prefab;
        }

        /// <summary>Codul nu mai folosește vechiul Input Manager, deci scăpăm de avertismentul de la build.</summary>
        static bool UseNewInputSystemOnly()
        {
            var settings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset").FirstOrDefault();
            if (settings == null) return false;
            var so = new SerializedObject(settings);
            var prop = so.FindProperty("activeInputHandler");
            if (prop == null || prop.intValue == 1) return false;
            prop.intValue = 1; // 0 = vechi, 1 = Input System Package, 2 = ambele
            so.ApplyModifiedPropertiesWithoutUndo();
            Debug.Log($"{Log} Active Input Handling = Input System Package (necesită repornirea editorului)");
            return true;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            AssetDatabase.CreateFolder(System.IO.Path.GetDirectoryName(path), System.IO.Path.GetFileName(path));
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
