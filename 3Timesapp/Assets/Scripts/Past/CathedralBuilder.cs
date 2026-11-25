using System.Collections.Generic;
using UnityEngine;

namespace ThreeTimes.Past
{
    public enum CathedralPhase { Foundation = 0, Scaffold = 1, Complete = 2 }

    /// <summary>
    /// Catedrala Mitropolitană în cele trei momente ale ei, din corpurile 3D din OpenStreetMap (CathedralData):
    /// <list type="bullet">
    /// <item>1936, temelia: placa de beton, primele rânduri de cărămidă, gardul de scânduri, stive de cărămizi,
    /// piatra de temelie și două steaguri la poartă;</item>
    /// <item>1938, șantierul: zidurile pe trei sferturi, turlele fără coif, schele de lemn de jur împrejur și un
    /// catarg de ridicat materiale;</item>
    /// <item>1946, sfințirea: Catedrala întreagă, cu ferestre în arc, brâu, firidele de sub streașină, acoperiș de
    /// țiglă smălțuită, cruci aurite și steaguri în fața intrării.</item>
    /// </list>
    /// Plus „masca”: volumul întreg, pentru „cerul pictat” care ascunde Catedrala de azi în 1936 și 1938.
    /// Coordonate: metri față de CathedralData (x spre est, y în sus, z spre nord).
    /// </summary>
    public static class CathedralBuilder
    {
        const float ScaffoldShare = 0.72f;

        static readonly FacadeMaterial Stone = FacadeMaterial.Stone, Brick = FacadeMaterial.Trim, Concrete = FacadeMaterial.Rustic,
            Glass = FacadeMaterial.Glass, Terrace = FacadeMaterial.Roof, Gold = FacadeMaterial.Sculpture, Wood = FacadeMaterial.Shadow;

        class Poly
        {
            public List<Vector3> Points;
            public float Area;      // > 0: sens trigonometric privit de sus
            public Vector3 Center;
            public float Radius;
        }

        // ---------------------------------------------------------------- fazele

        public static PalaceGeometry Build(CathedralPhase phase)
        {
            var g = new PalaceGeometry { Height = CathedralData.CrossTop };
            var ground = Make(CathedralData.Ground);
            if (phase == CathedralPhase.Foundation)
            {
                Foundation(g, ground);
            }
            else
            {
                foreach (var part in CathedralData.Parts)
                {
                    if (phase == CathedralPhase.Scaffold && part.MinHeight >= 50f) continue;
                    Part(g, part, phase);
                }
                if (phase == CathedralPhase.Scaffold) Derrick(g.Body, ground);
                else Flags(g, ground, 4, 9f);
            }
            for (int i = 0; i < ground.Points.Count; i++)
            {
                g.SegmentStarts.Add(ground.Points[i]);
                g.SegmentEnds.Add(ground.Points[(i + 1) % ground.Points.Count]);
            }
            g.Focus = ground.Center + Vector3.up * 25f;
            return g;
        }

        /// <summary>Volumul întreg al Catedralei, puțin umflat (pentru „cerul pictat”).</summary>
        public static FacadeMesh BuildMask()
        {
            var f = new FacadeMesh();
            foreach (var part in CathedralData.Parts)
            {
                var p = Make(part.Outline);
                var pts = new List<Vector3>();
                foreach (var v in p.Points) pts.Add(p.Center + (v - p.Center) * (1f + 0.5f / Mathf.Max(1f, p.Radius)));
                var inflated = new Poly { Points = pts, Area = p.Area, Center = p.Center, Radius = p.Radius + 0.5f };
                Walls(f, inflated, part.MinHeight, part.Height + 0.4f, Stone);
                Cap(f, inflated, part.Height + 0.4f, Stone);
            }
            // Crucile de pe turla centrală.
            var top = Make(CathedralData.Parts[CathedralData.Parts.Length - 1].Outline).Center;
            f.Box(new Vector3(top.x - 1.4f, 79f, top.z - 1.4f), new Vector3(top.x + 1.4f, CathedralData.CrossTop + 0.6f, top.z + 1.4f), Stone, true);
            return f;
        }

        // ---------------------------------------------------------------- un corp

        static void Part(PalaceGeometry g, CathedralPart part, CathedralPhase phase)
        {
            var p = Make(part.Outline);
            float roofH = part.Roof == PartRoof.Flat ? 0f : (part.RoofHeight > 0f ? part.RoofHeight : 3f);
            float wallTop = part.Roof == PartRoof.Skillion ? part.Height - roofH * 0.5f : part.Height - roofH;
            float y0 = part.MinHeight;
            bool complete = phase == CathedralPhase.Complete;
            float top = complete ? wallTop : y0 + (wallTop - y0) * ScaffoldShare;

            Walls(g.Body, p, y0, top, Stone);

            // Detaliile de pe fiecare latură: brâul, ferestrele în arc, firidele și cornișa.
            float wallH = wallTop - y0;
            for (int i = 0; i < p.Points.Count; i++)
            {
                Edge(p, i, out var a, out var b, out var dir, out var normal, out float length);
                if (length < 1.2f || wallH < 8f) continue;
                var f = new FacadeMesh();
                float band = y0 + wallH * 0.6f;
                if (band + 0.45f <= top) f.Box(-0.02f, length + 0.02f, band, band + 0.45f, 0f, 0.16f, Brick);

                float wy0 = part.WindowFrom > 0f ? part.WindowFrom : y0 + wallH * 0.3f;
                float wy1 = part.WindowTo > 0f ? part.WindowTo : wy0 + Mathf.Min(4.5f, wallH * 0.28f);
                if (length >= 2f && wy1 < wallTop - 1.8f && wy1 <= top)
                {
                    int count = Mathf.Max(1, Mathf.FloorToInt(length / 3.4f));
                    float step = length / count;
                    float w = Mathf.Min(1.1f, step * 0.42f);
                    for (int k = 0; k < count; k++)
                    {
                        float cx = (k + 0.5f) * step;
                        f.ArchPanel(cx, wy0, w, wy1 - w / 2, 0.03f, 0.08f, 8, Glass);
                        f.ArchRing(cx, wy1 - w / 2, w / 2, w / 2 + 0.16f, 0f, 0.13f, 8, Brick);
                        f.Box(cx - w / 2 - 0.16f, cx + w / 2 + 0.16f, wy0 - 0.18f, wy0, 0f, 0.18f, Brick);
                    }
                }
                if (complete)
                {
                    if (part.Height >= 20f && length >= 1.6f)
                    {
                        // Firidele de sub streașină, ca la bisericile moldovenești.
                        int niches = Mathf.FloorToInt(length / 0.95f);
                        float step = length / Mathf.Max(1, niches);
                        for (int k = 0; k < niches; k++)
                            f.ArchPanel((k + 0.5f) * step, wallTop - 1.75f, 0.48f, wallTop - 1.0f, 0f, 0.05f, 4, Wood);
                    }
                    f.Box(-0.25f, length + 0.25f, wallTop - 0.35f, wallTop, 0f, 0.4f, Brick);
                }
                PalaceBuilder.Append(g.Body, f, a, dir, normal);
            }

            if (complete)
            {
                Roof(g, p, part, wallTop, roofH);
                if ((part.Roof == PartRoof.Cone || part.Roof == PartRoof.Pyramidal) && part.Height >= 30f)
                    Cross(g.Body, p.Center, part.Height, part.MinHeight >= 68f ? CathedralData.CrossTop - part.Height : 2.4f);
            }
            else if (top > 4f)
            {
                Scaffold(g.Body, p, y0, top + 1.6f);
            }
        }

        static void Roof(PalaceGeometry g, Poly p, CathedralPart part, float wallTop, float roofH)
        {
            var d = g.Decor;
            var pts = p.Points;
            if (part.Roof == PartRoof.Flat)
            {
                Cap(g.Body, p, wallTop, Terrace);
            }
            else if (part.Roof == PartRoof.Skillion)
            {
                TileCap(d, p, wallTop);
            }
            else if (part.Roof == PartRoof.Gabled && pts.Count == 4)
            {
                int s = Vector3.Distance(pts[0], pts[1]) >= Vector3.Distance(pts[1], pts[2]) ? 0 : 1;
                var p0 = pts[s]; var p1 = pts[(s + 1) % 4]; var p2 = pts[(s + 2) % 4]; var p3 = pts[(s + 3) % 4];
                var m1 = (p1 + p2) * 0.5f; var m3 = (p3 + p0) * 0.5f;
                var up = Vector3.up * wallTop; var ridge = Vector3.up * part.Height;
                TileQuad(d, p0 + up, p1 + up, m1 + ridge, m3 + ridge, Outward(p, p0, p1) + Vector3.up * 0.5f);
                TileQuad(d, p2 + up, p3 + up, m3 + ridge, m1 + ridge, Outward(p, p2, p3) + Vector3.up * 0.5f);
                g.Body.Tri(p1 + up, p2 + up, m1 + ridge, Outward(p, p1, p2), Stone);
                g.Body.Tri(p3 + up, p0 + up, m3 + ridge, Outward(p, p3, p0), Stone);
            }
            else
            {
                // Piramidă / coif: fiecare latură urcă spre vârf; streașina iese puțin în afară.
                var apex = new Vector3(p.Center.x, part.Height, p.Center.z);
                for (int i = 0; i < pts.Count; i++)
                {
                    var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                    var o = Outward(p, a, b);
                    var ea = a + (a - p.Center).normalized * 0.35f + Vector3.up * (wallTop - 0.1f);
                    var eb = b + (b - p.Center).normalized * 0.35f + Vector3.up * (wallTop - 0.1f);
                    float l = Vector3.Distance(ea, eb), slope = Vector3.Distance((ea + eb) * 0.5f, apex);
                    d.Quad(ea, eb, apex, apex, new Vector2(0, 0), new Vector2(l, 0), new Vector2(l * 0.5f, slope), new Vector2(l * 0.5f, slope),
                           o + Vector3.up * 0.4f, DecorMaterial.Tiles);
                }
            }
        }

        static void TileQuad(DecorMesh d, Vector3 a, Vector3 b, Vector3 c, Vector3 e, Vector3 outward)
        {
            float l = Vector3.Distance(a, b), s = Vector3.Distance((a + b) * 0.5f, (c + e) * 0.5f);
            d.Quad(a, b, c, e, new Vector2(0, 0), new Vector2(l, 0), new Vector2(l, s), new Vector2(0, s), outward, DecorMaterial.Tiles);
        }

        static void TileCap(DecorMesh d, Poly p, float y)
        {
            var tris = Triangulate(p);
            for (int i = 0; i < tris.Count; i += 3)
            {
                Vector3 a = p.Points[tris[i]] + Vector3.up * y, b = p.Points[tris[i + 1]] + Vector3.up * y, c = p.Points[tris[i + 2]] + Vector3.up * y;
                d.Quad(a, b, c, c, new Vector2(a.x, a.z), new Vector2(b.x, b.z), new Vector2(c.x, c.z), new Vector2(c.x, c.z), Vector3.up, DecorMaterial.Tiles);
            }
        }

        static void Cross(FacadeMesh f, Vector3 c, float y, float height)
        {
            float t = height > 4f ? 0.16f : 0.08f;
            f.Box(new Vector3(c.x - t, y, c.z - t), new Vector3(c.x + t, y + height, c.z + t), Gold, true);
            float bar = y + height * 0.68f, half = height * 0.3f;
            f.Box(new Vector3(c.x - half, bar - t, c.z - t), new Vector3(c.x + half, bar + t, c.z + t), Gold, true);
            f.Dome(new Vector3(c.x, y, c.z), t * 2.5f, t * 3f, t * 2.5f, 8, 3, Gold);
        }

        // ---------------------------------------------------------------- șantierul

        static void Scaffold(FacadeMesh body, Poly p, float baseY, float top)
        {
            // Corpurile rotunde (multe laturi scurte): schela e un octogon în jurul lor.
            var ring = p;
            if (p.Points.Count > 10)
            {
                var pts = new List<Vector3>();
                for (int k = 0; k < 8; k++)
                {
                    float a = -k * Mathf.PI / 4f;
                    pts.Add(p.Center + new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a)) * p.Radius);
                }
                ring = Make(pts);
            }
            for (int i = 0; i < ring.Points.Count; i++)
            {
                Edge(ring, i, out var a, out var b, out var dir, out var normal, out float length);
                if (length < 1f) continue;
                var f = new FacadeMesh();
                int poles = Mathf.Max(1, Mathf.CeilToInt(length / 3.0f));
                for (int k = 0; k <= poles; k++)
                {
                    float x = length * k / poles;
                    f.Box(x - 0.06f, x + 0.06f, baseY, top, 1.24f, 1.36f, Wood);
                    f.Box(x - 0.06f, x + 0.06f, baseY, top - 1f, 0.3f, 0.42f, Wood);
                }
                for (float y = baseY + 2.5f; y <= top; y += 2.5f)
                {
                    f.Box(-0.15f, length + 0.15f, y - 0.05f, y + 0.05f, 1.36f, 1.46f, Wood);
                    if (Mathf.RoundToInt((y - baseY) / 2.5f) % 2 == 0) f.Box(-0.15f, length + 0.15f, y, y + 0.06f, 0.3f, 1.42f, Wood);
                }
                // O diagonală pe fiecare latură, ca la schelele de lemn.
                float diag = Mathf.Min(length, top - baseY);
                var brace = new List<Vector2> { new Vector2(0, baseY), new Vector2(0.12f, baseY), new Vector2(diag, baseY + diag * 0.9f), new Vector2(diag - 0.12f, baseY + diag * 0.9f) };
                f.Extrude(brace, 1.46f, 1.54f, Wood);
                PalaceBuilder.Append(body, f, a, dir, normal);
            }
        }

        static void Derrick(FacadeMesh f, Poly ground)
        {
            // Catargul de ridicat materiale, lângă turla centrală, cu brațul spre intrare.
            var c = ground.Center + new Vector3(6f, 0, 2f);
            f.Box(new Vector3(c.x - 0.25f, 0, c.z - 0.25f), new Vector3(c.x + 0.25f, 46f, c.z + 0.25f), Wood, true);
            f.Box(new Vector3(c.x - 0.12f, 43f, c.z - 0.12f), new Vector3(c.x + 11f, 43.25f, c.z + 0.12f), Wood, true);
            f.Box(new Vector3(c.x + 10.9f, 37f, c.z - 0.03f), new Vector3(c.x + 11f, 43f, c.z + 0.03f), Wood, true);
        }

        // ---------------------------------------------------------------- temelia

        static void Foundation(PalaceGeometry g, Poly ground)
        {
            var f = g.Body;
            Walls(f, ground, 0f, 0.6f, Concrete);
            Cap(f, ground, 0.6f, Concrete);
            // Primele rânduri de cărămidă, pe contur.
            for (int i = 0; i < ground.Points.Count; i++)
            {
                Edge(ground, i, out var a, out var b, out var dir, out var normal, out float length);
                if (length < 0.6f) continue;
                var e = new FacadeMesh();
                e.Box(0f, length, 0.6f, 1.6f + 0.4f * Mathf.Sin(i * 1.7f), -0.6f, 0f, Brick);
                PalaceBuilder.Append(f, e, a, dir, normal);
            }
            // Piatra de temelie, cu o cruce mică.
            var c = ground.Center;
            f.Box(new Vector3(c.x - 0.5f, 0.6f, c.z - 0.35f), new Vector3(c.x + 0.5f, 1.3f, c.z + 0.35f), Gold, true);
            Cross(f, c, 1.3f, 0.9f);

            // Gardul de scânduri al șantierului, cu poarta spre piață (nord).
            float x0 = float.MaxValue, x1 = float.MinValue, z0 = float.MaxValue, z1 = float.MinValue;
            foreach (var v in ground.Points) { x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); z0 = Mathf.Min(z0, v.z); z1 = Mathf.Max(z1, v.z); }
            x0 -= 7f; x1 += 7f; z0 -= 7f; z1 += 7f;
            float gate = (x0 + x1) * 0.5f;
            Fence(f, new Vector3(x0, 0, z0), new Vector3(x1, 0, z0));
            Fence(f, new Vector3(x1, 0, z0), new Vector3(x1, 0, z1));
            Fence(f, new Vector3(x1, 0, z1), new Vector3(gate + 2.5f, 0, z1));
            Fence(f, new Vector3(gate - 2.5f, 0, z1), new Vector3(x0, 0, z1));
            Fence(f, new Vector3(x0, 0, z1), new Vector3(x0, 0, z0));
            // Stive de cărămizi.
            var stacks = new[] { new Vector2(x0 + 3, z0 + 3), new Vector2(x0 + 5, z0 + 3), new Vector2(x1 - 3, z0 + 4), new Vector2(x1 - 3, z1 - 4), new Vector2(x0 + 3, z1 - 5), new Vector2(x0 + 4.6f, z1 - 5) };
            foreach (var s in stacks) f.Box(new Vector3(s.x - 0.6f, 0, s.y - 0.5f), new Vector3(s.x + 0.6f, 1.1f, s.y + 0.5f), Brick, true);
            // Două steaguri la poartă (ceremonia pietrei de temelie).
            FlagPole(g, new Vector3(gate - 4f, 0, z1 + 0.5f), 7f);
            FlagPole(g, new Vector3(gate + 4f, 0, z1 + 0.5f), 7f);
        }

        static void Fence(FacadeMesh f, Vector3 a, Vector3 b)
        {
            float length = Vector3.Distance(a, b);
            if (length < 0.5f) return;
            var dir = (b - a) / length;
            var normal = Vector3.Cross(dir, Vector3.up);
            var e = new FacadeMesh();
            e.Box(new Vector3(0, 0.15f, -0.03f), new Vector3(length, 2.25f, 0.03f), Wood, true);
            for (float x = 0; x <= length; x += 2.5f) e.Box(new Vector3(x - 0.07f, 0, 0.03f), new Vector3(x + 0.07f, 2.4f, 0.17f), Wood, true);
            PalaceBuilder.Append(f, e, a, dir, normal);
        }

        static void Flags(PalaceGeometry g, Poly ground, int count, float height)
        {
            float x0 = float.MaxValue, x1 = float.MinValue, z1 = float.MinValue;
            foreach (var v in ground.Points) { x0 = Mathf.Min(x0, v.x); x1 = Mathf.Max(x1, v.x); z1 = Mathf.Max(z1, v.z); }
            float cx = (x0 + x1) * 0.5f;
            for (int i = 0; i < count; i++)
            {
                float x = cx + (i - (count - 1) * 0.5f) * 6f;
                FlagPole(g, new Vector3(x, 0, z1 + 9f), height);
            }
        }

        static void FlagPole(PalaceGeometry g, Vector3 at, float height)
        {
            g.Body.Cylinder(at.x, at.z, 0f, height, 0.06f, 6, Wood);
            g.Body.Dome(new Vector3(at.x, height, at.z), 0.12f, 0.15f, 0.12f, 6, 2, Gold);
            var a = new Vector3(at.x + 0.08f, height - 1.4f, at.z);
            var b = new Vector3(at.x + 2.0f, height - 1.4f, at.z);
            var c = new Vector3(at.x + 2.0f, height - 0.15f, at.z);
            var d = new Vector3(at.x + 0.08f, height - 0.15f, at.z);
            g.Decor.Quad(a, b, c, d, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), Vector3.back, DecorMaterial.Flag, true);
        }

        // ---------------------------------------------------------------- geometrie

        static Poly Make(double[] outline)
        {
            var pts = new List<Vector3>();
            int n = outline.Length / 2;
            for (int i = 0; i < n; i++) pts.Add(PalaceCatalog.ToLocal(outline[2 * i], outline[2 * i + 1], CathedralData.Lat, CathedralData.Lon));
            if (pts.Count > 1 && (pts[0] - pts[pts.Count - 1]).sqrMagnitude < 1e-6f) pts.RemoveAt(pts.Count - 1);
            return Make(pts);
        }

        static Poly Make(List<Vector3> pts)
        {
            float area = 0f;
            var center = Vector3.zero;
            for (int i = 0; i < pts.Count; i++)
            {
                var a = pts[i]; var b = pts[(i + 1) % pts.Count];
                area += a.x * b.z - b.x * a.z;
                center += a;
            }
            center /= Mathf.Max(1, pts.Count);
            float radius = 0f;
            foreach (var v in pts) radius = Mathf.Max(radius, Vector3.Distance(v, center));
            return new Poly { Points = pts, Area = area * 0.5f, Center = center, Radius = radius };
        }

        /// <summary>Normala spre exterior a laturii a→b (orizontală).</summary>
        static Vector3 Outward(Poly p, Vector3 a, Vector3 b)
        {
            var d = b - a;
            var n = new Vector3(d.z, 0, -d.x);
            return p.Area > 0 ? n : -n;
        }

        /// <summary>Latura i, orientată așa încât Cross(dir, sus) să fie spre exterior (cadrul folosit de Append).</summary>
        static void Edge(Poly p, int i, out Vector3 a, out Vector3 b, out Vector3 dir, out Vector3 normal, out float length)
        {
            a = p.Points[i]; b = p.Points[(i + 1) % p.Points.Count];
            var outward = Outward(p, a, b);
            if (Vector3.Dot(Vector3.Cross(b - a, Vector3.up), outward) < 0) { var t = a; a = b; b = t; }
            length = Vector3.Distance(a, b);
            dir = length > 1e-5f ? (b - a) / length : Vector3.right;
            normal = Vector3.Cross(dir, Vector3.up);
        }

        static void Walls(FacadeMesh f, Poly p, float y0, float y1, FacadeMaterial m)
        {
            if (y1 - y0 < 0.02f) return;
            for (int i = 0; i < p.Points.Count; i++)
            {
                var a = p.Points[i]; var b = p.Points[(i + 1) % p.Points.Count];
                f.Quad(a + Vector3.up * y0, b + Vector3.up * y0, b + Vector3.up * y1, a + Vector3.up * y1, Outward(p, a, b), m);
            }
        }

        static void Cap(FacadeMesh f, Poly p, float y, FacadeMaterial m)
        {
            var tris = Triangulate(p);
            for (int i = 0; i < tris.Count; i += 3)
                f.Tri(p.Points[tris[i]] + Vector3.up * y, p.Points[tris[i + 1]] + Vector3.up * y, p.Points[tris[i + 2]] + Vector3.up * y, Vector3.up, m);
        }

        /// <summary>Triunghiuri pentru un poligon simplu (și concav), prin „tăierea urechilor”.</summary>
        static List<int> Triangulate(Poly p)
        {
            var result = new List<int>();
            var idx = new List<int>();
            for (int i = 0; i < p.Points.Count; i++) idx.Add(i);
            if (p.Area < 0) idx.Reverse();
            int guard = 0;
            while (idx.Count > 3 && guard++ < 5000)
            {
                bool clipped = false;
                for (int i = 0; i < idx.Count; i++)
                {
                    int i0 = idx[(i + idx.Count - 1) % idx.Count], i1 = idx[i], i2 = idx[(i + 1) % idx.Count];
                    Vector3 a = p.Points[i0], b = p.Points[i1], c = p.Points[i2];
                    float cross = (b.x - a.x) * (c.z - a.z) - (b.z - a.z) * (c.x - a.x);
                    if (cross <= 1e-7f) continue; // colț concav
                    bool inside = false;
                    foreach (int j in idx)
                    {
                        if (j == i0 || j == i1 || j == i2) continue;
                        if (InTriangle(p.Points[j], a, b, c)) { inside = true; break; }
                    }
                    if (inside) continue;
                    result.Add(i0); result.Add(i1); result.Add(i2);
                    idx.RemoveAt(i);
                    clipped = true;
                    break;
                }
                if (!clipped) break;
            }
            if (idx.Count == 3) { result.Add(idx[0]); result.Add(idx[1]); result.Add(idx[2]); }
            return result;
        }

        static bool InTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
        {
            float d1 = Sign(p, a, b), d2 = Sign(p, b, c), d3 = Sign(p, c, a);
            bool neg = d1 < 0 || d2 < 0 || d3 < 0, pos = d1 > 0 || d2 > 0 || d3 > 0;
            return !(neg && pos);
        }

        static float Sign(Vector3 p, Vector3 a, Vector3 b) => (p.x - b.x) * (a.z - b.z) - (a.x - b.x) * (p.z - b.z);
    }
}
