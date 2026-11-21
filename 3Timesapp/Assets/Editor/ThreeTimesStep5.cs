#if UNITY_EDITOR
using ThreeTimes.Past;
using UnityEditor;
using UnityEngine;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul: fațada Operei din 1900, generată din cod. Creează materialele și prefab-ul ei, apoi îl deschide
    /// (prefab mode), ca fațada să se vadă în Scene view. Nu modifică scena Main.
    /// Meniu: Tools → 3Times → 5. Trecutul: fațada Operei (1900)
    /// </summary>
    public static class ThreeTimesStep5
    {
        const string Log = "[3Times Setup]";
        const string MaterialFolder = "Assets/Materials/Past";
        const string PrefabPath = "Assets/Prefabs/OperaFacade1900.prefab";

        // Culorile din cărțile poștale colorate de epocă (piatră deschisă, cupru verde).
        static readonly (string name, Color color, float smoothness)[] Materials =
        {
            ("Piatra", Hex("CFC2A6"), 0.08f),
            ("Ancadramente", Hex("E2D8C0"), 0.12f),
            ("Bosaje", Hex("BFB194"), 0.05f),
            ("Ferestre", Hex("3A4650"), 0.65f),
            ("Acoperis", Hex("6E9C84"), 0.35f),
            ("Sculptura", Hex("D9D2C3"), 0.1f),
            ("Umbra", Hex("4A4038"), 0.0f),
        };

        [MenuItem("Tools/3Times/5. Trecutul: fațada Operei (1900)", priority = 5)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }
            var prefab = CreatePrefab();
            if (prefab == null) return;
            AssetDatabase.OpenAsset(prefab);
            EditorUtility.DisplayDialog("3Times: fațada Operei (1900)",
                "Gata. Prefab-ul e deschis: treci pe tabul Scene.\n\n" +
                "Dacă nu vezi fațada: în Hierarchy dă dublu-clic pe „OperaFacade1900” (o încadrează în ecran).\n" +
                "Fațada are ~42 m lățime; rotește vederea cu Alt + tragere.", "OK");
        }

        /// <summary>Creează (sau refreshează) materialele și prefab-ul fațadei. Îl folosește și pasul 6.</summary>
        public static GameObject CreatePrefab()
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc shader-ul URP Lit. E pornit Universal Render Pipeline?", "OK");
                return null;
            }

            EnsureFolder("Assets/Materials");
            EnsureFolder(MaterialFolder);
            EnsureFolder("Assets/Prefabs");

            var mats = new Material[FacadeMesh.MaterialCount];
            for (int i = 0; i < Materials.Length; i++)
            {
                var (name, color, smoothness) = Materials[i];
                string path = $"{MaterialFolder}/Opera1900_{name}.mat";
                var mat = AssetDatabase.LoadAssetAtPath<Material>(path);
                if (mat == null)
                {
                    mat = new Material(shader);
                    AssetDatabase.CreateAsset(mat, path);
                }
                mat.shader = shader;
                mat.SetColor("_BaseColor", color);
                mat.SetFloat("_Smoothness", smoothness);
                mat.SetFloat("_Metallic", 0f);
                EditorUtility.SetDirty(mat);
                mats[i] = mat;
            }

            var go = new GameObject("OperaFacade1900");
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>();
            var view = go.AddComponent<FacadeView>();
            view.building = FacadeView.Building.Opera1900;
            view.materials = mats;
            go.GetComponent<MeshRenderer>().sharedMaterials = mats;
            // Plasa se generează la deschidere; nu o salvăm în prefab (nu e un asset).
            go.GetComponent<MeshFilter>().sharedMesh = null;
            var prefab = PrefabUtility.SaveAsPrefabAsset(go, PrefabPath);
            Object.DestroyImmediate(go);
            AssetDatabase.SaveAssets();
            Debug.Log($"{Log} Fațada Operei (1900): {PrefabPath}, {OperaFacade1900.Build().TriangleCount} triunghiuri.");
            return prefab;
        }

        static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path)) return;
            int slash = path.LastIndexOf('/');
            AssetDatabase.CreateFolder(path.Substring(0, slash), path.Substring(slash + 1));
        }

        static Color Hex(string hex) => ColorUtility.TryParseHtmlString("#" + hex, out var c) ? c : Color.magenta;
    }
}
#endif
