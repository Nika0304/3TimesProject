using System;
using System.Collections.Generic;
using UnityEngine;

namespace ThreeTimes.Past
{
    /// <summary>Materialele fațadelor istorice (câte o sub-plasă pentru fiecare).</summary>
    public enum FacadeMaterial
    {
        Stone = 0,      // zidul tencuit / piatra
        Trim = 1,       // ancadramente, cornișe, coloane (puțin mai deschise)
        Rustic = 2,     // parterul cu bosaje
        Glass = 3,      // ferestre și uși
        Roof = 4,       // acoperișul de cupru, verde
        Sculpture = 5,  // statui, medalioane, reliefuri
        Shadow = 6,     // adâncituri: loggia, rosturile bosajelor, nișe
    }

    /// <summary>
    /// O plasă construită din forme simple (cutii, prisme din contururi convexe, cilindri, arce, cupole), cu fețe plane
    /// (fiecare față are vârfurile ei, ca muchiile să rămână nete). Nu depinde de nimic din scenă: aceleași date se pot
    /// verifica și în afara Unity. La fiecare triunghi, normala (b-a)×(c-a) e spre exterior: în Unity (sistem de mână
    /// stângă) asta înseamnă sens orar privit din afară, adică fața vizibilă.
    /// Axe: x de la stânga la dreapta (privind fațada), y în sus, z spre privitor (în afara fațadei); unități: metri.
    /// </summary>
    public class FacadeMesh
    {
        public const int MaterialCount = 7;

        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<int>[] Triangles = new List<int>[MaterialCount];

        public FacadeMesh()
        {
            for (int i = 0; i < MaterialCount; i++) Triangles[i] = new List<int>();
        }

        public int TriangleCount
        {
            get
            {
                int n = 0;
                foreach (var t in Triangles) n += t.Count / 3;
                return n;
            }
        }

        // ---------------------------------------------------------------- fețe

        /// <summary>Un triunghi; ordinea se întoarce dacă normala nu e spre <paramref name="outward"/>.</summary>
        public void Tri(Vector3 a, Vector3 b, Vector3 c, Vector3 outward, FacadeMaterial m)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0) { var t = b; b = c; c = t; n = -n; }
            n = n.normalized;
            int i = Vertices.Count;
            Vertices.Add(a); Vertices.Add(b); Vertices.Add(c);
            Normals.Add(n); Normals.Add(n); Normals.Add(n);
            var list = Triangles[(int)m];
            list.Add(i); list.Add(i + 1); list.Add(i + 2);
        }

        /// <summary>Un patrulater plan (a, b, c, d în ordine pe contur).</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 outward, FacadeMaterial m)
        {
            Tri(a, b, c, outward, m);
            Tri(a, c, d, outward, m);
        }

        // ---------------------------------------------------------------- forme

        /// <summary>O cutie între colțurile min și max. Fața din spate (z minim) se poate omite (stă lipită de zid).</summary>
        public void Box(Vector3 min, Vector3 max, FacadeMaterial m, bool back = false, bool bottom = true)
        {
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            if (x1 - x0 <= 0 || y1 - y0 <= 0 || z1 - z0 <= 0) return;
            Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.forward, m);
            if (back) Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), Vector3.back, m);
            Quad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), Vector3.right, m);
            Quad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), Vector3.left, m);
            Quad(new Vector3(x0, y1, z0), new Vector3(x1, y1, z0), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), Vector3.up, m);
            if (bottom) Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x0, y0, z1), Vector3.down, m);
        }

        /// <summary>Cutie dată prin x0..x1, y0..y1, z0..z1.</summary>
        public void Box(float x0, float x1, float y0, float y1, float z0, float z1, FacadeMaterial m, bool bottom = true) =>
            Box(new Vector3(x0, y0, z0), new Vector3(x1, y1, z1), m, false, bottom);

        /// <summary>
        /// Un contur convex din planul fațadei (x, y), scos în afară între z0 și z1: fața din față, laturile (și, la cerere,
        /// spatele).
        /// </summary>
        public void Extrude(IList<Vector2> outline, float z0, float z1, FacadeMaterial m, bool back = false)
        {
            int n = outline.Count;
            if (n < 3) return;
            Vector2 center = Vector2.zero;
            foreach (var p in outline) center += p;
            center /= n;
            for (int i = 1; i < n - 1; i++)
                Tri(V(outline[0], z1), V(outline[i], z1), V(outline[i + 1], z1), Vector3.forward, m);
            if (back)
                for (int i = 1; i < n - 1; i++)
                    Tri(V(outline[0], z0), V(outline[i], z0), V(outline[i + 1], z0), Vector3.back, m);
            for (int i = 0; i < n; i++)
            {
                Vector2 a = outline[i], b = outline[(i + 1) % n];
                Vector2 mid = (a + b) * 0.5f;
                Vector3 outward = new Vector3(mid.x - center.x, mid.y - center.y, 0);
                Quad(V(a, z0), V(b, z0), V(b, z1), V(a, z1), outward, m);
            }
        }

        static Vector3 V(Vector2 p, float z) => new Vector3(p.x, p.y, z);

        /// <summary>Un cilindru vertical (coloană), cu capacul de sus.</summary>
        public void Cylinder(float cx, float cz, float y0, float y1, float r, int segments, FacadeMaterial m)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = 2 * Mathf.PI * i / segments, a1 = 2 * Mathf.PI * (i + 1) / segments;
                var p0 = new Vector3(cx + r * Mathf.Cos(a0), 0, cz + r * Mathf.Sin(a0));
                var p1 = new Vector3(cx + r * Mathf.Cos(a1), 0, cz + r * Mathf.Sin(a1));
                var outward = new Vector3(Mathf.Cos((a0 + a1) / 2), 0, Mathf.Sin((a0 + a1) / 2));
                Quad(new Vector3(p0.x, y0, p0.z), new Vector3(p1.x, y0, p1.z), new Vector3(p1.x, y1, p1.z), new Vector3(p0.x, y1, p0.z), outward, m);
                Tri(new Vector3(cx, y1, cz), new Vector3(p0.x, y1, p0.z), new Vector3(p1.x, y1, p1.z), Vector3.up, m);
            }
        }

        /// <summary>Un disc ieșit din fațadă (medalion, rozetă): cilindru cu axa pe z.</summary>
        public void Disc(float cx, float cy, float r, float z0, float z1, int segments, FacadeMaterial m)
        {
            var outline = new List<Vector2>(segments);
            for (int i = 0; i < segments; i++)
            {
                float a = 2 * Mathf.PI * i / segments;
                outline.Add(new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a)));
            }
            Extrude(outline, z0, z1, m);
        }

        /// <summary>
        /// Un inel de arc (arhivoltă) în planul fațadei: semicerc de centru (cx, cy), între raza ri și ro, scos între z0 și z1.
        /// </summary>
        public void ArchRing(float cx, float cy, float ri, float ro, float z0, float z1, int segments, FacadeMaterial m,
                             float fromAngle = 0f, float toAngle = 180f)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = Mathf.Deg2Rad * Mathf.Lerp(fromAngle, toAngle, (float)i / segments);
                float a1 = Mathf.Deg2Rad * Mathf.Lerp(fromAngle, toAngle, (float)(i + 1) / segments);
                Extrude(new[]
                {
                    new Vector2(cx + ri * Mathf.Cos(a0), cy + ri * Mathf.Sin(a0)),
                    new Vector2(cx + ro * Mathf.Cos(a0), cy + ro * Mathf.Sin(a0)),
                    new Vector2(cx + ro * Mathf.Cos(a1), cy + ro * Mathf.Sin(a1)),
                    new Vector2(cx + ri * Mathf.Cos(a1), cy + ri * Mathf.Sin(a1)),
                }, z0, z1, m);
            }
        }

        /// <summary>Un dreptunghi încheiat sus cu semicerc (ușă / fereastră în arc), ca placă între z0 și z1.</summary>
        public void ArchPanel(float cx, float y0, float width, float springY, float z0, float z1, int segments, FacadeMaterial m)
        {
            float r = width / 2;
            var outline = new List<Vector2> { new Vector2(cx - r, y0), new Vector2(cx + r, y0) };
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.PI * i / segments;
                outline.Add(new Vector2(cx + r * Mathf.Cos(a), springY + r * Mathf.Sin(a)));
            }
            Extrude(outline, z0, z1, m);
        }

        /// <summary>Un fronton triunghiular (prismă), cu baza între x0 și x1 la înălțimea y și vârful la y + h.</summary>
        public void Pediment(float x0, float x1, float y, float h, float z0, float z1, FacadeMaterial m)
        {
            Extrude(new[] { new Vector2(x0, y), new Vector2(x1, y), new Vector2((x0 + x1) / 2, y + h) }, z0, z1, m);
        }

        /// <summary>Un fronton curb (segment de cerc) peste x0..x1, cu săgeata h.</summary>
        public void SegmentPediment(float x0, float x1, float y, float h, float z0, float z1, int segments, FacadeMaterial m)
        {
            float half = (x1 - x0) / 2, cx = (x0 + x1) / 2;
            // Cercul care trece prin capete și prin vârf: R = (half² + h²) / (2h).
            float r = (half * half + h * h) / (2 * h);
            float cy = y + h - r;
            float a0 = Mathf.Atan2(y - cy, half);
            var outline = new List<Vector2>();
            for (int i = 0; i <= segments; i++)
            {
                float a = Mathf.Lerp(a0, Mathf.PI - a0, (float)i / segments);
                outline.Add(new Vector2(cx + r * Mathf.Cos(a), cy + r * Mathf.Sin(a)));
            }
            Extrude(outline, z0, z1, m);
        }

        /// <summary>O jumătate de elipsoid (cupolă) cu centrul bazei în <paramref name="c"/>.</summary>
        public void Dome(Vector3 c, float rx, float ry, float rz, int segments, int rings, FacadeMaterial m)
        {
            for (int j = 0; j < rings; j++)
            {
                float t0 = Mathf.PI / 2 * j / rings, t1 = Mathf.PI / 2 * (j + 1) / rings;
                for (int i = 0; i < segments; i++)
                {
                    float a0 = 2 * Mathf.PI * i / segments, a1 = 2 * Mathf.PI * (i + 1) / segments;
                    var p00 = Ellipsoid(c, rx, ry, rz, a0, t0); var p10 = Ellipsoid(c, rx, ry, rz, a1, t0);
                    var p11 = Ellipsoid(c, rx, ry, rz, a1, t1); var p01 = Ellipsoid(c, rx, ry, rz, a0, t1);
                    var mid = (p00 + p11) * 0.5f;
                    var outward = new Vector3((mid.x - c.x) / (rx * rx), (mid.y - c.y) / (ry * ry), (mid.z - c.z) / (rz * rz));
                    if (j == rings - 1) Tri(p00, p10, p11, outward, m);
                    else Quad(p00, p10, p11, p01, outward, m);
                }
            }
        }

        static Vector3 Ellipsoid(Vector3 c, float rx, float ry, float rz, float a, float t) =>
            new Vector3(c.x + rx * Mathf.Cos(t) * Mathf.Cos(a), c.y + ry * Mathf.Sin(t), c.z + rz * Mathf.Cos(t) * Mathf.Sin(a));

        /// <summary>
        /// Un acoperiș în patru ape (trunchi de piramidă) peste dreptunghiul x0..x1 × z0..z1, ridicat de la y0 la y1,
        /// cu creasta retrasă cu <paramref name="inset"/>.
        /// </summary>
        public void HipRoof(float x0, float x1, float z0, float z1, float y0, float y1, float inset, FacadeMaterial m)
        {
            var a = new Vector3(x0, y0, z1); var b = new Vector3(x1, y0, z1);
            var c = new Vector3(x1, y0, z0); var d = new Vector3(x0, y0, z0);
            var a2 = new Vector3(x0 + inset, y1, z1 - inset); var b2 = new Vector3(x1 - inset, y1, z1 - inset);
            var c2 = new Vector3(x1 - inset, y1, z0 + inset); var d2 = new Vector3(x0 + inset, y1, z0 + inset);
            Quad(a, b, b2, a2, new Vector3(0, 0.5f, 1), m);
            Quad(b, c, c2, b2, new Vector3(1, 0.5f, 0), m);
            Quad(c, d, d2, c2, new Vector3(0, 0.5f, -1), m);
            Quad(d, a, a2, d2, new Vector3(-1, 0.5f, 0), m);
            Quad(a2, b2, c2, d2, Vector3.up, m);
        }

        /// <summary>
        /// O statuie simplificată (siluetă în picioare): soclu, corp ușor conic, umeri, cap. Doar ca volum, nu ca portret.
        /// </summary>
        public void Statue(float cx, float y0, float cz, float height, FacadeMaterial m)
        {
            float s = height / 2.4f;
            Box(cx - 0.32f * s, cx + 0.32f * s, y0, y0 + 0.25f * s, cz - 0.3f * s, cz + 0.3f * s, m);
            float body0 = y0 + 0.25f * s, body1 = y0 + 1.75f * s;
            // Corp (drapaj): două trunchiuri suprapuse, mai larg jos.
            Frustum(cx, cz, body0, body0 + 0.9f * s, 0.30f * s, 0.22f * s, 10, m);
            Frustum(cx, cz, body0 + 0.9f * s, body1, 0.22f * s, 0.25f * s, 10, m);
            Box(cx - 0.32f * s, cx + 0.32f * s, body1 - 0.15f * s, body1 + 0.05f * s, cz - 0.14f * s, cz + 0.14f * s, m);
            Frustum(cx, cz, body1 + 0.05f * s, body1 + 0.18f * s, 0.08f * s, 0.07f * s, 8, m);
            // Capul: două jumătăți de elipsoid (sus și jos).
            var head = new Vector3(cx, body1 + 0.3f * s, cz);
            Dome(head, 0.13f * s, 0.16f * s, 0.14f * s, 8, 3, m);
            Dome(head, 0.13f * s, -0.12f * s, 0.14f * s, 8, 2, m);
        }

        /// <summary>Un trunchi de con vertical (razele r0 jos, r1 sus), cu capacul de sus.</summary>
        public void Frustum(float cx, float cz, float y0, float y1, float r0, float r1, int segments, FacadeMaterial m)
        {
            for (int i = 0; i < segments; i++)
            {
                float a0 = 2 * Mathf.PI * i / segments, a1 = 2 * Mathf.PI * (i + 1) / segments;
                var b0 = new Vector3(cx + r0 * Mathf.Cos(a0), y0, cz + r0 * Mathf.Sin(a0));
                var b1 = new Vector3(cx + r0 * Mathf.Cos(a1), y0, cz + r0 * Mathf.Sin(a1));
                var t1 = new Vector3(cx + r1 * Mathf.Cos(a1), y1, cz + r1 * Mathf.Sin(a1));
                var t0 = new Vector3(cx + r1 * Mathf.Cos(a0), y1, cz + r1 * Mathf.Sin(a0));
                var outward = new Vector3(Mathf.Cos((a0 + a1) / 2), (r0 - r1) / Mathf.Max(0.001f, y1 - y0), Mathf.Sin((a0 + a1) / 2));
                Quad(b0, b1, t1, t0, outward, m);
                Tri(new Vector3(cx, y1, cz), t0, t1, Vector3.up, m);
            }
        }
    }
}
