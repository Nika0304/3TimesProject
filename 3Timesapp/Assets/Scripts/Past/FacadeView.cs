using UnityEngine;
using UnityEngine.Rendering;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Afișează o fațadă istorică generată din cod (deocamdată: Opera în 1900). Plasa se construiește la pornire
    /// și în Editor (ExecuteAlways), fără să fie salvată în scenă sau în prefab: se vede direct în Scene view.
    /// Materialele vin în ordinea din FacadeMaterial (le creează Tools → 3Times → 5).
    /// </summary>
    [ExecuteAlways]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public class FacadeView : MonoBehaviour
    {
        public enum Building { Opera1900 }

        public Building building = Building.Opera1900;

        [Tooltip("În ordinea: piatră, ancadramente, bosaje, ferestre, acoperiș, sculptură, umbră")]
        public Material[] materials = new Material[FacadeMesh.MaterialCount];

        Mesh mesh;

        void OnEnable()
        {
            // În aplicație plasa se face o singură dată (fațada se ascunde și reapare des); în Editor, la fiecare activare.
            if (mesh == null || !Application.isPlaying) Rebuild();
        }

        void OnDisable()
        {
            if (!Application.isPlaying) Release();
        }

        void OnDestroy() => Release();

        void Release()
        {
            if (mesh == null) return;
            if (Application.isPlaying) Destroy(mesh);
            else DestroyImmediate(mesh);
            mesh = null;
        }

        void OnValidate()
        {
            if (isActiveAndEnabled) GetComponent<MeshRenderer>().sharedMaterials = materials;
        }

        [ContextMenu("Reconstruiește fațada")]
        public void Rebuild()
        {
            var data = OperaFacade1900.Build(); // deocamdată singura clădire
            if (mesh == null)
            {
                mesh = new Mesh { name = $"{building} (generat)", hideFlags = HideFlags.DontSave };
                mesh.indexFormat = IndexFormat.UInt32;
            }
            mesh.Clear();
            mesh.SetVertices(data.Vertices);
            mesh.SetNormals(data.Normals);
            mesh.subMeshCount = FacadeMesh.MaterialCount;
            for (int i = 0; i < FacadeMesh.MaterialCount; i++)
            {
                // În sistemul lui Unity (de mână stângă), ordinea din FacadeMesh e exact cea a fețelor din față.
                mesh.SetTriangles(data.Triangles[i], i, false);
            }
            mesh.RecalculateBounds();
            GetComponent<MeshFilter>().sharedMesh = mesh;
            GetComponent<MeshRenderer>().sharedMaterials = materials;
        }
    }
}
