#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.AR;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Formele din particule ale urmelor: creează materialul lor și îl leagă de TraceManager.
    /// Meniu: Tools → 3Times → 4. Adaugă formele din particule
    /// </summary>
    public static class ThreeTimesStep4
    {
        const string Log = "[3Times Setup]";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string TexturePath = "Assets/Textures/particle-soft.png";
        const string MaterialPath = "Assets/Materials/TraceParticle.mat";

        [MenuItem("Tools/3Times/4. Adaugă formele din particule", priority = 4)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var manager = Resources.FindObjectsOfTypeAll<TraceManager>().FirstOrDefault(c => c.gameObject.scene.IsValid());
            if (manager == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc TraceManager în scenă. Rulează întâi „3. Adaugă urmele în scenă”.", "OK");
                return;
            }

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(TexturePath);
            if (texture == null)
            {
                EditorUtility.DisplayDialog("3Times", $"Lipsește {TexturePath}. Apasă Cmd + R și încearcă din nou.", "OK");
                return;
            }

            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            var material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material == null)
            {
                // Sprites/Default: fără lumini, transparent, colorat de fiecare particulă.
                material = new Material(Shader.Find("Sprites/Default"));
                AssetDatabase.CreateAsset(material, MaterialPath);
            }
            material.mainTexture = texture;
            EditorUtility.SetDirty(material);

            var so = new SerializedObject(manager);
            so.FindProperty("particleMaterial").objectReferenceValue = material;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            Debug.Log($"{Log} Materialul particulelor e legat de TraceManager.");

            EditorUtility.DisplayDialog("3Times: formele din particule",
                "Gata. Ca să le vezi fără telefon:\n\n" +
                "1. Apasă Play\n" +
                "2. Tools → 3Times → Arată formele 3D (în Play)", "OK");
        }

        [MenuItem("Tools/3Times/Arată formele 3D (în Play)", priority = 20)]
        public static void Preview()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Apasă întâi Play, apoi alege din nou acest meniu.", "OK");
                return;
            }
            var manager = Object.FindFirstObjectByType<TraceManager>();
            if (manager != null) manager.PreviewShapes();
        }
    }
}
#endif
