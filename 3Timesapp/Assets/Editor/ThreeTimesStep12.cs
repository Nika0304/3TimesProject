#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.Past;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul, „developarea” din granulație: trece materialele clădirilor din Trecut (fațada Operei, palatele,
    /// Catedrala: Opera1900_*, PalaceAwning, PalaceSign, CathedralTiles, Flag) pe shaderul ThreeTimes/PastLit, care le
    /// face să apară din granulație, de jos în sus, cu o margine aurie. Culorile, texturile și luciul rămân.
    /// Leagă și „cerul pictat” de PastTransition, ca Opera de azi să se dizolve în particule înainte.
    /// Meniu: Tools → 3Times → 12. Trecutul: clădirile apar din granulație
    /// </summary>
    public static class ThreeTimesStep12
    {
        static readonly string[] Materials =
        {
            "Opera1900_Piatra", "Opera1900_Ancadramente", "Opera1900_Bosaje", "Opera1900_Ferestre",
            "Opera1900_Acoperis", "Opera1900_Sculptura", "Opera1900_Umbra",
            "PalaceAwning", "PalaceSign", "CathedralTiles", "Flag",
        };

        [MenuItem("Tools/3Times/12. Trecutul: clădirile apar din granulație", priority = 12)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }
            var shader = Shader.Find("ThreeTimes/PastLit");
            if (shader == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc shaderul ThreeTimes/PastLit (Assets/Shaders).", "OK");
                return;
            }

            int done = 0;
            var missing = new System.Collections.Generic.List<string>();
            foreach (var name in Materials)
            {
                var material = AssetDatabase.LoadAssetAtPath<Material>($"Assets/Materials/Past/{name}.mat");
                if (material == null) { missing.Add(name); continue; }
                // Proprietățile cu același nume (_BaseColor, _BaseMap cu repetarea ei, _Smoothness) rămân.
                var color = material.HasProperty("_BaseColor") ? material.GetColor("_BaseColor") : Color.white;
                var map = material.HasProperty("_BaseMap") ? material.GetTexture("_BaseMap") : null;
                var scale = material.HasProperty("_BaseMap") ? material.GetTextureScale("_BaseMap") : Vector2.one;
                float smooth = material.HasProperty("_Smoothness") ? material.GetFloat("_Smoothness") : 0.1f;
                material.shader = shader;
                material.SetColor("_BaseColor", color);
                if (map != null) { material.SetTexture("_BaseMap", map); material.SetTextureScale("_BaseMap", scale); }
                material.SetFloat("_Smoothness", name == "CathedralTiles" ? smooth : Mathf.Min(smooth, 0.25f));
                material.SetFloat("_Reveal", 1f);
                material.SetFloat("_Cull", name == "Flag" || name == "PalaceAwning" ? 0f : 2f);
                EditorUtility.SetDirty(material);
                done++;
            }
            AssetDatabase.SaveAssets();

            // Opera de azi se dizolvă: PastTransition are nevoie de materialul cerului.
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene("Assets/Scenes/Main.unity", OpenSceneMode.Single);
            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var transition = app != null ? app.GetComponent<PastTransition>() : null;
            var sky = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/Past/SkyMask.mat");
            if (transition != null && sky != null)
            {
                var so = new SerializedObject(transition);
                so.FindProperty("skyMaterial").objectReferenceValue = sky;
                so.ApplyModifiedPropertiesWithoutUndo();
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
            }
            else missing.Add("PastTransition sau SkyMask.mat (pașii 7–8)");
            Debug.Log($"[3Times Setup] Trecutul: {done} materiale pe ThreeTimes/PastLit (developare din granulație).");
            EditorUtility.DisplayDialog("3Times: developarea",
                $"Gata: {done} materiale trecute pe ThreeTimes/PastLit." +
                (missing.Count > 0 ? $"\nLipsesc: {string.Join(", ", missing)} (rulează pașii 5, 9 și 10)." : "") +
                "\n\nPe telefon, la intrarea în Trecut: bliț, Opera de azi se dizolvă în particule, apoi fațada din 1900 " +
                "se developează din granulație, de jos în sus, cu o margine aurie. Palatele și Catedrala apar la fel.", "OK");
        }
    }
}
#endif
