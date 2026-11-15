using System.Collections.Generic;
using UnityEngine;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Fundalul care arată tipul unei urme în AR, desenat în spatele formei din particule:
    /// polaroid (poză) sau microfon (voce). Alb, puțin transparent, cu o umbră din culoarea emoției.
    /// Dimensiunile sunt în diametre ale formei (D), cu centrul formei în (0, 0) și Y în sus.
    /// </summary>
    public static class TraceBackdropMesh
    {
        static readonly Color Paper = new Color(1f, 1f, 1f, 0.6f);
        static Material sharedMaterial;

        public static GameObject Create(string traceType, float diameter, Color tint, Material particleMaterial, Transform parent)
        {
            var b = new Builder();
            var tinted = tint;
            if (traceType == "PHOTO")
            {
                // Rama polaroidului (marginea de jos mai lată) și „poza” colorată ușor în culoarea emoției.
                tinted.a = 0.3f;
                b.Rect(new Vector2(-0.62f, -0.78f), new Vector2(0.62f, 0.62f), Paper);
                b.Rect(new Vector2(-0.52f, -0.45f), new Vector2(0.52f, 0.52f), tinted);
            }
            else
            {
                // Microfon: capul (cu trei linii de grilă), suportul în U, tija și baza.
                tinted.a = 0.45f;
                b.Stadium(new Vector2(0f, 0.12f), 0.25f, 0.78f, Paper);
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.30f - i * 0.13f;
                    b.Rect(new Vector2(-0.13f, y - 0.012f), new Vector2(0.13f, y + 0.012f), tinted);
                }
                b.Arc(new Vector2(0f, 0.05f), 0.355f, 0.405f, 180f, 360f, Paper);
                b.Rect(new Vector2(-0.025f, -0.64f), new Vector2(0.025f, -0.355f), Paper);
                b.Rect(new Vector2(-0.2f, -0.69f), new Vector2(0.2f, -0.64f), Paper);
            }

            var go = new GameObject(traceType == "PHOTO" ? "Polaroid" : "Microphone");
            go.transform.SetParent(parent, false);
            // Puțin în spatele particulelor (+Z = dinspre cameră spre urmă) și, la polaroid, ușor înclinat.
            go.transform.localPosition = new Vector3(0f, 0f, 0.06f);
            go.transform.localRotation = Quaternion.Euler(0f, 0f, traceType == "PHOTO" ? 5f : 0f);
            go.transform.localScale = Vector3.one * diameter;

            go.AddComponent<MeshFilter>().sharedMesh = b.ToMesh(go.name);
            var renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = MaterialFor(particleMaterial);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }

        /// <summary>Același shader ca particulele (Sprites/Default, inclus sigur în build), cu textură albă.</summary>
        static Material MaterialFor(Material particleMaterial)
        {
            if (sharedMaterial == null || sharedMaterial.shader != particleMaterial.shader)
            {
                sharedMaterial = new Material(particleMaterial.shader) { name = "TraceBackdrop" };
                sharedMaterial.mainTexture = Texture2D.whiteTexture;
                sharedMaterial.color = Color.white;
            }
            return sharedMaterial;
        }

        /// <summary>Strânge triunghiuri colorate (culoare pe vârf) într-o singură plasă.</summary>
        class Builder
        {
            readonly List<Vector3> vertices = new List<Vector3>();
            readonly List<Color> colors = new List<Color>();
            readonly List<int> triangles = new List<int>();

            int Vertex(Vector2 p, Color c)
            {
                vertices.Add(new Vector3(p.x, p.y, 0f));
                colors.Add(c);
                return vertices.Count - 1;
            }

            void Triangle(int a, int b, int c)
            {
                triangles.Add(a); triangles.Add(b); triangles.Add(c);
            }

            public void Rect(Vector2 min, Vector2 max, Color c)
            {
                int a = Vertex(min, c);
                int b = Vertex(new Vector2(min.x, max.y), c);
                int d = Vertex(max, c);
                int e = Vertex(new Vector2(max.x, min.y), c);
                Triangle(a, b, d);
                Triangle(a, d, e);
            }

            /// <summary>Dreptunghi cu capetele de sus și de jos rotunde (capul microfonului).</summary>
            public void Stadium(Vector2 center, float radius, float height, Color c)
            {
                float half = height / 2f - radius;
                int mid = Vertex(center, c);
                var ring = new List<int>();
                const int steps = 14;
                for (int i = 0; i <= steps; i++) // semicercul de sus, de la dreapta la stânga
                {
                    float a = Mathf.PI * i / steps;
                    ring.Add(Vertex(center + new Vector2(radius * Mathf.Cos(a), half + radius * Mathf.Sin(a)), c));
                }
                for (int i = 0; i <= steps; i++) // semicercul de jos, de la stânga la dreapta
                {
                    float a = Mathf.PI + Mathf.PI * i / steps;
                    ring.Add(Vertex(center + new Vector2(radius * Mathf.Cos(a), -half + radius * Mathf.Sin(a)), c));
                }
                for (int i = 0; i < ring.Count; i++)
                    Triangle(mid, ring[i], ring[(i + 1) % ring.Count]);
            }

            /// <summary>O bandă în arc de cerc, între două raze, de la unghiul from la to (grade).</summary>
            public void Arc(Vector2 center, float inner, float outer, float from, float to, Color c)
            {
                const int steps = 24;
                int prevIn = -1, prevOut = -1;
                for (int i = 0; i <= steps; i++)
                {
                    float a = Mathf.Deg2Rad * Mathf.Lerp(from, to, i / (float)steps);
                    var dir = new Vector2(Mathf.Cos(a), Mathf.Sin(a));
                    int vin = Vertex(center + dir * inner, c);
                    int vout = Vertex(center + dir * outer, c);
                    if (i > 0)
                    {
                        Triangle(prevIn, prevOut, vout);
                        Triangle(prevIn, vout, vin);
                    }
                    prevIn = vin;
                    prevOut = vout;
                }
            }

            public Mesh ToMesh(string name)
            {
                var mesh = new Mesh { name = name };
                mesh.SetVertices(vertices);
                mesh.SetColors(colors);
                mesh.SetTriangles(triangles, 0);
                mesh.RecalculateBounds();
                return mesh;
            }
        }
    }
}
