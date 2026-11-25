#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.Past;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul, palatele din 1912–1914 (anul după direcția privirii):
    /// - setează texturile din Assets/Textures/Past (firme, copertine, pavaj);
    /// - creează materialele palatelor (copertine, firme, pavaj, lumina felinarelor);
    /// - adaugă PastEra pe App, legat de PastManager, PastTransition, ecranul principal și camera AR; corpul
    ///   palatelor folosește materialele fațadei Operei.
    /// Are nevoie de pașii 6–8. Meniu: Tools → 3Times → 9. Trecutul: palatele și anul după privire
    /// </summary>
    public static class ThreeTimesStep9
    {
        const string Log = "[3Times Setup]";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string TexFolder = "Assets/Textures/Past";
        const string MatFolder = "Assets/Materials/Past";

        [MenuItem("Tools/3Times/9. Trecutul: palatele și anul după privire", priority = 9)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }

            // 1. Texturile
            var signs = Texture($"{TexFolder}/PalaceSigns.png", TextureWrapMode.Clamp);
            var awning = Texture($"{TexFolder}/Awning.png", TextureWrapMode.Repeat);
            var paving = Texture($"{TexFolder}/Paving.png", TextureWrapMode.Repeat);
            if (signs == null || awning == null || paving == null)
            {
                EditorUtility.DisplayDialog("3Times", $"Lipsesc texturile din {TexFolder} (PalaceSigns, Awning, Paving).", "OK");
                return;
            }

            // 2. Materialele
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            var unlit = Shader.Find("Universal Render Pipeline/Unlit");
            var pavingShader = Shader.Find("ThreeTimes/PastPaving");
            if (lit == null || unlit == null || pavingShader == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc shader-ele URP Lit / Unlit sau ThreeTimes/PastPaving.", "OK");
                return;
            }
            var awningMat = Material($"{MatFolder}/PalaceAwning.mat", lit, m =>
            {
                m.SetTexture("_BaseMap", awning);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.1f);
                m.SetFloat("_Cull", 0f);
            });
            var signMat = Material($"{MatFolder}/PalaceSign.mat", lit, m =>
            {
                m.SetTexture("_BaseMap", signs);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.25f);
            });
            var pavingMat = Material($"{MatFolder}/PalacePaving.mat", pavingShader, m =>
            {
                m.SetTexture("_BaseMap", paving);
            });
            var glowMat = Material($"{MatFolder}/LampGlow.mat", unlit, m =>
            {
                m.SetColor("_BaseColor", new Color(1f, 0.86f, 0.55f));
            });
            AssetDatabase.SaveAssets();

            // 3. Scena
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var past = app != null ? app.GetComponent<PastManager>() : null;
            var transition = app != null ? app.GetComponent<PastTransition>() : null;
            var cameraManager = Resources.FindObjectsOfTypeAll<ARCameraManager>().FirstOrDefault(c => c.gameObject.scene == scene);
            var arCamera = cameraManager != null ? cameraManager.GetComponent<Camera>() : null;
            if (app == null || past == null || transition == null || arCamera == null)
            {
                EditorUtility.DisplayDialog("3Times",
                    "Nu găsesc App cu PastManager și PastTransition, sau camera AR, în scena Main. Rulează întâi pașii 6–8.", "OK");
                return;
            }

            var pastSo = new SerializedObject(past);
            var prefab = pastSo.FindProperty("facadePrefab").objectReferenceValue as GameObject;
            var facadeView = prefab != null ? prefab.GetComponent<FacadeView>() : null;
            if (facadeView == null || facadeView.materials == null || facadeView.materials.Length != FacadeMesh.MaterialCount)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc materialele fațadei Operei (prefab-ul din pasul 5).", "OK");
                return;
            }

            var era = app.GetComponent<PastEra>();
            if (era == null) era = app.AddComponent<PastEra>();
            var so = new SerializedObject(era);
            so.FindProperty("past").objectReferenceValue = past;
            so.FindProperty("transition").objectReferenceValue = transition;
            so.FindProperty("mainView").objectReferenceValue = pastSo.FindProperty("mainView").objectReferenceValue;
            so.FindProperty("router").objectReferenceValue = pastSo.FindProperty("router").objectReferenceValue;
            so.FindProperty("tracker").objectReferenceValue = pastSo.FindProperty("tracker").objectReferenceValue;
            so.FindProperty("anchorManager").objectReferenceValue = pastSo.FindProperty("anchorManager").objectReferenceValue;
            so.FindProperty("arCamera").objectReferenceValue = arCamera;
            var body = so.FindProperty("bodyMaterials");
            body.arraySize = FacadeMesh.MaterialCount;
            for (int i = 0; i < FacadeMesh.MaterialCount; i++) body.GetArrayElementAtIndex(i).objectReferenceValue = facadeView.materials[i];
            so.FindProperty("awningMaterial").objectReferenceValue = awningMat;
            so.FindProperty("signMaterial").objectReferenceValue = signMat;
            so.FindProperty("pavingMaterial").objectReferenceValue = pavingMat;
            so.FindProperty("glowMaterial").objectReferenceValue = glowMat;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{Log} Trecutul: {PalaceCatalog.Palaces.Length} palate, anul după privire.");
            EditorUtility.DisplayDialog("3Times: palatele",
                "Gata. Pe telefon, în Trecut (după ce apare Opera din 1900):\n" +
                "• privește un palat cam o secundă: anul se derulează până la anul lui și palatele apar, cu vitrine, " +
                "copertine, firme, felinare și pavaj;\n" +
                "• apare o notiță despre palat;\n" +
                "• spre latura de est și spre Catedrală apare doar notița „aici nu era încă nimic”;\n" +
                "• atinge anul de sus ca să revii la 1900.", "OK");
        }

        static Texture2D Texture(string path, TextureWrapMode wrap)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) return null;
            importer.textureType = TextureImporterType.Default;
            importer.sRGBTexture = true;
            importer.mipmapEnabled = true;
            importer.wrapMode = wrap;
            importer.anisoLevel = 4;
            importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Material(string path, Shader shader, System.Action<Material> setup)
        {
            EnsureFolder("Assets/Materials");
            EnsureFolder(MatFolder);
            var material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            setup(material);
            EditorUtility.SetDirty(material);
            return material;
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
