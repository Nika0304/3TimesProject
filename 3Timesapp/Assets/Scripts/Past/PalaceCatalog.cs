using UnityEngine;

namespace ThreeTimes.Past
{
    public enum PalaceRoof { Hipped, Mansard }

    /// <summary>Un palat din Piața Victoriei, așa cum îl îmbrăcăm „în epocă” peste clădirea de azi.</summary>
    public class PalaceSpec
    {
        public string Name;
        /// <summary>Anul în care a fost terminat: atunci apare în Trecut.</summary>
        public int Year;
        /// <summary>Notița (carte poștală) care apare când îl privești.</summary>
        public string Note;
        /// <summary>Marginea clădirii spre piață și străzi (lat, lon, lat, lon, …), din OpenStreetMap.</summary>
        public double[] Facade;
        /// <summary>Un punct din interiorul clădirii (ca să știm care parte e „afară”).</summary>
        public double InsideLat, InsideLon;
        /// <summary>Înălțimea până la coama acoperișului și până la cornișă (m).</summary>
        public float Height = 25f, Cornice = 19.5f;
        /// <summary>Etajele de deasupra parterului.</summary>
        public int Floors = 4;
        public PalaceRoof Roof = PalaceRoof.Hipped;
        public Color Wall = new Color(0.90f, 0.85f, 0.75f);
        public Color Trim = new Color(0.95f, 0.92f, 0.85f);
        public Color RoofColor = new Color(0.36f, 0.34f, 0.33f);
        public Color Ornament = new Color(0.85f, 0.80f, 0.70f);
        public float BayWidth = 4.2f;
        /// <summary>Ferestre pe travee (1 sau 3: Hilt-Vogel are motive câte trei).</summary>
        public int WindowsPerBay = 1;
        /// <summary>Un bovindou la fiecare atâtea travee (0: fără).</summary>
        public int BayWindowEvery;
        /// <summary>Un fronton curb (Secession) la fiecare atâtea travee, pe latura cea mai lungă (0: fără).</summary>
        public int GableEvery;
        /// <summary>Fronton triunghiular pe latura cu acest număr (−1: fără).</summary>
        public int PedimentOnSegment = -1;
        /// <summary>Turn cu cupolă la colțul cu acest număr din contur (−1: fără).</summary>
        public int DomeAtVertex = -1;
        /// <summary>Obloane de lemn, deschise, la ferestrele etajelor.</summary>
        public bool Shutters;
        /// <summary>Firmele de la parter (indici în PalaceCatalog.Signs), puse pe rând pe vitrine.</summary>
        public int[] Signs = new int[0];
        /// <summary>Ce parte din vitrine au copertină (0..1).</summary>
        public float AwningShare = 0.6f;
    }

    /// <summary>
    /// Palatele din jurul Pieței Victoriei (Corso și Surogat), cu anii lor. Marginile vin din OpenStreetMap (ODbL,
    /// © OpenStreetMap contributors); anii și notițele din surse (Wikipedia, Heritage of Timișoara, Grande Flânerie).
    /// Firmele de la parter sunt ilustrative: tipuri de prăvălii obișnuite în Temesvár în jurul lui 1912.
    /// </summary>
    public static class PalaceCatalog
    {
        /// <summary>Textele de pe firme, în ordinea rândurilor din textura Assets/Textures/Past/PalaceSigns.png.</summary>
        public static readonly string[] Signs =
        {
            "KÁVÉHÁZ", "ÉTTEREM", "TEMESVÁRI LLOYD", "DIVATÁRU",
            "ÓRÁS ÉS ÉKSZERÉSZ", "KÖNYVKERESKEDÉS", "GYÓGYSZERTÁR", "CUKRÁSZDA",
            "KONDITOREI", "BUCHHANDLUNG", "DELIKATESSEN", "VASKERESKEDÉS",
            "FŰSZERÜZLET", "KALAPOS", "FÉNYKÉPÉSZ", "TAKARÉKPÉNZTÁR",
        };
        public const int SignRows = 16;

        public static readonly PalaceSpec[] Palaces =
        {
            new PalaceSpec
            {
                Name = "Palatul Lloyd", Year = 1912,
                Note = "Palatul Lloyd, terminat în 1912: prima clădire ridicată în piață după Teatru. " +
                       "Arhitect Lipót Baumhorn, constructor Arnold Merbl.",
                Facade = new[] { 45.7537651, 21.2249597, 45.7537763, 21.2250244, 45.75374, 21.2251455, 45.7536698, 21.2252461,
                                 45.75359, 21.2252842, 45.7535314, 21.2252619, 45.7533035, 21.2251496 },
                InsideLat = 45.75355, InsideLon = 21.22495,
                Height = 25f, Cornice = 20f, Floors = 4, Roof = PalaceRoof.Mansard,
                Wall = new Color(0.91f, 0.86f, 0.75f), DomeAtVertex = 2, BayWindowEvery = 4,
                Signs = new[] { 2, 3, 10 },
            },
            new PalaceSpec
            {
                Name = "Palatul Neuhausz", Year = 1912,
                Note = "Palatul Neuhausz, 1910–1912: ridicat de întreprinzătorul Ernő Neuhausz, după planurile lui " +
                       "László Székely, arhitectul-șef al orașului.",
                Facade = new[] { 45.7533035, 21.2251496, 45.7531498, 21.2250739 },
                InsideLat = 45.7532, InsideLon = 21.22485,
                Height = 26f, Cornice = 18f, Floors = 3,
                Wall = new Color(0.93f, 0.88f, 0.79f), GableEvery = 4, BayWindowEvery = 4,
                Signs = new[] { 4, 13 },
            },
            new PalaceSpec
            {
                Name = "Palatul Merbl", Year = 1912,
                Note = "Palatul Merbl, 1911–1912: casa lui Arnold Merbl, constructorul palatelor Lloyd și Weiss.",
                Facade = new[] { 45.7531034, 21.2245023, 45.7529735, 21.2249871, 45.7531498, 21.2250739 },
                InsideLat = 45.7531, InsideLon = 21.2248,
                Height = 24f, Cornice = 18.5f, Floors = 3,
                Wall = new Color(0.87f, 0.82f, 0.71f), BayWindowEvery = 3, Shutters = true,
                Signs = new[] { 5, 9 },
            },
            new PalaceSpec
            {
                Name = "Palatul Dauerbach", Year = 1913,
                Note = "Palatul Dauerbach, 1911–1913, de László Székely: sobru, cu ornamente gri-închis și plăci " +
                       "smălțuite albastru-cobalt.",
                Facade = new[] { 45.7529385, 21.2245378, 45.7528361, 21.2249195, 45.7526564, 21.2248314,
                                 45.7526525, 21.2248463, 45.7522489, 21.2246477 },
                InsideLat = 45.75258, InsideLon = 21.22457,
                Height = 25f, Cornice = 19.5f, Floors = 4,
                Wall = new Color(0.84f, 0.83f, 0.80f), Trim = new Color(0.45f, 0.45f, 0.46f),
                Ornament = new Color(0.20f, 0.32f, 0.62f), GableEvery = 5,
                Signs = new[] { 6, 7, 8 },
            },
            new PalaceSpec
            {
                Name = "Palatul Hilt-Vogel", Year = 1913,
                Note = "Palatul Hilt-Vogel, 1911–1913, tot de László Székely: motive geometrice, adesea grupate câte trei.",
                Facade = new[] { 45.7521948, 21.2243072, 45.7521214, 21.2245854, 45.7518503, 21.2244582 },
                InsideLat = 45.7520, InsideLon = 21.2243,
                Height = 25f, Cornice = 19.5f, Floors = 4,
                Wall = new Color(0.90f, 0.85f, 0.74f), WindowsPerBay = 3,
                Signs = new[] { 11, 10 },
            },
            new PalaceSpec
            {
                Name = "Palatul Széchényi", Year = 1914,
                Note = "Palatul Széchényi, 1911–1914, de László Székely: parterul a fost gândit de la început pentru un local.",
                Facade = new[] { 45.751857, 21.2244343, 45.7517327, 21.2243775, 45.7515358, 21.2240093, 45.751658, 21.2237181 },
                InsideLat = 45.75172, InsideLon = 21.22405,
                Height = 25f, Cornice = 19.5f, Floors = 4,
                Wall = new Color(0.90f, 0.87f, 0.78f), BayWindowEvery = 5, GableEvery = 6,
                Signs = new[] { 1, 12 },
            },
            new PalaceSpec
            {
                Name = "Palatul Löffler", Year = 1913,
                Note = "Palatul Löffler, 1912–1913: casa lui Jacob Löffler și a celor trei fii ai lui, de aceea are " +
                       "trei intrări. La parter s-a deschis o cafenea.",
                Facade = new[] { 45.753401, 21.2263703, 45.753499, 21.2260067, 45.7530439, 21.2257825, 45.7527915, 21.2256614 },
                InsideLat = 45.7531, InsideLon = 21.2260,
                Height = 25f, Cornice = 19f, Floors = 4, Roof = PalaceRoof.Mansard,
                Wall = new Color(0.93f, 0.84f, 0.62f), PedimentOnSegment = 0, BayWindowEvery = 5,
                Signs = new[] { 0, 14, 15 },
            },
            new PalaceSpec
            {
                Name = "Palatul Weiss", Year = 1912,
                Note = "Palatul Weiss, 1912: construit de firma lui Arnold Merbl după planurile lui Adalbert Szladek, " +
                       "cu 27 de apartamente și magazine la parter.",
                Facade = new[] { 45.7538767, 21.2247537, 45.7539498, 21.2250544, 45.7540416, 21.2251195, 45.7543104, 21.2250015 },
                InsideLat = 45.7541, InsideLon = 21.22487,
                Height = 26f, Cornice = 21f, Floors = 4,
                Wall = new Color(0.91f, 0.85f, 0.77f), PedimentOnSegment = 1, Shutters = true,
                Signs = new[] { 12, 3 },
            },
        };

        /// <summary>Locuri din piață unde, în anii Trecutului, nu era încă nimic (notiță când le privești).</summary>
        public struct EmptySpot
        {
            public double Lat, Lon;
            public int UntilYear;
            public string Note;
        }

        public static readonly EmptySpot[] Empty =
        {
            new EmptySpot
            {
                Lat = 45.75205, Lon = 21.22560, UntilYear = 1918,
                Note = "Pe această parte a pieței, înainte de Primul Război Mondial era doar Palatul Löffler. " +
                       "Celelalte clădiri au apărut după război.",
            },
        };

        // ---------------------------------------------------------------- coordonate

        /// <summary>Metri pe grad de latitudine / longitudine la latitudinea dată (elipsoidul WGS84, aproximare).</summary>
        public static Vector2 MetersPerDegree(double lat)
        {
            double phi = lat * System.Math.PI / 180.0;
            double mLat = 111132.92 - 559.82 * System.Math.Cos(2 * phi) + 1.175 * System.Math.Cos(4 * phi);
            double mLon = 111412.84 * System.Math.Cos(phi) - 93.5 * System.Math.Cos(3 * phi);
            return new Vector2((float)mLat, (float)mLon);
        }

        /// <summary>Punctul (lat, lon) în metri față de origine: x spre est, z spre nord (cadrul ancorelor ARCore).</summary>
        public static Vector3 ToLocal(double lat, double lon, double lat0, double lon0)
        {
            var m = MetersPerDegree(lat0);
            return new Vector3((float)((lon - lon0) * m.y), 0f, (float)((lat - lat0) * m.x));
        }

        /// <summary>Originea unui palat: media punctelor de pe margine.</summary>
        public static void Origin(PalaceSpec p, out double lat, out double lon)
        {
            lat = 0; lon = 0;
            int n = p.Facade.Length / 2;
            for (int i = 0; i < n; i++) { lat += p.Facade[2 * i]; lon += p.Facade[2 * i + 1]; }
            lat /= n; lon /= n;
        }
    }
}
