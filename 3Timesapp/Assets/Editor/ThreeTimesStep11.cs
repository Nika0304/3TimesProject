#if UNITY_EDITOR
using System.Linq;
using ThreeTimes.Past;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ThreeTimes.EditorTools
{
    /// <summary>
    /// Trecutul, sunetele: setează fișierele din Assets/Audio/Past (proiectorul, ambianța pieței, clopotele din 1946),
    /// le pune pe PastTransition (proiectorul) și pe un PastAmbience nou pe App (ambianța, clopotele).
    /// Are nevoie de pașii 8–10. Meniu: Tools → 3Times → 11. Trecutul: sunetele
    /// </summary>
    public static class ThreeTimesStep11
    {
        const string ScenePath = "Assets/Scenes/Main.unity";
        const string Folder = "Assets/Audio/Past";

        [MenuItem("Tools/3Times/11. Trecutul: sunetele", priority = 11)]
        public static void Run()
        {
            if (EditorApplication.isPlaying)
            {
                EditorUtility.DisplayDialog("3Times", "Oprește Play înainte.", "OK");
                return;
            }

            var start = Clip($"{Folder}/ProjectorStart.wav", AudioClipLoadType.DecompressOnLoad);
            var loop = Clip($"{Folder}/ProjectorLoop.wav", AudioClipLoadType.DecompressOnLoad);
            var ambience = Clip($"{Folder}/Ambience1912.wav", AudioClipLoadType.CompressedInMemory);
            var bells = Clip($"{Folder}/Bells1946.wav", AudioClipLoadType.CompressedInMemory);
            if (start == null || loop == null || ambience == null || bells == null)
            {
                EditorUtility.DisplayDialog("3Times", $"Lipsesc sunetele din {Folder}.", "OK");
                return;
            }

            if (PrefabStageUtility.GetCurrentPrefabStage() != null) StageUtility.GoToMainStage();
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
            var scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            var app = scene.GetRootGameObjects().FirstOrDefault(g => g.name == "App");
            var past = app != null ? app.GetComponent<PastManager>() : null;
            var transition = app != null ? app.GetComponent<PastTransition>() : null;
            var era = app != null ? app.GetComponent<PastEra>() : null;
            if (past == null || transition == null || era == null)
            {
                EditorUtility.DisplayDialog("3Times", "Nu găsesc PastManager, PastTransition și PastEra pe App. Rulează întâi pașii 8–10.", "OK");
                return;
            }

            var so = new SerializedObject(transition);
            so.FindProperty("projectorStart").objectReferenceValue = start;
            so.FindProperty("projectorLoop").objectReferenceValue = loop;
            so.ApplyModifiedPropertiesWithoutUndo();

            var amb = app.GetComponent<PastAmbience>();
            if (amb == null) amb = app.AddComponent<PastAmbience>();
            so = new SerializedObject(amb);
            so.FindProperty("past").objectReferenceValue = past;
            so.FindProperty("era").objectReferenceValue = era;
            so.FindProperty("ambience").objectReferenceValue = ambience;
            so.FindProperty("bells").objectReferenceValue = bells;
            so.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Debug.Log("[3Times Setup] Trecutul: sunetele (proiector, ambianță, clopote).");
            EditorUtility.DisplayDialog("3Times: sunetele",
                "Gata. În Trecut:\n" +
                "• la trecerea în timp pornește proiectorul, apoi toarce încet;\n" +
                "• după câteva secunde se aude piața: lume, o trăsură, tramvaiul de departe, vrăbii;\n" +
                "• la 1946 (sfințirea Catedralei) bat clopotele.\n\n" +
                "Sunetele sunt generate în aplicație; le poți asculta dând click pe ele în Assets/Audio/Past.", "OK");
        }

        static AudioClip Clip(string path, AudioClipLoadType loadType)
        {
            var importer = AssetImporter.GetAtPath(path) as AudioImporter;
            if (importer == null) return null;
            importer.forceToMono = true;
            importer.loadInBackground = loadType != AudioClipLoadType.DecompressOnLoad;
            var settings = importer.defaultSampleSettings;
            settings.loadType = loadType;
            settings.compressionFormat = AudioCompressionFormat.Vorbis;
            settings.quality = 0.6f;
            importer.defaultSampleSettings = settings;
            importer.SaveAndReimport();
            return AssetDatabase.LoadAssetAtPath<AudioClip>(path);
        }
    }
}
#endif
