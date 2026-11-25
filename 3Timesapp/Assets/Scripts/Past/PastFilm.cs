using UnityEngine;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Stratul de „film vechi” din Trecut: un pătrat lipit de cameră, cu shader-ul ThreeTimes/OldFilm, care
    /// acoperă tot ecranul (zgârieturi, praf, pâlpâire, margini arse). Apare odată cu sepia (urmează greutatea
    /// din PastLook). PastTransition îi dă în plus blițul și pâlpâirea puternică de la pornirea proiectorului.
    /// </summary>
    public class PastFilm : MonoBehaviour
    {
        [SerializeField] PastLook look;
        [Tooltip("Camera AR (pătratul se lipește de ea)")]
        [SerializeField] Camera arCamera;
        [Tooltip("Materialul cu shader-ul ThreeTimes/OldFilm (îl creează Tools → 3Times → 8)")]
        [SerializeField] Material filmMaterial;
        [Tooltip("Cât de tare se văd zgârieturile și praful (0..1)")]
        [Range(0f, 1f)] [SerializeField] float strength = 1f;

        /// <summary>Blițul proiectorului, 0..1 (îl pune PastTransition).</summary>
        public float Flash { get; set; }
        /// <summary>Pâlpâire în plus, 0..1 (îl pune PastTransition).</summary>
        public float Flicker { get; set; }

        GameObject layer;
        Material material;
        static readonly int AmountId = Shader.PropertyToID("_Amount");
        static readonly int FlashId = Shader.PropertyToID("_Flash");
        static readonly int FlickerId = Shader.PropertyToID("_Flicker");

        void Start()
        {
            if (arCamera == null) arCamera = Camera.main;
            if (arCamera == null || filmMaterial == null)
            {
                Debug.LogWarning("[3Times] Filmul vechi: lipsește camera sau materialul (Tools → 3Times → 8)");
                enabled = false;
                return;
            }

            var mesh = new Mesh { name = "Film vechi" };
            mesh.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
            mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1) };
            mesh.triangles = new[] { 0, 2, 1, 0, 3, 2 };
            // Shader-ul îl întinde pe tot ecranul; limitele mari îl țin mereu „în cadru”, ca să nu fie ascuns.
            mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100000f);

            layer = new GameObject("Film vechi");
            layer.layer = arCamera.gameObject.layer;
            layer.transform.SetParent(arCamera.transform, false);
            layer.transform.localPosition = new Vector3(0f, 0f, 1f);
            layer.AddComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = layer.AddComponent<MeshRenderer>();
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            material = new Material(filmMaterial);
            renderer.sharedMaterial = material;
            layer.SetActive(false);
        }

        void LateUpdate()
        {
            if (layer == null) return;
            float amount = (look != null ? look.Weight : 0f) * strength;
            bool on = amount > 0.001f || Flash > 0.001f;
            if (layer.activeSelf != on) layer.SetActive(on);
            if (!on) return;
            material.SetFloat(AmountId, amount);
            material.SetFloat(FlashId, Mathf.Clamp01(Flash));
            material.SetFloat(FlickerId, Mathf.Clamp01(Flicker));
        }

        void OnDestroy()
        {
            if (material != null) Destroy(material);
            if (layer != null) Destroy(layer);
        }
    }
}
