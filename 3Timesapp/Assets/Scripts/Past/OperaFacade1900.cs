using UnityEngine;

namespace ThreeTimes.Past
{
    /// <summary>
    /// Fațada principală a Teatrului Ferenc József din Timișoara (azi Opera / Palatul Culturii), așa cum arăta între
    /// 1882 și 1920: proiectul Fellner &amp; Helmer (1871–1875), neorenascentist, înainte ca Duiliu Marcu să o refacă.
    /// Construită din forme după cărțile poștale de epocă (de ex. OSZK, „Temesvár. Belváros; Városi Ferenc József
    /// Színház”, 1914): parter cu bosaje și arcade, risalit central cu balcon și loggia în trei arce pe coloane, nișe cu
    /// statui, atic cu medalioane, pavilioane cu frontoane curbe, acoperiș de cupru cu cupolă joasă.
    ///
    /// Origine: mijlocul fațadei, la nivelul trotuarului, în planul aripilor laterale. x spre dreapta (privind fațada),
    /// y în sus, z spre piață. Dimensiunile sunt estimate (proporțiile din fotografii, ~42 m lățime); se potrivesc pe
    /// clădire în modul de aliniere (mutare, rotire, scalare).
    /// </summary>
    public static class OperaFacade1900
    {
        // Lățimi
        const float HalfWidth = 21f;      // jumătate din lățimea totală
        const float RisalitHalf = 8f;     // jumătate din risalitul central
        const float RisalitDepth = 1.0f;  // cât iese risalitul în față
        const float InnerHalf = 5.4f;     // jumătate din partea cu cele trei arce (risalitul fără pavilioanele lui)

        // Înălțimi
        const float Plinth = 0.6f;
        const float GroundTop = 6.6f;     // parterul (bosaje)
        const float BandTop = 7.1f;       // brâul de peste parter
        const float FloorBand = 13.8f;    // brâul dintre etaje (aripi)
        const float CorniceY = 19.6f;     // cornișa principală (aripi)
        const float SideTop = 21.8f;      // atic aripi
        const float RisalitTop = 21.6f;   // cornișa risalitului

        const FacadeMaterial Stone = FacadeMaterial.Stone;
        const FacadeMaterial Trim = FacadeMaterial.Trim;
        const FacadeMaterial Rustic = FacadeMaterial.Rustic;
        const FacadeMaterial Glass = FacadeMaterial.Glass;
        const FacadeMaterial Roof = FacadeMaterial.Roof;
        const FacadeMaterial Sculpture = FacadeMaterial.Sculpture;
        const FacadeMaterial Shadow = FacadeMaterial.Shadow;

        public static FacadeMesh Build()
        {
            var m = new FacadeMesh();
            Walls(m);
            for (int side = -1; side <= 1; side += 2) Wing(m, side);
            Risalit(m);
            Roofs(m);
            return m;
        }

        // ---------------------------------------------------------------- volumul și brâiele

        static void Walls(FacadeMesh m)
        {
            const float back = -1.5f;
            float zr = RisalitDepth;
            // Aripile
            for (int side = 0; side < 2; side++)
            {
                float x0 = side == 0 ? -HalfWidth : RisalitHalf;
                float x1 = side == 0 ? -RisalitHalf : HalfWidth;
                m.Box(x0, x1, 0, Plinth, 0, 0.25f, Rustic);
                m.Box(x0, x1, 0, GroundTop, back, 0, Rustic);
                Rustication(m, x0, x1, 0);
                m.Box(x0, x1, GroundTop, BandTop, 0, 0.3f, Trim);
                m.Box(x0, x1, BandTop, CorniceY, back, 0, Stone);
                m.Box(x0, x1, FloorBand, FloorBand + 0.55f, 0, 0.16f, Trim);
                // Cornișa principală, în trepte
                m.Box(x0, x1, CorniceY, CorniceY + 0.4f, back, 0.25f, Trim);
                m.Box(x0, x1, CorniceY + 0.4f, CorniceY + 0.9f, back, 0.5f, Trim);
                m.Box(x0, x1, CorniceY + 0.9f, CorniceY + 1.4f, back, 0.85f, Trim);
                // Console sub cornișă
                for (float x = x0 + 0.5f; x < x1 - 0.3f; x += 1.1f)
                    m.Box(x - 0.12f, x + 0.12f, CorniceY - 0.35f, CorniceY, 0, 0.3f, Trim);
                m.Box(x0, x1, CorniceY + 1.4f, SideTop, back, 0.1f, Stone);
                m.Box(x0, x1, SideTop, SideTop + 0.15f, back, 0.2f, Trim);
            }

            // Risalitul
            float rx = RisalitHalf;
            m.Box(-rx, rx, 0, Plinth, zr, zr + 0.25f, Rustic);
            m.Box(-rx, rx, 0, GroundTop, back, zr, Rustic);
            Rustication(m, -rx, rx, zr);
            m.Box(-rx - 0.2f, rx + 0.2f, GroundTop, BandTop, zr, zr + 0.3f, Trim);
            m.Box(-rx, rx, BandTop, RisalitTop, back, zr, Stone);
            m.Box(-rx - 0.2f, rx + 0.2f, RisalitTop, RisalitTop + 0.4f, back, zr + 0.3f, Trim);
            m.Box(-rx - 0.3f, rx + 0.3f, RisalitTop + 0.4f, RisalitTop + 0.9f, back, zr + 0.6f, Trim);
            m.Box(-rx - 0.4f, rx + 0.4f, RisalitTop + 0.9f, RisalitTop + 1.3f, back, zr + 0.9f, Trim);
            for (float x = -rx + 0.4f; x < rx - 0.2f; x += 1.0f)
                m.Box(x - 0.12f, x + 0.12f, RisalitTop - 0.35f, RisalitTop, zr, zr + 0.3f, Trim);

            // Lesene / bosaje la colțuri
            foreach (float x in new[] { -HalfWidth, HalfWidth - 0.75f })
                Quoins(m, x, x + 0.75f, BandTop, CorniceY, 0);
            foreach (float x in new[] { -RisalitHalf, RisalitHalf - 0.7f })
                Quoins(m, x, x + 0.7f, BandTop, RisalitTop, zr);
        }

        /// <summary>Rosturile orizontale ale bosajelor de la parter.</summary>
        static void Rustication(FacadeMesh m, float x0, float x1, float z)
        {
            for (float y = Plinth + 0.5f; y < GroundTop - 0.2f; y += 0.55f)
                m.Box(x0, x1, y, y + 0.07f, z, z + 0.012f, Shadow, false);
        }

        /// <summary>Colțuri cu blocuri alternante (lung / scurt), ușor ieșite.</summary>
        static void Quoins(FacadeMesh m, float x0, float x1, float y0, float y1, float z)
        {
            bool longBlock = true;
            for (float y = y0; y < y1 - 0.1f; y += 0.62f)
            {
                float top = Mathf.Min(y + 0.55f, y1);
                float w = longBlock ? (x1 - x0) : (x1 - x0) * 0.7f;
                float cx = (x0 + x1) / 2;
                m.Box(cx - w / 2, cx + w / 2, y, top, z, z + 0.12f, Trim);
                longBlock = !longBlock;
            }
        }

        // ---------------------------------------------------------------- aripile (câte 3 travee)

        static void Wing(FacadeMesh m, int side)
        {
            float x0 = side < 0 ? -HalfWidth : RisalitHalf;
            float bay = (HalfWidth - RisalitHalf) / 3f;
            for (int i = 0; i < 3; i++)
            {
                float cx = x0 + bay * (i + 0.5f);
                bool middle = i == 1;

                // Parter: fereastră în arc, cu cheie de boltă
                ArchedOpening(m, cx, 1.4f, 1.8f, 3.4f, 0, 0.28f, 1.15f);

                // Etajul nobil: fereastră cu ancadrament, balconaș cu balustradă, cornișă pe console și fronton
                float w = 1.7f, sill = 8.3f, top = 11.6f;
                m.Box(cx - w / 2, cx + w / 2, sill, top, 0, 0.03f, Glass);
                Mullions(m, cx, w, sill, top, 0.03f);
                Frame(m, cx, w, sill, top, 0.2f, 0, 0.18f);
                m.Box(cx - 1.25f, cx + 1.25f, 7.8f, 8.1f, 0, 0.4f, Trim);
                Balustrade(m, cx - 1.15f, cx + 1.15f, BandTop, 7.8f, 0.1f, 0.4f);
                m.Box(cx - 1.12f, cx - 0.92f, 11.2f, top + 0.3f, 0, 0.32f, Trim);
                m.Box(cx + 0.92f, cx + 1.12f, 11.2f, top + 0.3f, 0, 0.32f, Trim);
                m.Box(cx - 1.3f, cx + 1.3f, top + 0.3f, top + 0.55f, 0, 0.38f, Trim);
                if (middle) m.SegmentPediment(cx - 1.3f, cx + 1.3f, top + 0.55f, 0.55f, 0, 0.32f, 12, Trim);
                else m.Pediment(cx - 1.3f, cx + 1.3f, top + 0.55f, 0.65f, 0, 0.32f, Trim);

                // Etajul 2: fereastră cu „urechi” și pervaz
                float w2 = 1.6f, sill2 = 15.3f, top2 = 17.9f;
                m.Box(cx - w2 / 2, cx + w2 / 2, sill2, top2, 0, 0.03f, Glass);
                Mullions(m, cx, w2, sill2, top2, 0.03f);
                Frame(m, cx, w2, sill2, top2, 0.18f, 0, 0.15f);
                m.Box(cx - 1.15f, cx + 1.15f, top2 + 0.15f, top2 + 0.42f, 0, 0.2f, Trim);
                m.Box(cx - 1.05f, cx + 1.05f, sill2 - 0.32f, sill2 - 0.12f, 0, 0.24f, Trim);
                // Panou cu ghirlandă sub fereastră
                m.Box(cx - 0.75f, cx + 0.75f, FloorBand + 0.75f, sill2 - 0.45f, 0, 0.05f, Trim);
            }
        }

        // ---------------------------------------------------------------- risalitul central

        static void Risalit(FacadeMesh m)
        {
            float zr = RisalitDepth;
            float pavilion = (RisalitHalf + InnerHalf) / 2; // mijlocul pavilioanelor laterale ale risalitului
            float bay = InnerHalf * 2 / 3f;

            // Parter: trei uși în arc
            for (int i = -1; i <= 1; i++)
                ArchedOpening(m, i * bay, Plinth, 2.2f, 3.7f, zr, 0.3f, 1.42f);

            // Balconul peste intrare, pe console, cu balustradă
            m.Box(-InnerHalf - 0.3f, InnerHalf + 0.3f, BandTop, BandTop + 0.35f, zr, zr + 1.1f, Trim);
            for (float x = -InnerHalf + 0.2f; x <= InnerHalf - 0.2f; x += 1.2f)
                m.Box(x - 0.15f, x + 0.15f, GroundTop - 0.5f, BandTop, zr, zr + 0.9f, Trim);
            Balustrade(m, -InnerHalf - 0.3f, InnerHalf + 0.3f, BandTop + 0.35f, BandTop + 1.35f, zr + 0.8f, zr + 1.1f);

            // Loggia: trei arce pe patru coloane
            float floor = BandTop + 0.35f;
            float colTop = 13.6f, spring = 14.7f;
            float colR = 0.36f, colZ = zr + 0.5f;
            foreach (float x in new[] { -InnerHalf, -InnerHalf + bay, InnerHalf - bay, InnerHalf })
            {
                m.Box(x - 0.5f, x + 0.5f, floor, floor + 0.5f, zr, zr + 1.0f, Trim);          // soclu
                m.Frustum(x, colZ, floor + 0.5f, floor + 0.75f, colR + 0.12f, colR, 16, Trim);  // bază
                m.Cylinder(x, colZ, floor + 0.75f, colTop, colR, 16, Trim);                     // fus
                m.Frustum(x, colZ, colTop, colTop + 0.35f, colR, colR + 0.22f, 16, Trim);       // capitel
                m.Box(x - 0.6f, x + 0.6f, colTop + 0.35f, spring, zr, zr + 1.05f, Trim);        // abac / impostă
                m.Disc(x, 16.9f, 0.42f, zr, zr + 0.12f, 14, Sculpture);                         // rozetă în timpan
            }
            for (int i = -1; i <= 1; i++)
            {
                float cx = i * bay;
                float open = bay - 1.2f, r = open / 2;
                // Golul loggiei (umbră), arhivolta și cheia de boltă cu mascaron
                m.ArchPanel(cx, floor, open, spring, zr, zr + 0.015f, 16, Shadow);
                m.ArchRing(cx, spring, r, r + 0.35f, zr, zr + 0.35f, 14, Trim);
                m.Box(cx - 0.25f, cx + 0.25f, spring + r - 0.1f, spring + r + 0.6f, zr, zr + 0.45f, Trim);
                m.Disc(cx, spring + r + 0.25f, 0.2f, zr + 0.45f, zr + 0.55f, 10, Sculpture);
                // Fereastra din fundul loggiei, cu fronton
                float w = 1.6f, sill = floor + 0.6f, top = 11.4f;
                m.Box(cx - w / 2, cx + w / 2, sill, top, zr + 0.015f, zr + 0.045f, Glass);
                Mullions(m, cx, w, sill, top, zr + 0.045f);
                Frame(m, cx, w, sill, top, 0.22f, zr + 0.015f, zr + 0.2f);
                m.Box(cx - 1.15f, cx + 1.15f, top + 0.2f, top + 0.42f, zr + 0.015f, zr + 0.3f, Trim);
                m.Pediment(cx - 1.15f, cx + 1.15f, top + 0.42f, 0.6f, zr + 0.015f, zr + 0.25f, Trim);
            }

            // Brâu și atic cu medalioane și ghirlande
            m.Box(-RisalitHalf, RisalitHalf, 17.6f, 18.2f, zr, zr + 0.25f, Trim);
            float[] medallions = { -4.05f, -1.35f, 1.35f, 4.05f };
            foreach (float x in medallions)
            {
                m.Disc(x, 19.9f, 0.95f, zr, zr + 0.12f, 24, Trim);
                m.Disc(x, 19.9f, 0.72f, zr + 0.12f, zr + 0.24f, 24, Sculpture);
            }
            for (int i = 0; i < medallions.Length - 1; i++)
            {
                float x = (medallions[i] + medallions[i + 1]) / 2;
                Garland(m, x, 20.3f, zr);
            }

            // Pavilioanele risalitului: nișă cu statuie, panou, medalion în chenar, fronton curb
            foreach (float cx in new[] { -pavilion, pavilion })
            {
                m.Box(cx - 0.65f, cx + 0.65f, floor - 0.1f, floor + 0.35f, zr, zr + 0.5f, Trim);       // consola nișei
                m.ArchPanel(cx, floor + 0.35f, 1.3f, 11.9f, zr, zr + 0.015f, 14, Shadow);
                m.ArchRing(cx, 11.9f, 0.65f, 0.88f, zr, zr + 0.2f, 12, Trim);
                m.Box(cx - 0.88f, cx - 0.65f, floor + 0.35f, 11.9f, zr, zr + 0.2f, Trim);
                m.Box(cx + 0.65f, cx + 0.88f, floor + 0.35f, 11.9f, zr, zr + 0.2f, Trim);
                m.Statue(cx, floor + 0.35f, zr + 0.3f, 2.9f, Sculpture);

                m.Box(cx - 0.95f, cx + 0.95f, 14.4f, 16.6f, zr, zr + 0.1f, Trim);
                m.Box(cx - 0.75f, cx + 0.75f, 14.6f, 16.4f, zr + 0.1f, zr + 0.13f, Stone);

                m.Box(cx - 1.0f, cx + 1.0f, 18.9f, 20.9f, zr, zr + 0.12f, Trim);
                m.Disc(cx, 19.9f, 0.62f, zr + 0.12f, zr + 0.24f, 20, Sculpture);

                float y = RisalitTop + 1.3f;
                m.Box(cx - 1.35f, cx + 1.35f, y, y + 0.6f, zr - 0.6f, zr + 0.35f, Stone);
                m.SegmentPediment(cx - 1.45f, cx + 1.45f, y + 0.6f, 1.2f, zr - 0.6f, zr + 0.4f, 16, Trim);
                m.SegmentPediment(cx - 1.1f, cx + 1.1f, y + 0.6f, 0.85f, zr + 0.4f, zr + 0.46f, 14, Sculpture);
            }
            // Aticul dintre pavilioane
            m.Box(-pavilion + 1.35f, pavilion - 1.35f, RisalitTop + 1.3f, RisalitTop + 1.8f, zr - 1.2f, zr + 0.2f, Stone);
        }

        /// <summary>O ghirlandă simplificată: două discuri mici și o bandă, în relief.</summary>
        static void Garland(FacadeMesh m, float cx, float cy, float z)
        {
            m.Box(cx - 0.55f, cx + 0.55f, cy - 0.08f, cy + 0.08f, z, z + 0.1f, Sculpture);
            m.Disc(cx - 0.3f, cy - 0.3f, 0.22f, z, z + 0.14f, 10, Sculpture);
            m.Disc(cx + 0.3f, cy - 0.3f, 0.22f, z, z + 0.14f, 10, Sculpture);
            m.Disc(cx, cy - 0.5f, 0.26f, z, z + 0.16f, 10, Sculpture);
        }

        // ---------------------------------------------------------------- acoperișul

        static void Roofs(FacadeMesh m)
        {
            float zr = RisalitDepth;
            // Aripile: acoperiș jos, abia vizibil în spatele aticului
            m.HipRoof(-HalfWidth + 0.3f, -RisalitHalf, -10f, -0.3f, SideTop, SideTop + 2.2f, 2.0f, Roof);
            m.HipRoof(RisalitHalf, HalfWidth - 0.3f, -10f, -0.3f, SideTop, SideTop + 2.2f, 2.0f, Roof);
            // Risalitul: acoperiș înalt de cupru, cu cupolă joasă și un mic felinar
            float y0 = RisalitTop + 1.3f;
            m.HipRoof(-RisalitHalf + 0.2f, RisalitHalf - 0.2f, -9f, zr - 1.3f, y0, y0 + 3.2f, 3.0f, Roof);
            var top = new Vector3(0, y0 + 3.2f, -3.6f);
            m.Dome(top, 3.4f, 1.6f, 2.6f, 24, 6, Roof);
            m.Frustum(0, -3.6f, top.y + 1.5f, top.y + 2.4f, 0.35f, 0.3f, 10, Roof);
            m.Dome(new Vector3(0, top.y + 2.4f, -3.6f), 0.42f, 0.35f, 0.42f, 10, 3, Roof);
            m.Frustum(0, -3.6f, top.y + 2.7f, top.y + 3.6f, 0.05f, 0.02f, 6, Trim);
        }

        // ---------------------------------------------------------------- piese repetate

        /// <summary>Un gol în arc (fereastră sau ușă) cu ancadrament, arhivoltă, cheie de boltă și pervaz.</summary>
        static void ArchedOpening(FacadeMesh m, float cx, float sill, float width, float spring, float z, float depth, float outer)
        {
            float r = width / 2;
            m.ArchPanel(cx, sill, width, spring, z, z + 0.03f, 14, Glass);
            ArchMullions(m, cx, width, sill, spring, z + 0.03f);
            m.Box(cx - outer, cx - r, sill - 0.1f, spring, z, z + depth * 0.7f, Trim);
            m.Box(cx + r, cx + outer, sill - 0.1f, spring, z, z + depth * 0.7f, Trim);
            m.ArchRing(cx, spring, r, outer, z, z + depth * 0.7f, 14, Trim);
            m.Box(cx - 0.2f, cx + 0.2f, spring + r - 0.05f, spring + outer + 0.15f, z, z + depth, Trim);
            if (sill > Plinth + 0.1f) m.Box(cx - outer - 0.05f, cx + outer + 0.05f, sill - 0.25f, sill - 0.05f, z, z + depth, Trim);
        }

        /// <summary>Tâmplăria unei ferestre dreptunghiulare: montant la mijloc și traversă la două treimi.</summary>
        static void Mullions(FacadeMesh m, float cx, float w, float sill, float top, float z)
        {
            m.Box(cx - 0.05f, cx + 0.05f, sill, top, z, z + 0.06f, Trim);
            float t = sill + (top - sill) * 0.68f;
            m.Box(cx - w / 2, cx + w / 2, t - 0.05f, t + 0.05f, z, z + 0.06f, Trim);
        }

        /// <summary>Tâmplăria unui gol în arc: montant și o traversă la naștere.</summary>
        static void ArchMullions(FacadeMesh m, float cx, float w, float sill, float spring, float z)
        {
            m.Box(cx - 0.05f, cx + 0.05f, sill, spring + w / 2 - 0.05f, z, z + 0.06f, Trim);
            m.Box(cx - w / 2, cx + w / 2, spring - 0.05f, spring + 0.05f, z, z + 0.06f, Trim);
        }

        /// <summary>Ancadrament dreptunghiular în jurul unei ferestre (stâlpi și buiandrug).</summary>
        static void Frame(FacadeMesh m, float cx, float w, float sill, float top, float thick, float z0, float z1)
        {
            m.Box(cx - w / 2 - thick, cx - w / 2, sill - thick, top + thick, z0, z1, Trim);
            m.Box(cx + w / 2, cx + w / 2 + thick, sill - thick, top + thick, z0, z1, Trim);
            m.Box(cx - w / 2, cx + w / 2, top, top + thick, z0, z1, Trim);
            m.Box(cx - w / 2, cx + w / 2, sill - thick, sill, z0, z1, Trim);
        }

        /// <summary>Balustradă: soclu, baluștri strunjiți (două trunchiuri de con) și mână curentă.</summary>
        static void Balustrade(FacadeMesh m, float x0, float x1, float y0, float y1, float z0, float z1)
        {
            float zc = (z0 + z1) / 2, r = (z1 - z0) * 0.32f;
            m.Box(x0, x1, y0, y0 + 0.12f, z0, z1, Trim);
            m.Box(x0 - 0.03f, x1 + 0.03f, y1 - 0.14f, y1, z0 - 0.03f, z1 + 0.03f, Trim);
            int n = Mathf.Max(2, Mathf.RoundToInt((x1 - x0) / 0.3f));
            float mid = y0 + 0.12f + (y1 - y0 - 0.26f) * 0.4f;
            for (int i = 0; i < n; i++)
            {
                float x = x0 + (i + 0.5f) * (x1 - x0) / n;
                m.Frustum(x, zc, y0 + 0.12f, mid, r * 0.55f, r, 6, Trim);
                m.Frustum(x, zc, mid, y1 - 0.14f, r, r * 0.5f, 6, Trim);
            }
        }
    }
}
