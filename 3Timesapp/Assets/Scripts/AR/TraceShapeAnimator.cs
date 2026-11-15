using UnityEngine;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Mișcarea lentă a unei forme, aleasă de AI odată cu forma: pulse (pulsează), sway (se leagănă), drift (coboară și
    /// urcă), flicker (tremură ușor, ca o flacără), breathe (respiră rar), spin (se rotește încet).
    /// Forma se întoarce mereu cu fața spre telefon.
    /// </summary>
    public class TraceShapeAnimator : MonoBehaviour
    {
        string motion = "breathe";
        int level = 1;
        float phase; // decalaj, ca urmele să nu se miște toate la fel

        public void Setup(string motionName, int intensityLevel, int seed)
        {
            motion = string.IsNullOrEmpty(motionName) ? "breathe" : motionName;
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
            float roll = 0f;
            switch (motion)
            {
                case "pulse":   // mai repede când emoția e mai puternică
                    scale = 1f + 0.06f * Mathf.Sin(t * (1.6f + 0.5f * level));
                    break;
                case "sway":
                    roll = 10f * Mathf.Sin(t * 0.6f);
                    break;
                case "drift":
                    lift = 0.04f * Mathf.Sin(t * 0.9f);
                    break;
                case "flicker":
                    scale = 1f + 0.035f * Mathf.Sin(t * 7.3f) + 0.025f * Mathf.Sin(t * 11.1f);
                    lift = 0.01f * Mathf.Sin(t * 5.7f);
                    break;
                case "spin":
                    roll = t * (12f + 6f * level);
                    break;
                default:        // breathe
                    scale = 1f + 0.04f * Mathf.Sin(t * 0.7f);
                    break;
            }

            transform.rotation = rotation * Quaternion.Euler(0f, 0f, roll);
            transform.localScale = Vector3.one * scale;
            transform.localPosition = new Vector3(0f, lift, 0f);
        }
    }
}
