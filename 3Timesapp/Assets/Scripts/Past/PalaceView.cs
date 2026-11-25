using UnityEngine;
using UnityEngine.Rendering;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Un palat afișat în AR: corpul (cu materialele fațadei Operei, colorate pentru fiecare palat) și decorul
    /// (copertine, firme, pavaj, lumina felinarelor). Stă pe ancora lui, în cadrul est-sus-nord al ARCore.
    /// </summary>
    public class PalaceView : MonoBehaviour
    {
        public PalaceSpec Spec { get; private set; }
        public PalaceGeometry Geometry { get; private set; }

        Mesh bodyMesh, decorMesh;

        static readonly int BaseColor = Shader.PropertyToID("_BaseColor");

        public void Build(PalaceSpec spec, PalaceGeometry geometry, Material[] bodyMaterials, Material[] decorMaterials)
        {
            Spec = spec;
            Geometry = geometry;

            var body = Child("Corp");
            bodyMesh = new Mesh { name = spec.Name, indexFormat = IndexFormat.UInt32 };
            bodyMesh.SetVertices(geometry.Body.Vertices);
            bodyMesh.SetNormals(geometry.Body.Normals);
            bodyMesh.subMeshCount = FacadeMesh.MaterialCount;
            for (int i = 0; i < FacadeMesh.MaterialCount; i++) bodyMesh.SetTriangles(geometry.Body.Triangles[i], i, false);
            bodyMesh.RecalculateBounds();
            body.AddComponent<MeshFilter>().sharedMesh = bodyMesh;
            var bodyRenderer = body.AddComponent<MeshRenderer>();
            bodyRenderer.sharedMaterials = bodyMaterials;
            bodyRenderer.shadowCastingMode = ShadowCastingMode.Off;

            // Culorile palatului, fără materiale noi: un bloc de proprietăți pe fiecare sub-plasă.
            Tint(bodyRenderer, (int)FacadeMaterial.Stone, spec.Wall);
            Tint(bodyRenderer, (int)FacadeMaterial.Trim, spec.Trim);
            Tint(bodyRenderer, (int)FacadeMaterial.Roof, spec.RoofColor);
            Tint(bodyRenderer, (int)FacadeMaterial.Sculpture, spec.Ornament);

            var decor = Child("Decor");
            decorMesh = new Mesh { name = spec.Name + " (decor)", indexFormat = IndexFormat.UInt32 };
            decorMesh.SetVertices(geometry.Decor.Vertices);
            decorMesh.SetNormals(geometry.Decor.Normals);
            decorMesh.SetUVs(0, geometry.Decor.UVs);
            decorMesh.subMeshCount = DecorMesh.MaterialCount;
            for (int i = 0; i < DecorMesh.MaterialCount; i++) decorMesh.SetTriangles(geometry.Decor.Triangles[i], i, false);
            decorMesh.RecalculateBounds();
            decor.AddComponent<MeshFilter>().sharedMesh = decorMesh;
            var decorRenderer = decor.AddComponent<MeshRenderer>();
            decorRenderer.sharedMaterials = decorMaterials;
            decorRenderer.shadowCastingMode = ShadowCastingMode.Off;
        }

        GameObject Child(string childName)
        {
            var go = new GameObject(childName);
            go.layer = gameObject.layer;
            go.transform.SetParent(transform, false);
            return go;
        }

        static void Tint(Renderer renderer, int index, Color color)
        {
            var block = new MaterialPropertyBlock();
            block.SetColor(BaseColor, color);
            renderer.SetPropertyBlock(block, index);
        }

        void OnDestroy()
        {
            if (bodyMesh != null) Destroy(bodyMesh);
            if (decorMesh != null) Destroy(decorMesh);
        }
    }
}
