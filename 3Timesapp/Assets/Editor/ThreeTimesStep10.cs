#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.Past;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul, Catedrala în trei momente (1936 temelia, 1938 schelele, 1946 sfințirea):
    /// - setează texturile RoofTiles (țigla smălțuită) și Flag (steagul) din Assets/Textures/Past;
    /// - creează materialele CathedralTiles (lucios) și Flag;
    /// - le leagă de PastEra, împreună cu materialul „cerului pictat” (din pasul 7).
    /// Are nevoie de pașii 7–9. Meniu: Tools → 3Times → 10. Trecutul: Catedrala
    /// </summary>
    public static class ThreeTimesStep10
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string TexFolder = "Assets/Textures/Past";
        const string MatFolder = "Assets/Materials/Past";
        const string SkyPath = "Assets/Materials/Past/SkyMask.mat";

        [MenuItem("Tools/3Times/10. Trecutul: Catedrala (1936, 1938, 1946)", priority = 10)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }

            var tiles = Texture($"{TexFolder}/RoofTiles.png", TextureWrapMode.Repeat);
            var flag = Texture($"{TexFolder}/Flag.png", TextureWrapMode.Clamp);
            var sky = AssetDatabase.LoadAssetAtPath<Material>(SkyPath);
            var lit = Shader.Find("Universal Render Pipeline/Lit");
            if (tiles == null || flag == null || sky == null || lit == null)
            {
                EditorUtility.DisplayDialog("3Times",
                    $"Lipsesc RoofTiles.png / Flag.png din {TexFolder} sau {SkyPath} (pasul 7).", "OK");
                return;
            }

            var tilesMat = Material($"{MatFolder}/CathedralTiles.mat", lit, m =>
            {
                m.SetTexture("_BaseMap", tiles);
                m.SetTextureScale("_BaseMap", new Vector2(0.5f, 0.5f)); // textura acoperă 2 × 2 m
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.78f);                        // smălțuită: lucește
                m.SetFloat("_Metallic", 0f);
            });
            var flagMat = Material($"{MatFolder}/Flag.mat", lit, m =>
            {
                m.SetTexture("_BaseMap", flag);
                m.SetColor("_BaseColor", Color.white);
                m.SetFloat("_Smoothness", 0.1f);
            });
            AssetDatabase.SaveAssets();

            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var era = app != null ? app.GetComponent<PastEra>() : null;
            if (era == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc PastEra pe App. Rulează întâi pasul 9.", "OK");
                return;
            }
            var so = new SerializedObject(era);
            so.FindProperty("tilesMaterial").objectReferenceValue = tilesMat;
            so.FindProperty("flagMaterial").objectReferenceValue = flagMat;
            so.FindProperty("skyMaterial").objectReferenceValue = sky;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[3Times Setup] Trecutul: Catedrala în trei momente.");
            EditorUtility.DisplayDialog("3Times: Catedrala",
                "Gata. Pe telefon, în Trecut, privește spre Catedrală cam o secundă:\n" +
                "• 1936: temelia, gardul șantierului, piatra de temelie (Catedrala de azi e acoperită cu „cer”);\n" +
                "• 1938: zidurile și turlele în schele;\n" +
                "• 1946: Catedrala sfințită, cu țigla smălțuită, cruci aurite și steaguri.\n" +
                "De la 1920 încolo, fațada Operei din 1900 dispare (Opera arăta deja ca azi).\n\n" +
                "Previzualizare: Tools → 3Times → Previzualizare: Piața Victoriei în Trecut.", "OK");
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
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<Texture2D>(path);
        }

        static Material Material(string path, Shader shader, System.Action<Material> setup)
        {
            if (!AssetDatabase.IsValidFolder("Assets/Materials")) AssetDatabase.CreateFolder("Assets", "Materials");
            if (!AssetDatabase.IsValidFolder(MatFolder)) AssetDatabase.CreateFolder("Assets/Materials", "Past");
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
    }
}
#endif
