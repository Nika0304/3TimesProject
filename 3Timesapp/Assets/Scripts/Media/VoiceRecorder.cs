using System;
using System.Collections;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ThreeTimes.Media
{
    /// <summary>
    /// Înregistrarea vocală a unei urme: microfonul implicit, mono, 16 kHz, cel mult MaxSeconds.
    /// Permisiunea de microfon se cere abia la prima înregistrare.
    /// </summary>
    public class VoiceRecorder
    {
        public const int MaxSeconds = 30;
        const int Frequency = 16000;

        AudioClip clip;
        float startedAt;

        public bool IsRecording { get; private set; }
        public float Elapsed => IsRecording ? Time.realtimeSinceStartup - startedAt : 0f;

        public static bool HasPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Microphone);
#else
            return true;
#endif
        }

        /// <summary>Cere permisiunea de microfon (dacă lipsește) și spune dacă o avem.</summary>
        public static IEnumerator RequestPermission(Action<bool> onDone)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!HasPermission())
            {
                bool answered = false, lostFocus = false;
                void OnFocus(bool focused)
                {
                    if (!focused) lostFocus = true;
                    else if (lostFocus) answered = true;
                }
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => answered = true;
                callbacks.PermissionDenied += _ => answered = true;
                Application.focusChanged += OnFocus;
                Permission.RequestUserPermission(Permission.Microphone, callbacks);
                float elapsed = 0f;
                while (!answered && elapsed < 60f)
                {
                    elapsed += Time.unscaledDeltaTime;
                    yield return null;
                }
                Application.focusChanged -= OnFocus;
                yield return new WaitForSecondsRealtime(0.2f);
            }
#endif
            yield return null;
            onDone?.Invoke(HasPermission());
        }

        public bool Start()
        {
            if (IsRecording) return true;
            if (Microphone.devices.Length == 0)
            {
                Debug.LogWarning("[3Times] Nu există microfon");
                return false;
            }
            clip = Microphone.Start(null, false, MaxSeconds, Frequency);
            if (clip == null) return false;
            startedAt = Time.realtimeSinceStartup;
            IsRecording = true;
            return true;
        }

        /// <summary>Oprește înregistrarea și o salvează. Întoarce numele fișierului și durata, sau null.</summary>
        public (string file, float seconds)? Stop()
        {
            if (!IsRecording) return null;
            IsRecording = false;

            // Dacă s-a atins limita, microfonul s-a oprit singur și poziția revine la 0.
            int position = Microphone.IsRecording(null) ? Microphone.GetPosition(null) : clip.samples;
            Microphone.End(null);
            if (position <= 0) position = clip.samples;

            float seconds = position / (float)clip.frequency;
            if (seconds < 0.5f)
            {
                UnityEngine.Object.Destroy(clip);
                clip = null;
                return null;
            }

            var samples = new float[position * clip.channels];
            clip.GetData(samples, 0);
            string file = MediaStore.SaveWav(samples, clip.channels, clip.frequency);
            UnityEngine.Object.Destroy(clip);
            clip = null;
            return file == null ? ((string, float)?)null : (file, seconds);
        }

        /// <summary>Oprește fără să salveze.</summary>
        public void Cancel()
        {
            if (!IsRecording) return;
            IsRecording = false;
            Microphone.End(null);
            if (clip != null) UnityEngine.Object.Destroy(clip);
            clip = null;
        }
    }

    /// <summary>Redă o înregistrare salvată. Un singur sunet odată.</summary>
    public class VoicePlayer
    {
        readonly AudioSource source;
        readonly AudioListener fallbackListener;
        string currentFile;

        public VoicePlayer(GameObject host)
        {
            source = host.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.spatialBlend = 0f;
            // Când camera AR e oprită, ascultătorul ei e inactiv; avem unul de rezervă.
            fallbackListener = host.AddComponent<AudioListener>();
            fallbackListener.enabled = false;
        }

        public bool IsPlaying(string file) => file != null && file == currentFile && source.isPlaying;

        public float Length => source.clip != null ? source.clip.length : 0f;
        public float Position => source.time;

        public bool Play(string file)
        {
            Stop();
            var clip = MediaStore.LoadWav(file);
            if (clip == null) return false;

            bool otherListener = false;
            foreach (var l in UnityEngine.Object.FindObjectsByType<AudioListener>(FindObjectsSortMode.None))
                if (l != fallbackListener && l.isActiveAndEnabled) otherListener = true;
            fallbackListener.enabled = !otherListener;

            source.clip = clip;
            source.Play();
            currentFile = file;
            return true;
        }

        public void Stop()
        {
            if (source.isPlaying) source.Stop();
            if (source.clip != null) UnityEngine.Object.Destroy(source.clip);
            source.clip = null;
            currentFile = null;
            fallbackListener.enabled = false;
        }
    }
}
