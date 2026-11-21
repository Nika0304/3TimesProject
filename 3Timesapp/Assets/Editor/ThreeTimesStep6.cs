#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.AR;
using ThreeTimes.Past;
using ThreeTimes.UI;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR.ARFoundation;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul în AR: adaugă PastManager pe obiectul App din scena Main și îi leagă fațada Operei (1900).
    /// Nu schimbă nimic altceva în scenă.
    /// Meniu: Tools → 3Times → 6. Trecutul în AR (fațada peste Operă)
    /// </summary>
    public static class ThreeTimesStep6
    {
        const string Log = "[3Times Setup]";
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string PrefabPath = "Assets/Prefabs/OperaFacade1900.prefab";

        [MenuItem("Tools/3Times/6. Trecutul în AR (fațada peste Operă)", priority = 6)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) ?? ThreeTimesStep5.CreatePrefab();
            if (prefab == null) return;

            // Dacă e deschis prefab-ul fațadei, ieșim din el înainte să deschidem scena.
            if (UnityEditor.SceneManagement.PrefabStageUtility.GetCurrentPrefabStage() != null)
                StageUtility.GoToMainStage();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var anchorManager = Resources.FindObjectsOfTypeAll<ARAnchorManager>().FirstOrDefault(c => c.gameObject.scene == scene);
            if (app == null || anchorManager == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc App sau AR Anchor Manager în scena Main.", "OK");
                return;
            }

            var manager = app.GetComponent<PastManager>();
            if (manager == null) manager = app.AddComponent<PastManager>();
            var so = new SerializedObject(manager);
            so.FindProperty("anchorManager").objectReferenceValue = anchorManager;
            so.FindProperty("tracker").objectReferenceValue = app.GetComponent<GeospatialTracker>();
            so.FindProperty("mainView").objectReferenceValue = app.GetComponent<MainScreenView>();
            so.FindProperty("router").objectReferenceValue = app.GetComponent<UIRouter>();
            so.FindProperty("facadePrefab").objectReferenceValue = prefab;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log($"{Log} PastManager adăugat pe App, cu fațada {PrefabPath}.");

            EditorUtility.DisplayDialog("3Times: Trecutul în AR",
                "Gata. Fațada din 1900 apare doar pe telefon, în Piața Victoriei:\n\n" +
                "1. Pornește camera și așteaptă localizarea\n" +
                "2. Trage cursorul pe „Trecut”\n" +
                "3. Privește spre Operă\n\n" +
                "În Editor (simulare) nu apare.", "OK");
        }
    }
}
#endif
