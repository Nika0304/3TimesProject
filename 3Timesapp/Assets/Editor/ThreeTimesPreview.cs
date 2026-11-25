#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.Past;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Previzualizarea Pieței Victoriei în Trecut, direct în Scene view: Opera din 1900, palatele din 1912–1914 și
    /// Catedrala sfințită (1946),
    /// fiecare la locul lui (din coordonatele GPS, față de mijlocul fațadei Operei), pe un teren gri. Nimic din ce
    /// apare nu se salvează în scenă. Meniu: Tools → 3Times → Previzualizare: Piața Victoriei / ascunde.
    /// </summary>
    public static class ThreeTimesPreview
    {
        const string RootName = "Previzualizare Piața Victoriei (nu se salvează)";
        const double OperaLat = 45.75409965, OperaLon = 21.2259413;

        [MenuItem("Tools/3Times/Previzualizare: Piața Victoriei în Trecut", priority = 30)]
        public static void ShowSquare()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }
            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            Hide();

            var past = Object.FindFirstObjectByType<PastManager>();
            var era = Object.FindFirstObjectByType<PastEra>();
            if (past == null || era == null)
            {
                EditorUtility.DisplayDialog("3Times",
                    "Deschide scena Main și rulează întâi pașii 6–9 (Tools → 3Times).", "OK");
                return;
            }
            var pastSo = new SerializedObject(past);
            var eraSo = new SerializedObject(era);
            var prefab = pastSo.FindProperty("facadePrefab").objectReferenceValue as GameObject;
            float heading = pastSo.FindProperty("heading").floatValue;
            float scale = pastSo.FindProperty("scale").floatValue;

            var body = new Material[FacadeMesh.MaterialCount];
            var bodyProp = eraSo.FindProperty("bodyMaterials");
            for (int i = 0; i < body.Length && i < bodyProp.arraySize; i++)
                body[i] = bodyProp.GetArrayElementAtIndex(i).objectReferenceValue as Material;
            var decor = new[]
            {
                eraSo.FindProperty("awningMaterial").objectReferenceValue as Material,
                eraSo.FindProperty("signMaterial").objectReferenceValue as Material,
                eraSo.FindProperty("pavingMaterial").objectReferenceValue as Material,
                eraSo.FindProperty("glowMaterial").objectReferenceValue as Material,
                eraSo.FindProperty("tilesMaterial").objectReferenceValue as Material,
                eraSo.FindProperty("flagMaterial").objectReferenceValue as Material,
            };
            if (prefab == null || body.Any(m => m == null) || decor.Any(m => m == null))
            {
                EditorUtility.DisplayDialog("3Times", "Lipsesc materialele: rulează din nou pașii 5, 9 și 10.", "OK");
                return;
            }

            var root = new GameObject(RootName);

            // Terenul: un pătrat gri sub piață.
            var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
            ground.name = "Teren";
            ground.transform.SetParent(root.transform, false);
            ground.transform.localPosition = new Vector3(-40f, -0.02f, -150f);
            ground.transform.localScale = new Vector3(30f, 1f, 40f);
            Object.DestroyImmediate(ground.GetComponent<Collider>());

            // Opera din 1900, rotită și scalată ca în AR.
            var opera = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            opera.transform.SetParent(root.transform, false);
            opera.transform.localPosition = Vector3.zero;
            opera.transform.localRotation = Quaternion.Euler(0f, heading, 0f);
            opera.transform.localScale = Vector3.one * scale;

            // Palatele, fiecare la originea lui (cadrul est-sus-nord, ca ancorele ARCore).
            foreach (var spec in PalaceCatalog.Palaces)
            {
                PalaceCatalog.Origin(spec, out double lat, out double lon);
                var go = new GameObject(spec.Name);
                go.transform.SetParent(root.transform, false);
                go.transform.localPosition = PalaceCatalog.ToLocal(lat, lon, OperaLat, OperaLon);
                go.AddComponent<PalaceView>().Build(spec, PalaceBuilder.Build(spec), body, decor);
            }

            // Catedrala, sfințită (1946).
            var cathedral = new GameObject("Catedrala 1946");
            cathedral.transform.SetParent(root.transform, false);
            cathedral.transform.localPosition = PalaceCatalog.ToLocal(CathedralData.Lat, CathedralData.Lon, OperaLat, OperaLon);
            cathedral.AddComponent<PalaceView>().Build(new PalaceSpec
            {
                Name = "Catedrala 1946",
                Wall = new Color(0.89f, 0.84f, 0.72f), Trim = new Color(0.60f, 0.30f, 0.20f),
                RoofColor = new Color(0.45f, 0.50f, 0.42f), Ornament = new Color(0.83f, 0.69f, 0.29f),
            }, CathedralBuilder.Build(CathedralPhase.Complete), body, decor);

            // Nimic din previzualizare nu intră în scenă sau în fișiere.
            foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.hideFlags = HideFlags.DontSave;
            foreach (var mf in root.GetComponentsInChildren<MeshFilter>(true))
                if (mf.sharedMesh != null && !AssetDatabase.Contains(mf.sharedMesh)) mf.sharedMesh.hideFlags = HideFlags.DontSave;

            // Camera din Scene view: din mijlocul pieței, spre Operă.
            var view = SceneView.lastActiveSceneView;
            if (view != null)
            {
                view.LookAt(new Vector3(-20f, 8f, -45f), Quaternion.LookRotation(new Vector3(0.45f, -0.12f, 1f)), 60f);
                view.Repaint();
            }
            Selection.activeGameObject = root;
            Debug.Log("[3Times] Previzualizare: Opera din 1900 și " + PalaceCatalog.Palaces.Length + " palate în Scene view.");
        }

        [MenuItem("Tools/3Times/Previzualizare: ascunde", priority = 31)]
        public static void Hide()
        {
            foreach (var go in Resources.FindObjectsOfTypeAll<GameObject>().Where(g => g.name == RootName && g.scene.IsValid()).ToList())
            {
                foreach (var mf in go.GetComponentsInChildren<MeshFilter>(true))
                    if (mf.sharedMesh != null && !AssetDatabase.Contains(mf.sharedMesh)) Object.DestroyImmediate(mf.sharedMesh);
                Object.DestroyImmediate(go);
            }
        }
    }
}
#endif
