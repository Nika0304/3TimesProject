using UnityEngine;

namespace ThreeTimes.AR
{
    /// <summary>Microfonul unei urme cu voce: „respiră” încet, ca un sunet care se aude.</summary>
    public class TraceTypePulse : MonoBehaviour
    {
        float phase;

        void Awake() => phase = Random.value * 6.28f;

        void Update()
        {
            float t = Time.time * 2.2f + phase;
            transform.localScale = Vector3.one * (1f + 0.04f * Mathf.Sin(t));
        }
    }
}
