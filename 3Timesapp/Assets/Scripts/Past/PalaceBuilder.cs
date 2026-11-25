using System.Collections.Generic;
using UnityEngine;

namespace ThreeTimes.Past
{
    /// <summary>Părțile cu textură: copertine, firme, pavaj, lumina felinarelor, țigla smălțuită, steaguri.</summary>
    public enum DecorMaterial { Awning = 0, Sign = 1, Paving = 2, Glow = 3, Tiles = 4, Flag = 5 }

    /// <summary>O plasă simplă cu coordonate de textură (UV), câte o sub-plasă pentru fiecare DecorMaterial.</summary>
    public class DecorMesh
    {
        public const int MaterialCount = 6;
        public readonly List<Vector3> Vertices = new List<Vector3>();
        public readonly List<Vector3> Normals = new List<Vector3>();
        public readonly List<Vector2> UVs = new List<Vector2>();
        public readonly List<int>[] Triangles = new List<int>[MaterialCount];

        public DecorMesh()
        {
            for (int i = 0; i < MaterialCount; i++) Triangles[i] = new List<int>();
        }

        /// <summary>Un patrulater (a, b, c, d pe contur) cu UV-urile lui, întors cu fața spre <paramref name="outward"/>.</summary>
        public void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud,
                         Vector3 outward, DecorMaterial m, bool twoSided = false)
        {
            Vector3 n = Vector3.Cross(b - a, c - a);
            if (n.sqrMagnitude < 1e-12f) return;
            if (Vector3.Dot(n, outward) < 0)
            {
                // Același patrulater, parcurs invers.
                var t = b; b = d; d = t;
                var tu = ub; ub = ud; ud = tu;
                n = -n;
            }
            Add(a, b, c, d, ua, ub, uc, ud, n.normalized, m);
            if (twoSided) Add(a, d, c, b, ua, ud, uc, ub, -n.normalized, m);
        }

        void Add(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector2 ua, Vector2 ub, Vector2 uc, Vector2 ud, Vector3 n, DecorMaterial m)
        {
            int i = Vertices.Count;
            Vertices.Add(a); Vertices.Add(b); Vertices.Add(c); Vertices.Add(d);
            UVs.Add(ua); UVs.Add(ub); UVs.Add(uc); UVs.Add(ud);
            for (int k = 0; k < 4; k++) Normals.Add(n);
            var list = Triangles[(int)m];
            list.Add(i); list.Add(i + 1); list.Add(i + 2);
            list.Add(i); list.Add(i + 2); list.Add(i + 3);
        }

        /// <summary>O cutie care luminează (felinar): toate fețele, aceeași culoare.</summary>
        public void GlowBox(Vector3 min, Vector3 max)
        {
            float x0 = min.x, y0 = min.y, z0 = min.z, x1 = max.x, y1 = max.y, z1 = max.z;
            var uv = Vector2.zero;
            Quad(new Vector3(x0, y0, z1), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x0, y1, z1), uv, uv, uv, uv, Vector3.forward, DecorMaterial.Glow);
            Quad(new Vector3(x0, y0, z0), new Vector3(x1, y0, z0), new Vector3(x1, y1, z0), new Vector3(x0, y1, z0), uv, uv, uv, uv, Vector3.back, DecorMaterial.Glow);
            Quad(new Vector3(x0, y0, z0), new Vector3(x0, y0, z1), new Vector3(x0, y1, z1), new Vector3(x0, y1, z0), uv, uv, uv, uv, Vector3.left, DecorMaterial.Glow);
            Quad(new Vector3(x1, y0, z0), new Vector3(x1, y0, z1), new Vector3(x1, y1, z1), new Vector3(x1, y1, z0), uv, uv, uv, uv, Vector3.right, DecorMaterial.Glow);
        }
    }

    /// <summary>Un palat gata de afișat: corpul (materialele fațadelor), decorul și liniile fațadelor (pentru praf).</summary>
    public class PalaceGeometry
    {
        public FacadeMesh Body = new FacadeMesh();
        public DecorMesh Decor = new DecorMesh();
        /// <summary>Capetele fiecărei laturi, în coordonatele palatului (pentru praful auriu).</summary>
        public readonly List<Vector3> SegmentStarts = new List<Vector3>(), SegmentEnds = new List<Vector3>();
        /// <summary>Mijlocul fațadei principale, la jumătatea înălțimii (unde „te uiți” la palat).</summary>
        public Vector3 Focus;
        public float Height;
    }

    /// <summary>
    /// Construiește un palat din PalaceSpec, în metri față de originea lui (x spre est, y în sus, z spre nord):
    /// pentru fiecare latură a conturului, un zid cu parter de prăvălii (vitrine, copertine în dungi, firme), etaje
    /// cu ferestre, brâuri, bovindouri, cornișă, frontoane și acoperiș; felinare electrice pe trotuar și o fâșie de
    /// pavaj. Nu depinde de scenă (se poate verifica și în afara Unity).
    /// </summary>
    public static class PalaceBuilder
    {
        const float Ground = 5.0f;       // înălțimea parterului
        const float Sidewalk = 6.5f;     // lățimea pavajului din fața clădirii
        const float LampOffset = 3.6f;   // felinarele, de la zid
        const float LampSpacing = 21f;

        static readonly FacadeMaterial Stone = FacadeMaterial.Stone, Trim = FacadeMaterial.Trim, Rustic = FacadeMaterial.Rustic,
            Glass = FacadeMaterial.Glass, Roof = FacadeMaterial.Roof, Ornament = FacadeMaterial.Sculpture, Dark = FacadeMaterial.Shadow;

        public static PalaceGeometry Build(PalaceSpec p)
        {
            var g = new PalaceGeometry { Height = p.Height };
            PalaceCatalog.Origin(p, out double lat0, out double lon0);

            // Conturul în metri, ordonat așa încât „afară” să fie la dreapta direcției de mers (Cross(dir, sus)).
            int n = p.Facade.Length / 2;
            var pts = new List<Vector3>(n);
            for (int i = 0; i < n; i++) pts.Add(PalaceCatalog.ToLocal(p.Facade[2 * i], p.Facade[2 * i + 1], lat0, lon0));
            var inside = PalaceCatalog.ToLocal(p.InsideLat, p.InsideLon, lat0, lon0);
            {
                // Pentru un contur deschis, cea mai sigură probă e latura cea mai lungă.
                int longest = Longest(pts);
                var a = pts[longest]; var b = pts[longest + 1];
                var outward = Vector3.Cross((b - a).normalized, Vector3.up);
                if (Vector3.Dot(outward, (a + b) * 0.5f - inside) < 0) pts.Reverse();
            }
            int main = Longest(pts);
            int domeVertex = p.DomeAtVertex >= 0 && pts.Count > 0 && Reversed(p, pts, lat0, lon0) ? (n - 1 - p.DomeAtVertex) : p.DomeAtVertex;
            int pedimentSegment = p.PedimentOnSegment >= 0 && Reversed(p, pts, lat0, lon0) ? (n - 2 - p.PedimentOnSegment) : p.PedimentOnSegment;

            int signCursor = 0;
            for (int s = 0; s < pts.Count - 1; s++)
            {
                var a = pts[s]; var b = pts[s + 1];
                float length = Vector3.Distance(a, b);
                if (length < 0.05f) continue;
                var dir = (b - a) / length;
                var normal = Vector3.Cross(dir, Vector3.up);

                var body = new FacadeMesh();
                var decor = new DecorMesh();
                BuildSegment(p, body, decor, length, s == main, s == pedimentSegment, s == 0, s == pts.Count - 2, ref signCursor);
                Append(g.Body, body, a, dir, normal);
                Append(g.Decor, decor, a, dir, normal);

                g.SegmentStarts.Add(a);
                g.SegmentEnds.Add(b);
                if (s == main) g.Focus = (a + b) * 0.5f + Vector3.up * p.Cornice * 0.5f;
            }

            if (domeVertex > 0 && domeVertex < pts.Count - 1) CornerDome(p, g.Body, pts, domeVertex);
            return g;
        }

        // Conturul a fost întors față de ordinea din catalog? (indicii de colț / latură se dau în ordinea din catalog)
        static bool Reversed(PalaceSpec p, List<Vector3> pts, double lat0, double lon0)
        {
            var first = PalaceCatalog.ToLocal(p.Facade[0], p.Facade[1], lat0, lon0);
            return (pts[0] - first).sqrMagnitude > 1e-4f;
        }

        static int Longest(List<Vector3> pts)
        {
            int best = 0; float bestLength = -1f;
            for (int i = 0; i < pts.Count - 1; i++)
            {
                float l = Vector3.Distance(pts[i], pts[i + 1]);
                if (l > bestLength) { bestLength = l; best = i; }
            }
            return best;
        }

        // ---------------------------------------------------------------- o latură

        // Coordonate locale: x de-a lungul laturii (0..length), y în sus, z în afară (spre piață / stradă).
        static void BuildSegment(PalaceSpec p, FacadeMesh f, DecorMesh d, float length, bool isMain, bool pediment,
                                 bool first, bool last, ref int signCursor)
        {
            float hc = p.Cornice;
            // Zidul, cu puțin peste capete ca să se închidă colțurile.
            f.Quad(new Vector3(-0.05f, 0, 0), new Vector3(length + 0.05f, 0, 0), new Vector3(length + 0.05f, hc, 0),
                   new Vector3(-0.05f, hc, 0), Vector3.forward, Stone);
            // Soclul și brâul de deasupra parterului.
            f.Box(-0.1f, length + 0.1f, 0f, 0.55f, 0f, 0.14f, Rustic);
            f.Box(-0.1f, length + 0.1f, Ground - 0.15f, Ground + 0.2f, 0f, 0.28f, Trim);
            // Cornișa (în trepte) și friza de sub ea.
            f.Box(-0.3f, length + 0.3f, hc - 0.9f, hc - 0.6f, 0f, 0.18f, Trim);
            f.Box(-0.4f, length + 0.4f, hc - 0.5f, hc - 0.2f, 0f, 0.55f, Trim);
            f.Box(-0.5f, length + 0.5f, hc - 0.2f, hc, 0f, 0.85f, Trim);

            Roofs(p, f, length, first, last);

            // Laturile foarte scurte (colțuri teșite, trepte): doar zid, brâie, acoperiș.
            if (length < 2.6f) return;

            int bays = Mathf.Max(1, Mathf.RoundToInt(length / p.BayWidth));
            float bw = length / bays;
            float top = hc - 0.9f;
            float floorH = (top - (Ground + 0.2f)) / Mathf.Max(1, p.Floors);

            for (int i = 0; i < bays; i++)
            {
                float cx = (i + 0.5f) * bw;
                bool bayWindow = p.BayWindowEvery > 0 && bays >= 3 && length >= 10f && i % p.BayWindowEvery == p.BayWindowEvery / 2
                                 && i != 0 && i != bays - 1;

                // Parterul: vitrine sau ușă.
                bool door = i % 4 == 2 || bays == 1;
                if (door) Door(f, cx);
                else
                {
                    ShopWindow(f, cx, bw);
                    if (p.Signs.Length > 0 && (signCursor % 3 != 2 || bays < 3))
                    {
                        int row = p.Signs[signCursor % p.Signs.Length];
                        Sign(d, cx, bw, row);
                    }
                    if (Hash(i * 7 + (int)(length * 10)) < p.AwningShare) Awning(d, cx, bw);
                    signCursor++;
                }

                // Etajele.
                if (bayWindow)
                {
                    BayWindow(f, cx, top, floorH, p.Floors);
                    continue;
                }
                for (int j = 0; j < p.Floors; j++)
                {
                    float y0 = Ground + 0.2f + j * floorH;
                    Windows(p, f, cx, y0, floorH, j);
                }
            }

            // Brâurile dintre etaje.
            for (int j = 1; j < p.Floors; j++)
            {
                float y = Ground + 0.2f + j * floorH;
                f.Box(-0.05f, length + 0.05f, y - 0.1f, y + 0.1f, 0f, 0.12f, Trim);
            }

            // Frontoane curbe (Secession) pe latura principală.
            if (isMain && p.GableEvery > 0 && bays >= 2)
            {
                for (int i = p.GableEvery / 2; i < bays; i += p.GableEvery)
                {
                    float cx = (i + 0.5f) * bw;
                    float half = Mathf.Min(bw * 1.1f, 4.2f);
                    f.Box(cx - half, cx + half, hc, hc + 1.6f, -0.3f, 0.25f, Stone);
                    f.SegmentPediment(cx - half - 0.2f, cx + half + 0.2f, hc + 1.6f, 2.2f, -0.3f, 0.35f, 12, Trim);
                    f.SegmentPediment(cx - half, cx + half, hc + 1.6f, 1.9f, -0.3f, 0.3f, 12, Stone);
                    f.Disc(cx, hc + 2.0f, 0.65f, 0.3f, 0.36f, 14, Glass);
                    f.Box(cx - half + 0.4f, cx + half - 0.4f, hc + 0.3f, hc + 0.9f, 0.25f, 0.33f, Ornament);
                }
            }
            // Fronton triunghiular (eclectic), la mijlocul laturii.
            if (pediment)
            {
                float cx = length / 2, half = Mathf.Min(length * 0.35f, 7f);
                f.Box(cx - half, cx + half, hc, hc + 1.2f, -0.3f, 0.3f, Stone);
                f.Pediment(cx - half - 0.4f, cx + half + 0.4f, hc + 1.2f, 3.4f, -0.3f, 0.5f, Trim);
                f.Pediment(cx - half, cx + half, hc + 1.3f, 2.9f, -0.3f, 0.55f, Stone);
                f.Disc(cx, hc + 2.4f, 0.75f, 0.55f, 0.65f, 16, Ornament);
            }

            // Felinarele și pavajul de pe trotuar.
            for (float x = Mathf.Min(6f, length / 2); x < length - 2.5f; x += LampSpacing) Lamp(f, d, x, LampOffset);
            d.Quad(new Vector3(0, 0.03f, 0.05f), new Vector3(length, 0.03f, 0.05f), new Vector3(length, 0.03f, Sidewalk), new Vector3(0, 0.03f, Sidewalk),
                   new Vector2(0, 0), new Vector2(length / 2.4f, 0), new Vector2(length / 2.4f, 1), new Vector2(0, 1), Vector3.up, DecorMaterial.Paving);
        }

        static void Roofs(PalaceSpec p, FacadeMesh f, float length, bool first, bool last)
        {
            float hc = p.Cornice, hr = p.Height, x0 = -0.6f, x1 = length + 0.6f;
            if (p.Roof == PalaceRoof.Mansard)
            {
                float yk = hc + Mathf.Min(3.6f, (hr - hc) * 0.65f);
                f.Quad(new Vector3(x0, hc, 0.6f), new Vector3(x1, hc, 0.6f), new Vector3(x1, yk, -1.1f), new Vector3(x0, yk, -1.1f), new Vector3(0, 0.4f, 1), Roof);
                f.Quad(new Vector3(x0, yk, -1.1f), new Vector3(x1, yk, -1.1f), new Vector3(x1, hr, -7f), new Vector3(x0, hr, -7f), new Vector3(0, 1, 0.3f), Roof);
                // Lucarne pe panta abruptă.
                if (length > 6f)
                {
                    int count = Mathf.Max(1, Mathf.RoundToInt(length / (p.BayWidth * 2)));
                    float step = length / count;
                    for (int i = 0; i < count; i++)
                    {
                        float cx = (i + 0.5f) * step;
                        f.Box(cx - 0.75f, cx + 0.75f, hc + 0.4f, hc + 2.5f, -1.8f, 0.05f, Stone);
                        f.Quad(new Vector3(cx - 0.45f, hc + 0.7f, 0.08f), new Vector3(cx + 0.45f, hc + 0.7f, 0.08f),
                               new Vector3(cx + 0.45f, hc + 2.0f, 0.08f), new Vector3(cx - 0.45f, hc + 2.0f, 0.08f), Vector3.forward, Glass);
                        f.Pediment(cx - 0.95f, cx + 0.95f, hc + 2.5f, 0.7f, -1.8f, 0.1f, Trim);
                    }
                }
            }
            else
            {
                f.Quad(new Vector3(x0, hc, 0.6f), new Vector3(x1, hc, 0.6f), new Vector3(x1, hr, -7f), new Vector3(x0, hr, -7f), new Vector3(0, 0.6f, 1), Roof);
            }
            // Capetele acoperișului, la capetele conturului.
            if (first) f.Tri(new Vector3(x0, hc, 0.6f), new Vector3(x0, hr, -7f), new Vector3(x0, hc, -7f), Vector3.left, Roof);
            if (last) f.Tri(new Vector3(x1, hc, 0.6f), new Vector3(x1, hr, -7f), new Vector3(x1, hc, -7f), Vector3.right, Roof);
        }

        static void ShopWindow(FacadeMesh f, float cx, float bw)
        {
            float half = bw / 2 - 0.45f;
            f.Quad(new Vector3(cx - half, 0.55f, 0.06f), new Vector3(cx + half, 0.55f, 0.06f), new Vector3(cx + half, 3.9f, 0.06f),
                   new Vector3(cx - half, 3.9f, 0.06f), Vector3.forward, Glass);
            // Rama de lemn: margini, montant, traversă.
            f.Box(cx - half - 0.14f, cx - half, 0.55f, 3.9f, 0f, 0.16f, Dark);
            f.Box(cx + half, cx + half + 0.14f, 0.55f, 3.9f, 0f, 0.16f, Dark);
            f.Box(cx - half - 0.14f, cx + half + 0.14f, 3.9f, 4.02f, 0f, 0.18f, Dark);
            f.Box(cx - 0.05f, cx + 0.05f, 0.55f, 3.9f, 0.06f, 0.14f, Dark);
            f.Box(cx - half, cx + half, 3.0f, 3.08f, 0.06f, 0.14f, Dark);
            f.Box(cx - half - 0.14f, cx + half + 0.14f, 0.45f, 0.62f, 0f, 0.2f, Dark);
        }

        static void Door(FacadeMesh f, float cx)
        {
            f.ArchPanel(cx, 0.02f, 1.9f, 2.9f, 0.04f, 0.08f, 10, Glass);
            f.ArchRing(cx, 2.9f, 0.95f, 1.2f, 0f, 0.2f, 10, Trim);
            f.Box(cx - 1.2f, cx - 0.95f, 0f, 2.9f, 0f, 0.2f, Trim);
            f.Box(cx + 0.95f, cx + 1.2f, 0f, 2.9f, 0f, 0.2f, Trim);
            f.Box(cx - 0.3f, cx + 0.3f, 4.05f, 4.55f, 0f, 0.22f, Ornament);
        }

        static void Sign(DecorMesh d, float cx, float bw, int row)
        {
            float half = bw / 2 - 0.35f;
            float v0 = 1f - (row + 1f) / PalaceCatalog.SignRows, v1 = 1f - (float)row / PalaceCatalog.SignRows;
            d.Quad(new Vector3(cx - half, 4.1f, 0.2f), new Vector3(cx + half, 4.1f, 0.2f), new Vector3(cx + half, 4.72f, 0.2f), new Vector3(cx - half, 4.72f, 0.2f),
                   new Vector2(0, v0), new Vector2(1, v0), new Vector2(1, v1), new Vector2(0, v1), Vector3.forward, DecorMaterial.Sign);
        }

        static void Awning(DecorMesh d, float cx, float bw)
        {
            float half = bw / 2 - 0.3f;
            float stripes = Mathf.Round(half * 2 / 0.45f);
            var a = new Vector3(cx - half, 3.98f, 0.18f); var b = new Vector3(cx + half, 3.98f, 0.18f);
            var c = new Vector3(cx + half, 3.15f, 1.75f); var e = new Vector3(cx - half, 3.15f, 1.75f);
            d.Quad(a, b, c, e, new Vector2(0, 1), new Vector2(stripes, 1), new Vector2(stripes, 0.2f), new Vector2(0, 0.2f),
                   new Vector3(0, 1, 0.6f), DecorMaterial.Awning, true);
            // Volanul din față.
            d.Quad(e, c, c + Vector3.down * 0.32f, e + Vector3.down * 0.32f, new Vector2(0, 0.2f), new Vector2(stripes, 0.2f),
                   new Vector2(stripes, 0f), new Vector2(0, 0f), Vector3.forward, DecorMaterial.Awning, true);
        }

        static void Windows(PalaceSpec p, FacadeMesh f, float cx, float y0, float floorH, int floor)
        {
            float wh = floorH * 0.58f, wy = y0 + floorH * 0.2f;
            int count = Mathf.Max(1, p.WindowsPerBay);
            float w = count == 1 ? 1.3f : 0.62f, gap = 0.22f;
            float total = count * w + (count - 1) * gap;
            for (int k = 0; k < count; k++)
            {
                float x0 = cx - total / 2 + k * (w + gap), x1 = x0 + w;
                f.Quad(new Vector3(x0, wy, 0.05f), new Vector3(x1, wy, 0.05f), new Vector3(x1, wy + wh, 0.05f), new Vector3(x0, wy + wh, 0.05f), Vector3.forward, Glass);
                // Rama și crucea ferestrei.
                f.Box(x0 - 0.1f, x0, wy, wy + wh, 0f, 0.12f, Trim);
                f.Box(x1, x1 + 0.1f, wy, wy + wh, 0f, 0.12f, Trim);
                f.Box(x0 - 0.1f, x1 + 0.1f, wy + wh, wy + wh + 0.12f, 0f, 0.12f, Trim);
                f.Box((x0 + x1) / 2 - 0.035f, (x0 + x1) / 2 + 0.035f, wy, wy + wh, 0.05f, 0.1f, Trim);
                f.Box(x0, x1, wy + wh * 0.68f, wy + wh * 0.68f + 0.07f, 0.05f, 0.1f, Trim);
                if (p.Shutters && count == 1)
                {
                    f.Box(x0 - 0.72f, x0 - 0.12f, wy, wy + wh, 0.02f, 0.07f, Dark);
                    f.Box(x1 + 0.12f, x1 + 0.72f, wy, wy + wh, 0.02f, 0.07f, Dark);
                }
            }
            // Pervazul, iar la primul etaj o cornișă mică deasupra.
            f.Box(cx - total / 2 - 0.22f, cx + total / 2 + 0.22f, wy - 0.16f, wy, 0f, 0.24f, Trim);
            if (floor == 0)
            {
                f.Box(cx - total / 2 - 0.3f, cx + total / 2 + 0.3f, wy + wh + 0.18f, wy + wh + 0.36f, 0f, 0.28f, Trim);
                f.Box(cx - 0.45f, cx + 0.45f, wy + wh + 0.36f, wy + wh + 0.6f, 0f, 0.16f, Ornament);
            }
            // Panouri ornamentale sub ferestrele etajului al doilea.
            if (floor == 1) f.Box(cx - total / 2, cx + total / 2, y0 + 0.12f, wy - 0.25f, 0f, 0.07f, Ornament);
        }

        static void BayWindow(FacadeMesh f, float cx, float top, float floorH, int floors)
        {
            float half = 1.7f, depth = 0.9f, y0 = Ground + 0.35f, y1 = top - 0.25f;
            f.Box(cx - half - 0.2f, cx + half + 0.2f, Ground + 0.05f, y0, 0f, depth + 0.15f, Trim);
            f.Box(cx - half, cx + half, y0, y1, 0f, depth, Stone);
            f.Box(cx - half - 0.15f, cx + half + 0.15f, y1, y1 + 0.3f, 0f, depth + 0.2f, Trim);
            for (int j = 0; j < floors; j++)
            {
                float fy = Ground + 0.2f + j * floorH;
                float wy = Mathf.Max(fy + floorH * 0.2f, y0 + 0.3f), wh = Mathf.Min(floorH * 0.58f, y1 - 0.2f - wy);
                if (wh < 0.5f) continue;
                for (int k = -1; k <= 1; k++)
                {
                    float wx = cx + k * 1.08f;
                    f.Quad(new Vector3(wx - 0.42f, wy, depth + 0.04f), new Vector3(wx + 0.42f, wy, depth + 0.04f),
                           new Vector3(wx + 0.42f, wy + wh, depth + 0.04f), new Vector3(wx - 0.42f, wy + wh, depth + 0.04f), Vector3.forward, Glass);
                }
                f.Box(cx - half, cx + half, fy - 0.08f, fy + 0.08f, depth, depth + 0.1f, Trim);
            }
        }

        static void Lamp(FacadeMesh f, DecorMesh d, float x, float z)
        {
            // Felinar electric de fontă: soclu, stâlp, inel, lanternă cu geam luminos, căciulă și vârf.
            f.Frustum(x, z, 0f, 0.9f, 0.26f, 0.15f, 10, Dark);
            f.Cylinder(x, z, 0.9f, 4.25f, 0.075f, 8, Dark);
            f.Frustum(x, z, 3.9f, 4.4f, 0.09f, 0.2f, 8, Dark);
            d.GlowBox(new Vector3(x - 0.2f, 4.42f, z - 0.2f), new Vector3(x + 0.2f, 5.0f, z + 0.2f));
            f.Box(x - 0.24f, x + 0.24f, 4.36f, 4.44f, z - 0.24f, z + 0.24f, Dark);
            f.Frustum(x, z, 5.0f, 5.32f, 0.3f, 0.06f, 8, Dark);
            f.Cylinder(x, z, 5.32f, 5.6f, 0.03f, 6, Dark);
        }

        static void CornerDome(PalaceSpec p, FacadeMesh f, List<Vector3> pts, int v)
        {
            // Turnul de colț: tambur și cupolă, puțin retrase de la colț spre interior.
            var prev = (pts[v] - pts[v - 1]).normalized; var next = (pts[v + 1] - pts[v]).normalized;
            var outward = (Vector3.Cross(prev, Vector3.up) + Vector3.Cross(next, Vector3.up)).normalized;
            var c = pts[v] - outward * 2.6f;
            float y0 = p.Cornice - 0.2f, y1 = p.Cornice + 3.4f;
            f.Cylinder(c.x, c.z, y0, y1, 3.0f, 16, FacadeMaterial.Stone);
            f.Frustum(c.x, c.z, y1, y1 + 0.3f, 3.25f, 3.25f, 16, FacadeMaterial.Trim);
            f.Dome(new Vector3(c.x, y1 + 0.3f, c.z), 3.1f, 4.2f, 3.1f, 16, 6, FacadeMaterial.Roof);
            f.Cylinder(c.x, c.z, y1 + 4.3f, y1 + 5.4f, 0.5f, 10, FacadeMaterial.Trim);
            f.Dome(new Vector3(c.x, y1 + 5.4f, c.z), 0.6f, 0.9f, 0.6f, 10, 3, FacadeMaterial.Roof);
            for (int k = 0; k < 6; k++)
            {
                float a = k * Mathf.PI / 3f;
                var w = new Vector3(Mathf.Cos(a), 0, Mathf.Sin(a));
                // Ferestre pe tambur (pătrate de sticlă puțin ieșite).
                var pc = c + w * 3.02f;
                var side = Vector3.Cross(Vector3.up, w) * 0.45f;
                f.Quad(pc - side + Vector3.up * (y0 + 1.0f), pc + side + Vector3.up * (y0 + 1.0f),
                       pc + side + Vector3.up * (y0 + 2.8f), pc - side + Vector3.up * (y0 + 2.8f), w, FacadeMaterial.Glass);
            }
        }

        // ---------------------------------------------------------------- utilitare

        internal static void Append(FacadeMesh target, FacadeMesh src, Vector3 origin, Vector3 dir, Vector3 normal)
        {
            int offset = target.Vertices.Count;
            for (int i = 0; i < src.Vertices.Count; i++)
            {
                var v = src.Vertices[i]; var nn = src.Normals[i];
                target.Vertices.Add(origin + dir * v.x + Vector3.up * v.y + normal * v.z);
                target.Normals.Add(dir * nn.x + Vector3.up * nn.y + normal * nn.z);
            }
            for (int m = 0; m < FacadeMesh.MaterialCount; m++)
                foreach (int t in src.Triangles[m]) target.Triangles[m].Add(t + offset);
        }

        internal static void Append(DecorMesh target, DecorMesh src, Vector3 origin, Vector3 dir, Vector3 normal)
        {
            int offset = target.Vertices.Count;
            for (int i = 0; i < src.Vertices.Count; i++)
            {
                var v = src.Vertices[i]; var nn = src.Normals[i];
                target.Vertices.Add(origin + dir * v.x + Vector3.up * v.y + normal * v.z);
                target.Normals.Add(dir * nn.x + Vector3.up * nn.y + normal * nn.z);
                target.UVs.Add(src.UVs[i]);
            }
            for (int m = 0; m < DecorMesh.MaterialCount; m++)
                foreach (int t in src.Triangles[m]) target.Triangles[m].Add(t + offset);
        }

        static float Hash(int n)
        {
            n = (n << 13) ^ n;
            return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f;
        }
    }
}
