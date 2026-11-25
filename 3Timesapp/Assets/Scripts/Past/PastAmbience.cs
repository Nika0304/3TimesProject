using UnityEngine;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Sunetul pieței în Trecut: după ce pornește proiectorul, se aude încet piața de atunci (murmurul lumii, o
    /// trăsură, clopoțelul tramvaiului din depărtare, vrăbii), în buclă. Când anul ajunge la 1946 (sfințirea
    /// Catedralei), bat clopotele. Sunetele sunt generate în aplicație (Assets/Audio/Past), nu înregistrări.
    /// </summary>
    public class PastAmbience : MonoBehaviour
    {
        [SerializeField] PastManager past;
        [SerializeField] PastEra era;
        [SerializeField] AudioClip ambience;
        [SerializeField] AudioClip bells;
        [Range(0f, 1f)] [SerializeField] float ambienceVolume = 0.85f;
        [Range(0f, 1f)] [SerializeField] float bellsVolume = 0.9f;
        [Tooltip("După cât timp de la trecerea în timp intră ambianța (s)")]
        [SerializeField] float startDelay = 1.8f;
        [SerializeField] float fadeSeconds = 2.5f;

        AudioSource loop, once;
        float activeSince = -1f;
        int lastYear;

        void Start()
        {
            loop = gameObject.AddComponent<AudioSource>();
            loop.playOnAwake = false;
            loop.loop = true;
            loop.spatialBlend = 0f;
            loop.volume = 0f;
            loop.clip = ambience;
            once = gameObject.AddComponent<AudioSource>();
            once.playOnAwake = false;
            once.spatialBlend = 0f;
        }

        void Update()
        {
            bool on = past != null && past.Active;
            if (on && activeSince < 0f) activeSince = Time.realtimeSinceStartup;
            if (!on) activeSince = -1f;

            bool audible = on && Time.realtimeSinceStartup - activeSince >= startDelay;
            float target = audible ? ambienceVolume : 0f;
            loop.volume = Mathf.MoveTowards(loop.volume, target, Time.deltaTime / Mathf.Max(0.1f, fadeSeconds));
            if (loop.clip != null)
            {
                if (loop.volume > 0f && !loop.isPlaying) loop.Play();
                else if (loop.volume <= 0f && loop.isPlaying) loop.Stop();
            }

            int year = on && era != null ? era.Year : 0;
            if (year >= 1946 && lastYear < 1946 && lastYear > 0 && bells != null) once.PlayOneShot(bells, bellsVolume);
            if (!on) once.Stop();
            lastYear = year;
        }
    }
}
