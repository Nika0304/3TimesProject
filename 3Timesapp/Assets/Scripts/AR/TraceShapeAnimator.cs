using UnityEngine;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Mișcarea lentă a unei forme: floarea pulsează, picătura coboară și urcă ușor,
    /// semiluna se leagănă. Forma se întoarce mereu cu fața spre telefon.
    /// </summary>
    public class TraceShapeAnimator : MonoBehaviour
    {
        string emotionKey = "joy";
        int level = 1;
        float phase; // decalaj, ca urmele să nu se miște toate la fel

        public void Setup(string emotion, int intensityLevel, int seed)
        {
            emotionKey = emotion;
            level = intensityLevel;
            phase = Mathf.Abs(seed % 1000) / 1000f * 6.28f;
        }

        void Update()
        {
            float t = Time.time + phase;

            // Cu fața spre cameră (doar rotire în jurul verticalei).
            var rotation = Quaternion.identity;
            var cam = Camera.main;
            if (cam != null)
            {
                Vector3 away = transform.position - cam.transform.position;
                away.y = 0f;
                if (away.sqrMagnitude > 0.0001f) rotation = Quaternion.LookRotation(away.normalized, Vector3.up);
            }

            float scale = 1f;
            float lift = 0f;
            switch (emotionKey)
            {
                case "sadness":
                    lift = 0.04f * Mathf.Sin(t * 0.9f);
                    break;
                case "longing":
                    rotation = rotation * Quaternion.Euler(0f, 0f, 10f * Mathf.Sin(t * 0.6f));
                    break;
                default:
                    // Bucuria pulsează mai repede când e mai puternică.
                    scale = 1f + 0.06f * Mathf.Sin(t * (1.6f + 0.5f * level));
                    break;
            }

            transform.rotation = rotation;
            transform.localScale = Vector3.one * scale;
            transform.localPosition = new Vector3(0f, lift, 0f);
        }
    }
}
