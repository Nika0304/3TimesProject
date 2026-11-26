using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using ThreeTimes.AR;
using ThreeTimes.Core;
using ThreeTimes.Data;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Harta urmelor din orașul tău (Timișoara sau zona din jurul tău): fundal CARTO Positron (bucăți de 256 px, proiecția Web Mercator), urmele din jur ca forme
    /// mici (cu polaroid / microfon după tip) și poziția ta. Se trage cu degetul, se mărește din butoane sau
    /// cu dublă atingere. Atingerea unei urme arată o previzualizare: emoția, tipul și cât de departe e.
    /// Conținutul altora se deschide doar de aproape, prin cameră; urmele tale se pot deschide de aici.
    /// Fila „Harta mea” (doar cu cont) arată doar urmele contului, oriunde ar fi, nu doar pe cele din jur.
    /// </summary>
    public class MapScreenView
    {
        const int TileSize = 256;          // în unități de interfață; bucățile vin la 512 px (@2x), deci clare
        // Zoom 12–13 = tot orașul, 17 = câteva străzi. CARTO are imagini până la 18; la 19 le mărim pe cele de 18
        // (mai mult de atât se văd prea neclar).
        const int MinZoom = 12, MaxZoom = 19, MaxDataZoom = 18, StartZoom = 13, LocateZoom = 17;
        const int MaxCachedTiles = 60, MaxParallelDownloads = 6;
        const float MarkerSize = 30f, TapRadius = 24f, DragThreshold = 6f;
        // Harta arată orașul în care ești: Timișoara (centrată pe Piața Victoriei) sau, dacă ești în altă parte,
        // o zonă de vreo 12 × 12 km în jurul tău. Nu se poate trage în afara zonei.
        const double DefaultLat = 45.75270, DefaultLng = 21.22530;
        const double MinLat = 45.70, MaxLat = 45.81, MinLng = 21.14, MaxLng = 21.33;
        const double OtherCityHalfKm = 6.0;

        readonly UIRouter router;
        readonly MonoBehaviour host;
        readonly GeospatialTracker tracker;
        readonly Action<double, double, float, Action<List<SavedTrace>>> loadTraces;
        readonly Action<SavedTrace> openTrace;
        readonly Action<Action<List<SavedTrace>, string>> loadMine;
        readonly MapConfig config;
        readonly PhoneLocation phone = new PhoneLocation();

        readonly VisualElement screen, viewport, tileLayer, markerLayer, me, card, cardShapeHolder;
        readonly Label status, cardTitle, cardKind, cardDistance, cardHint;
        readonly Button cardOpen, tabAll, tabMine, cardPrev, cardNext;
        readonly VisualElement cardNav, groupPanel, groupGrid;
        readonly Label cardIndexLabel, groupTitle;

        double centerLat = DefaultLat, centerLng = DefaultLng;
        int zoom = StartZoom;
        bool followMe;
        bool tileErrorShown;
        bool userMoved;     // utilizatorul a tras sau a mărit harta de la deschidere

        // Zona hărții (orașul): Timișoara sau zona din jurul tău, dacă ești în altă parte.
        double areaMinLat = MinLat, areaMaxLat = MaxLat, areaMinLng = MinLng, areaMaxLng = MaxLng;
        bool areaIsTimisoara = true;

        // Bucățile de hartă: elementele afișate, texturile descărcate (cele mai vechi se aruncă primele), coada de descărcare.
        readonly Dictionary<string, VisualElement> tileElements = new Dictionary<string, VisualElement>();
        readonly Dictionary<string, Texture2D> tileCache = new Dictionary<string, Texture2D>();
        readonly LinkedList<string> tileOrder = new LinkedList<string>();
        readonly HashSet<string> tileLoading = new HashSet<string>();
        readonly Queue<string> tileQueue = new Queue<string>();
        readonly HashSet<string> tilesNeeded = new HashSet<string>();
        int downloads;

        // Urmele
        // Urmele apropiate pe ecran (sub ClusterRadius) se strâng într-un singur semn cu numărul lor.
        class Marker
        {
            public SavedTrace Trace;
            public VisualElement Element;
            public Label Count;
            public float X, Y;
            public bool OnScreen;
            public List<SavedTrace> Group; // doar la semnul care conduce grupul (null la cele ascunse în grup)
        }
        const float ClusterRadius = 26f;
        readonly List<Marker> markers = new List<Marker>();
        SavedTrace selected;
        List<SavedTrace> cardGroup;
        int cardIndex;
        bool fetching;
        bool fetchedOnce;
        double fetchLat, fetchLng;
        float fetchRadius, lastFetchTime = -10f;

        // „Harta mea”: doar urmele contului; zona hărții se lărgește cât să le cuprindă
        bool mineMode;
        bool hasMineBounds;
        double mineMinLat, mineMaxLat, mineMinLng, mineMaxLng;

        // Poziția ta
        bool hasMe;
        double meLat, meLng;

        // Atingeri
        bool pointerDown, dragging;
        Vector2 pointerStart, pointerLast;
        int pointerId = -1;
        float lastTapTime = -1f;

        IVisualElementScheduledItem tick;

        public bool IsOpen { get; private set; }

        public MapScreenView(VisualElement root, UIRouter router, MonoBehaviour host, GeospatialTracker tracker,
                             Action<double, double, float, Action<List<SavedTrace>>> loadTraces,
                             Action<SavedTrace> openTrace,
                             Action<Action<List<SavedTrace>, string>> loadMine)
        {
            this.router = router;
            this.host = host;
            this.tracker = tracker;
            this.loadTraces = loadTraces;
            this.openTrace = openTrace;
            this.loadMine = loadMine;
            config = MapConfig.Load();

            screen = root.Q("screen-map");
            viewport = root.Q("map-view");
            tileLayer = root.Q("map-tiles");
            markerLayer = root.Q("map-markers");
            me = root.Q("map-me");
            status = root.Q<Label>("map-status");
            card = root.Q("map-card");
            cardShapeHolder = root.Q("map-card-shape");
            cardTitle = root.Q<Label>("map-card-title");
            cardKind = root.Q<Label>("map-card-kind");
            cardDistance = root.Q<Label>("map-card-distance");
            cardHint = root.Q<Label>("map-card-hint");
            cardOpen = root.Q<Button>("map-card-open");
            cardNav = root.Q("map-card-nav");
            cardPrev = root.Q<Button>("map-card-prev");
            cardNext = root.Q<Button>("map-card-next");
            cardIndexLabel = root.Q<Label>("map-card-index");
            groupPanel = root.Q("map-group");
            groupGrid = root.Q("map-group-grid");
            groupTitle = root.Q<Label>("map-group-title");
            root.Q<Button>("map-group-close").clicked += HideGroupPanel;
            cardPrev.clicked += () => StepCard(-1);
            cardNext.clicked += () => StepCard(1);
            tabAll = root.Q<Button>("map-tab-all");
            tabMine = root.Q<Button>("map-tab-mine");
            tabAll.clicked += () => SetMineMode(false);
            tabMine.clicked += () =>
            {
                if (!AccountSession.IsSignedIn)
                {
                    router.Toast("„Harta mea” arată unde ai lăsat urme și e doar pentru cei cu cont. Intră în cont din Meniu → Cont.", 5f);
                    return;
                }
                if (!ServerSettings.IsConfigured)
                {
                    router.Toast("„Harta mea” are nevoie de server: pune adresa lui în Meniu, la „Server”.", 4f);
                    return;
                }
                SetMineMode(true);
            };

            root.Q<Button>("map-back").clicked += Close;
            root.Q<Button>("map-zoom-in").clicked += () => SetZoom(zoom + 1);
            root.Q<Button>("map-zoom-out").clicked += () => SetZoom(zoom - 1);
            root.Q<Button>("map-locate").clicked += () =>
            {
                if (!hasMe)
                {
                    router.Toast("Încă nu știu unde ești. Verifică locația telefonului.");
                    return;
                }
                followMe = true;
                userMoved = true;
                if (zoom < LocateZoom) SetZoom(LocateZoom);
                Tick();
            };
            root.Q<Button>("map-card-close").clicked += HideCard;
            cardOpen.clicked += () =>
            {
                if (selected == null) return;
                var t = selected;
                HideCard();
                openTrace(t);
            };

            viewport.RegisterCallback<PointerDownEvent>(OnPointerDown);
            viewport.RegisterCallback<PointerMoveEvent>(OnPointerMove);
            viewport.RegisterCallback<PointerUpEvent>(OnPointerUp);
            viewport.RegisterCallback<PointerCaptureOutEvent>(_ => { pointerDown = false; dragging = false; });
            viewport.RegisterCallback<GeometryChangedEvent>(_ => Layout());
        }

        // ---------------------------------------------------------------- deschidere / închidere

        public void Open()
        {
            if (IsOpen) return;
            IsOpen = true;
            followMe = false;
            tileErrorShown = false;
            userMoved = false;
            HideCard();
            centerLat = DefaultLat;
            centerLng = DefaultLng;
            if (zoom != StartZoom) { zoom = StartZoom; ClearTileElements(); }
            if (hasMe && !areaIsTimisoara)
            {
                centerLat = meLat;
                centerLng = meLng;
            }
            if (mineMode)
            {
                markerLayer.Clear();
                markers.Clear();
            }
            mineMode = false;
            hasMineBounds = false;
            UpdateTabs();
            fetchedOnce = false; // la redeschidere, urmele din jur se cer din nou (pot fi altele)
            UIRouter.SetVisible(screen, true);
            phone.Start();
            status.text = config.HasKey ? "Caut poziția ta…" :
                "Lipsește cheia hărții. Vezi 3Timesapp/MapConfig.example.json.";
            UIRouter.SetVisible(status, true);
            tick = screen.schedule.Execute(Tick).Every(300);
            Tick();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;
            tick?.Pause();
            phone.Stop();
            HideCard();
            HideGroupPanel();
            UIRouter.SetVisible(screen, false);
        }

        /// <summary>Butonul „Înapoi” de pe Android: întâi închide previzualizarea, apoi harta.</summary>
        public void Back()
        {
            if (selected != null) HideCard();
            else if (IsVisible(groupPanel)) HideGroupPanel();
            else Close();
        }

        static bool IsVisible(VisualElement e) => e != null && !e.ClassListContains("hidden");

        // ---------------------------------------------------------------- actualizare

        void Tick()
        {
            if (!IsOpen) return;

            // Cu camera pornită, poziția din ARCore e mult mai precisă decât GPS-ul.
            if (tracker != null && tracker.Running && tracker.Fix.HasValue)
            {
                var f = tracker.Fix.Value;
                SetMe(f.Latitude, f.Longitude);
            }
            else if (phone.HasFix)
            {
                SetMe(phone.Latitude, phone.Longitude);
            }

            if (config.HasKey && !tileErrorShown)
            {
                if (hasMe) UIRouter.SetVisible(status, false);
                else if (phone.PermissionDenied) status.text = "Fără acces la locație, harta nu știe unde ești.";
            }

            if (hasMe && followMe && !dragging)
            {
                centerLat = meLat;
                centerLng = meLng;
            }
            Layout();
            MaybeFetch();
        }

        void SetMe(double lat, double lng)
        {
            hasMe = true;
            meLat = lat;
            meLng = lng;
            UpdateArea();
        }

        /// <summary>
        /// Alege zona hărții după unde ești. Zona se mută doar când ieși din ea, ca harta să nu „alunece” cu tine.
        /// </summary>
        void UpdateArea()
        {
            bool inside = meLat >= areaMinLat && meLat <= areaMaxLat && meLng >= areaMinLng && meLng <= areaMaxLng;
            if (inside) return;

            if (InTimisoara(meLat, meLng))
            {
                areaMinLat = MinLat; areaMaxLat = MaxLat; areaMinLng = MinLng; areaMaxLng = MaxLng;
                areaIsTimisoara = true;
            }
            else
            {
                double halfLat = OtherCityHalfKm / 111.32;
                double halfLng = OtherCityHalfKm / (111.32 * Math.Cos(meLat * Math.PI / 180.0));
                areaMinLat = meLat - halfLat; areaMaxLat = meLat + halfLat;
                areaMinLng = meLng - halfLng; areaMaxLng = meLng + halfLng;
                areaIsTimisoara = false;
            }

            // Dacă încă nu ai umblat pe hartă, o mutăm pe noua zonă.
            if (!userMoved)
            {
                centerLat = areaIsTimisoara ? DefaultLat : meLat;
                centerLng = areaIsTimisoara ? DefaultLng : meLng;
            }
        }

        void SetZoom(int z)
        {
            int clamped = Mathf.Clamp(z, MinZoom, MaxZoom);
            if (clamped == zoom) return;
            zoom = clamped;
            userMoved = true;
            ClearTileElements();
            Layout();
        }

        static bool InTimisoara(double lat, double lng) =>
            lat >= MinLat && lat <= MaxLat && lng >= MinLng && lng <= MaxLng;

        /// <summary>Centrul hărții rămâne în zona orașului (la „Harta mea”, și în zona urmelor tale).</summary>
        void ClampCenter()
        {
            double minLat = areaMinLat, maxLat = areaMaxLat, minLng = areaMinLng, maxLng = areaMaxLng;
            if (mineMode && hasMineBounds)
            {
                minLat = Math.Min(minLat, mineMinLat);
                maxLat = Math.Max(maxLat, mineMaxLat);
                minLng = Math.Min(minLng, mineMinLng);
                maxLng = Math.Max(maxLng, mineMaxLng);
            }
            centerLat = Math.Max(minLat, Math.Min(maxLat, centerLat));
            centerLng = Math.Max(minLng, Math.Min(maxLng, centerLng));
        }

        // ---------------------------------------------------------------- Web Mercator

        static double WorldSize(int z) => TileSize * Math.Pow(2, z);

        static double LngToX(double lng, int z) => (lng + 180.0) / 360.0 * WorldSize(z);

        static double LatToY(double lat, int z)
        {
            double r = Math.Max(-85.05, Math.Min(85.05, lat)) * Math.PI / 180.0;
            return (1.0 - Math.Log(Math.Tan(r) + 1.0 / Math.Cos(r)) / Math.PI) / 2.0 * WorldSize(z);
        }

        static double XToLng(double x, int z) => x / WorldSize(z) * 360.0 - 180.0;

        static double YToLat(double y, int z)
        {
            double n = Math.PI - 2.0 * Math.PI * y / WorldSize(z);
            return 180.0 / Math.PI * Math.Atan(Math.Sinh(n));
        }

        /// <summary>Câți metri înseamnă o unitate de interfață, la latitudinea și mărirea curente.</summary>
        double MetersPerUnit => 156543.03392 * Math.Cos(centerLat * Math.PI / 180.0) / Math.Pow(2, zoom);

        // ---------------------------------------------------------------- desenare

        void Layout()
        {
            if (!IsOpen) return;
            float w = viewport.layout.width, h = viewport.layout.height;
            if (float.IsNaN(w) || w <= 0f || float.IsNaN(h) || h <= 0f) return;

            ClampCenter();
            double left = LngToX(centerLng, zoom) - w / 2.0;
            double top = LatToY(centerLat, zoom) - h / 2.0;

            LayoutTiles(left, top, w, h);

            LayoutMarkers(left, top, w, h);

            UIRouter.SetVisible(me, hasMe);
            if (hasMe) Place(me, LngToX(meLng, zoom) - left, LatToY(meLat, zoom) - top, w, h);
        }

        static void Place(VisualElement e, double x, double y, float w, float h)
        {
            bool inside = x > -60 && x < w + 60 && y > -60 && y < h + 60;
            e.style.display = inside ? DisplayStyle.Flex : DisplayStyle.None;
            if (!inside) return;
            e.style.left = (float)x;
            e.style.top = (float)y;
        }

        /// <summary>
        /// Așază urmele pe ecran și le grupează pe cele prea apropiate: primul semn dintr-un grup rămâne, cu numărul
        /// urmelor din grup; celelalte se ascund. Grupurile se refac la fiecare mutare sau mărire a hărții.
        /// </summary>
        void LayoutMarkers(double left, double top, float w, float h)
        {
            var leaders = new List<Marker>();
            float r2 = ClusterRadius * ClusterRadius;
            foreach (var m in markers)
            {
                m.X = (float)(LngToX(m.Trace.longitude, zoom) - left);
                m.Y = (float)(LatToY(m.Trace.latitude, zoom) - top);
                m.OnScreen = m.X > -60 && m.X < w + 60 && m.Y > -60 && m.Y < h + 60;
                m.Group = null;
                if (!m.OnScreen)
                {
                    m.Element.style.display = DisplayStyle.None;
                    continue;
                }
                Marker leader = null;
                foreach (var l in leaders)
                {
                    float dx = l.X - m.X, dy = l.Y - m.Y;
                    if (dx * dx + dy * dy < r2) { leader = l; break; }
                }
                if (leader != null)
                {
                    leader.Group.Add(m.Trace);
                    m.Element.style.display = DisplayStyle.None;
                    continue;
                }
                m.Group = new List<SavedTrace> { m.Trace };
                leaders.Add(m);
                m.Element.style.display = DisplayStyle.Flex;
                m.Element.style.left = m.X;
                m.Element.style.top = m.Y;
            }
            foreach (var l in leaders)
            {
                bool many = l.Group.Count > 1;
                l.Count.text = many ? l.Group.Count.ToString() : "";
                UIRouter.SetVisible(l.Count, many);
            }
        }

        void LayoutTiles(double left, double top, float w, float h)
        {
            if (!config.HasKey) return;
            // Peste zoom 18, CARTO nu mai are imagini: folosim pe cele de 18, mărite.
            int dataZoom = Math.Min(zoom, MaxDataZoom);
            double size = TileSize * Math.Pow(2, zoom - dataZoom);
            int n = 1 << dataZoom;
            int x0 = (int)Math.Floor(left / size), x1 = (int)Math.Floor((left + w) / size);
            int y0 = (int)Math.Floor(top / size), y1 = (int)Math.Floor((top + h) / size);

            tilesNeeded.Clear();
            var shown = new HashSet<string>();
            for (int ty = y0; ty <= y1; ty++)
            {
                if (ty < 0 || ty >= n) continue;
                for (int tx = x0; tx <= x1; tx++)
                {
                    int wx = ((tx % n) + n) % n;
                    string texKey = $"{dataZoom}/{wx}/{ty}";
                    string elemKey = $"{zoom}/{tx}/{ty}";
                    tilesNeeded.Add(texKey);
                    shown.Add(elemKey);

                    if (!tileElements.TryGetValue(elemKey, out var tile))
                    {
                        tile = new VisualElement { pickingMode = PickingMode.Ignore, userData = texKey };
                        tile.AddToClassList("map-tile");
                        tileElements[elemKey] = tile;
                        tileLayer.Add(tile);
                        if (tileCache.TryGetValue(texKey, out var cached)) Show(tile, texKey, cached);
                        else RequestTile(texKey);
                    }
                    // Puțin peste mărimea bucății, ca între bucăți să nu apară linii subțiri.
                    tile.style.left = (float)(tx * size - left);
                    tile.style.top = (float)(ty * size - top);
                    tile.style.width = (float)size + 0.6f;
                    tile.style.height = (float)size + 0.6f;
                }
            }

            List<string> gone = null;
            foreach (var key in tileElements.Keys)
                if (!shown.Contains(key)) (gone ??= new List<string>()).Add(key);
            if (gone != null)
                foreach (var key in gone)
                {
                    tileElements[key].RemoveFromHierarchy();
                    tileElements.Remove(key);
                }
        }

        void ClearTileElements()
        {
            foreach (var tile in tileElements.Values) tile.RemoveFromHierarchy();
            tileElements.Clear();
        }

        void Show(VisualElement tile, string key, Texture2D texture)
        {
            tile.style.backgroundImage = new StyleBackground(texture);
            // Cea mai recent folosită ajunge la final; cele de la început se aruncă primele.
            tileOrder.Remove(key);
            tileOrder.AddLast(key);
        }

        void RequestTile(string key)
        {
            if (tileLoading.Contains(key) || tileCache.ContainsKey(key)) return;
            tileLoading.Add(key);
            tileQueue.Enqueue(key);
            PumpTiles();
        }

        void PumpTiles()
        {
            while (downloads < MaxParallelDownloads && tileQueue.Count > 0)
            {
                string key = tileQueue.Dequeue();
                if (!tilesNeeded.Contains(key))
                {
                    tileLoading.Remove(key); // nu mai e pe ecran (s-a tras harta sau s-a schimbat mărirea)
                    continue;
                }
                downloads++;
                host.StartCoroutine(DownloadTile(key));
            }
        }

        IEnumerator DownloadTile(string key)
        {
            var parts = key.Split('/');
            string url = config.TileUrl(int.Parse(parts[0]), int.Parse(parts[1]), int.Parse(parts[2]));
            using (var request = UnityWebRequestTexture.GetTexture(url, true))
            {
                request.timeout = 15;
                yield return request.SendWebRequest();
                downloads--;
                tileLoading.Remove(key);

                if (request.result == UnityWebRequest.Result.Success)
                {
                    var texture = DownloadHandlerTexture.GetContent(request);
                    texture.wrapMode = TextureWrapMode.Clamp;
                    tileCache[key] = texture;
                    foreach (var tile in tileElements.Values)
                        if ((string)tile.userData == key) Show(tile, key, texture);
                    EvictTiles();
                }
                else
                {
                    Debug.LogWarning($"[3Times] Bucată de hartă nedescărcată ({key}): {request.responseCode} {request.error}");
                    // Prima eroare o arătăm și pe ecran, ca să se vadă fără Console (cheie greșită, fără internet...).
                    if (!tileErrorShown && IsOpen)
                    {
                        tileErrorShown = true;
                        status.text = request.responseCode == 401 || request.responseCode == 403
                            ? $"Harta nu se încarcă: cheia CARTO nu e acceptată ({request.responseCode})."
                            : $"Harta nu se încarcă: {request.error}";
                        UIRouter.SetVisible(status, true);
                    }
                }
            }
            PumpTiles();
        }

        void EvictTiles()
        {
            var node = tileOrder.First;
            while (tileCache.Count > MaxCachedTiles && node != null)
            {
                var next = node.Next;
                if (!tilesNeeded.Contains(node.Value))
                {
                    if (tileCache.TryGetValue(node.Value, out var texture)) UnityEngine.Object.Destroy(texture);
                    tileCache.Remove(node.Value);
                    tileOrder.Remove(node);
                }
                node = next;
            }
        }

        // ---------------------------------------------------------------- urmele

        /// <summary>Cere urmele din zona vizibilă când harta s-a mutat destul sau s-a micșorat.</summary>
        void MaybeFetch()
        {
            if (mineMode || fetching || dragging) return;
            float w = viewport.layout.width, h = viewport.layout.height;
            if (float.IsNaN(w) || w <= 0f) return;

            float radius = (float)Math.Min(15000.0, Math.Sqrt(w * w + h * h) / 2.0 * MetersPerUnit);
            float moved = TraceDistance(fetchLat, fetchLng, centerLat, centerLng);
            bool needed = !fetchedOnce || moved > fetchRadius * 0.4f || radius > fetchRadius * 1.3f;
            if (!needed || Time.realtimeSinceStartup - lastFetchTime < 1.5f) return;

            fetching = true;
            lastFetchTime = Time.realtimeSinceStartup;
            double lat = centerLat, lng = centerLng;
            loadTraces(lat, lng, radius, list =>
            {
                fetching = false;
                fetchedOnce = true;
                fetchLat = lat;
                fetchLng = lng;
                fetchRadius = radius;
                if (IsOpen && !mineMode) BuildMarkers(list);
            });
        }

        // ---------------------------------------------------------------- „Urme” / „Harta mea”

        void SetMineMode(bool on)
        {
            if (on == mineMode) return;
            mineMode = on;
            UpdateTabs();
            HideCard();
            HideGroupPanel();
            markerLayer.Clear();
            markers.Clear();
            if (on)
            {
                LoadMine();
            }
            else
            {
                hasMineBounds = false;
                fetchedOnce = false;
                fetching = false;
                MaybeFetch();
            }
        }

        void UpdateTabs()
        {
            tabAll.EnableInClassList("seg--active", !mineMode);
            tabMine.EnableInClassList("seg--active", mineMode);
        }

        void LoadMine()
        {
            loadMine((list, error) =>
            {
                if (!IsOpen || !mineMode) return;
                if (list == null)
                {
                    router.Toast(string.IsNullOrEmpty(error) ? "Nu am putut încărca urmele tale." : error, 4f);
                    if (!AccountSession.IsSignedIn) SetMineMode(false); // sesiunea a expirat
                    return;
                }
                BuildMarkers(list);
                if (list.Count == 0)
                {
                    router.Toast("Încă nu ai urme în cont. Cele pe care le lași de acum apar aici.", 4f);
                    return;
                }

                hasMineBounds = true;
                mineMinLat = mineMaxLat = list[0].latitude;
                mineMinLng = mineMaxLng = list[0].longitude;
                foreach (var t in list)
                {
                    mineMinLat = Math.Min(mineMinLat, t.latitude);
                    mineMaxLat = Math.Max(mineMaxLat, t.latitude);
                    mineMinLng = Math.Min(mineMinLng, t.longitude);
                    mineMaxLng = Math.Max(mineMaxLng, t.longitude);
                }
                // Dacă nicio urmă nu se vede acum, mutăm harta pe cea mai nouă (prima din listă).
                if (!AnyMarkerVisible())
                {
                    followMe = false;
                    centerLat = list[0].latitude;
                    centerLng = list[0].longitude;
                    Layout();
                }
                router.Toast(list.Count == 1 ? "O urmă lăsată de tine." :
                             list.Count < 20 ? $"{list.Count} urme lăsate de tine." : $"{list.Count} de urme lăsate de tine.", 2.5f);
            });
        }

        bool AnyMarkerVisible()
        {
            foreach (var m in markers)
                if (m.Group != null) return true;
            return false;
        }

        void BuildMarkers(List<SavedTrace> list)
        {
            markerLayer.Clear();
            markers.Clear();
            foreach (var t in list)
            {
                var marker = new VisualElement { pickingMode = PickingMode.Ignore };
                marker.AddToClassList("map-marker");
                var shape = new VisualElement { pickingMode = PickingMode.Ignore };
                shape.AddToClassList("map-marker__shape");
                if (t.IsCapsule)
                {
                    // Capsula: sfera de sticlă (aurie când se poate deschide); a anului, mai mare.
                    marker.AddToClassList("map-marker--capsule");
                    if (t.IsYearCapsule) marker.AddToClassList("map-marker--year");
                    shape.style.backgroundImage = new StyleBackground(CapsuleIcon.Get(CapsuleText.IsReady(t)));
                }
                else
                {
                    var style = TraceStyle.Of(t);
                    if (t.mine) marker.AddToClassList("map-marker--mine");
                    var backdrop = TraceTypeBackdrop.Create(t.type, style.Color, MarkerSize);
                    if (backdrop != null) marker.Add(backdrop);
                    shape.style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
                }
                marker.Add(shape);
                var count = new Label { pickingMode = PickingMode.Ignore };
                count.AddToClassList("map-marker__count");
                count.AddToClassList("hidden");
                marker.Add(count);

                markerLayer.Add(marker);
                markers.Add(new Marker { Trace = t, Element = marker, Count = count });
            }
            Layout();
        }

        /// <summary>Cardul pentru un grup de urme: arată urma de la poziția index, cu „‹ 1 din 4 ›” dacă sunt mai multe.</summary>
        void ShowGroup(List<SavedTrace> group, int index)
        {
            cardGroup = group;
            cardIndex = Mathf.Clamp(index, 0, group.Count - 1);
            ShowCard(group[cardIndex]);
            bool many = group.Count > 1;
            UIRouter.SetVisible(cardNav, many);
            if (many) cardIndexLabel.text = $"{cardIndex + 1} din {group.Count} urme aici";
        }

        void StepCard(int step)
        {
            if (cardGroup == null || cardGroup.Count < 2) return;
            ShowGroup(cardGroup, (cardIndex + step + cardGroup.Count) % cardGroup.Count);
        }

        void ShowCard(SavedTrace t)
        {
            selected = t;
            if (t.IsCapsule)
            {
                ShowCapsuleCard(t);
                return;
            }
            var style = TraceStyle.Of(t);

            cardShapeHolder.Clear();
            var backdrop = TraceTypeBackdrop.Create(t.type, style.Color, 52f);
            if (backdrop != null) cardShapeHolder.Add(backdrop);
            var shape = new VisualElement { pickingMode = PickingMode.Ignore };
            shape.AddToClassList("map-card__shape");
            shape.style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
            cardShapeHolder.Add(shape);

            cardTitle.text = style.Label;
            cardTitle.style.color = style.Ink;
            string kind = t.type == "PHOTO" ? "Urmă cu poză" : t.type == "VOICE" ? "Urmă cu voce" : "Urmă scrisă";
            cardKind.text = t.mine ? kind + " · a ta" : kind;

            if (hasMe)
            {
                float d = TraceDistance(meLat, meLng, t.latitude, t.longitude);
                cardDistance.text = d < 15f ? "Ești chiar lângă ea." : $"La {Round(d)} m, spre {Bearing(meLat, meLng, t.latitude, t.longitude)}.";
            }
            else
            {
                cardDistance.text = "";
            }
            cardHint.text = t.mine
                ? "E urma ta: o poți deschide de aici."
                : "Mergi până lângă ea și pornește camera ca s-o deschizi.";
            UIRouter.SetVisible(cardOpen, t.mine);
            UIRouter.SetVisible(card, true);
        }

        static string OpenedOn(SavedTrace c) =>
            RomaniaTime.TryParseUtc(c.openedAt, out var at) ? $" pe {RomaniaTime.DateText(RomaniaTime.FromUtc(at))}" : "";

        /// <summary>Cardul unei capsule pe hartă: ce fel e, câte urme are, când se deschide, cât de departe e.</summary>
        void ShowCapsuleCard(SavedTrace c)
        {
            cardShapeHolder.Clear();
            var shape = new VisualElement { pickingMode = PickingMode.Ignore };
            shape.AddToClassList("map-card__shape");
            shape.style.backgroundImage = new StyleBackground(CapsuleIcon.Get(CapsuleText.IsReady(c)));
            cardShapeHolder.Add(shape);

            bool personal = c.visibility == "PERSONAL";
            cardTitle.text = c.IsYearCapsule ? $"Capsula anului {c.year}"
                           : personal ? "Capsula ta personală" : c.mine ? "Capsula ta de grup" : "Capsulă de grup";
            cardTitle.style.color = new Color(0.118f, 0.478f, 0.392f); // verdele Viitorului (--future)
            string items = c.itemCount == 1 ? "o urmă" : $"{c.itemCount} urme";
            int left = TraceTime.CapsuleDaysLeft(c);
            bool expired = TraceTime.CapsuleExpired(c);
            cardKind.text = c.opened ? $"{items} · ai deschis-o{OpenedOn(c)}"
                          : expired ? $"{items} · n-a mai fost deschisă la timp"
                          : CapsuleText.IsReady(c)
                ? $"{items} · se poate deschide{(left > 0 ? $" încă {left} {(left == 1 ? "zi" : RomaniaTime.Days(left))}" : "")}"
                : $"{items} · se deschide {CapsuleText.When(c)}";

            if (hasMe)
            {
                float d = TraceDistance(meLat, meLng, c.latitude, c.longitude);
                cardDistance.text = d < 15f ? "Ești chiar lângă ea." : $"La {Round(d)} m, spre {Bearing(meLat, meLng, c.latitude, c.longitude)}.";
            }
            else
            {
                cardDistance.text = "";
            }
            cardHint.text = c.opened ? "Urmele din ea apar în Prezent o săptămână de la deschidere și încă o săptămână la un an."
                : expired ? "Au trecut cele două săptămâni de după dată: capsula nu se mai poate deschide."
                : CapsuleText.IsReady(c)
                ? "Mergi până lângă ea, pornește camera și treci în Viitor ca s-o deschizi."
                : c.collecting && (!personal || c.mine)
                    ? "Încă primește urme: mergi lângă ea, în Viitor, ca să-ți pui și tu una."
                    : "Până la data deschiderii, nimeni nu vede ce e înăuntru.";
            UIRouter.SetVisible(cardOpen, false);
            UIRouter.SetVisible(card, true);
        }

        /// <summary>La mărirea maximă, urmele grupului s-ar despărți pe ecran? (altfel n-are rost să mărim)</summary>
        bool SplitsAtMaxZoom(List<SavedTrace> group)
        {
            double unitsPerMeter = Math.Pow(2, MaxZoom) / (156543.03392 * Math.Cos(group[0].latitude * Math.PI / 180.0));
            for (int i = 0; i < group.Count; i++)
                for (int j = i + 1; j < group.Count; j++)
                {
                    float d = TraceDistance(group[i].latitude, group[i].longitude, group[j].latitude, group[j].longitude);
                    if (d * unitsPerMeter > ClusterRadius) return true;
                }
            return false;
        }

        /// <summary>Fereastra cu toate urmele unui grup, mari; apare cu un mic „pop”, iar urmele una după alta.</summary>
        void ShowGroupPanel(List<SavedTrace> group)
        {
            HideCard();
            groupTitle.text = group.Count < 20 ? $"{group.Count} urme aici" : $"{group.Count} de urme aici";
            groupGrid.Clear();
            for (int i = 0; i < group.Count; i++)
            {
                var t = group[i];
                int index = i;

                var item = new VisualElement();
                item.AddToClassList("map-group__item");
                item.AddToClassList("map-group__item--enter");
                var holder = new VisualElement { pickingMode = PickingMode.Ignore };
                holder.AddToClassList("map-group__holder");
                var shape = new VisualElement { pickingMode = PickingMode.Ignore };
                shape.AddToClassList("map-group__shape");
                Label label;
                if (t.IsCapsule)
                {
                    shape.style.backgroundImage = new StyleBackground(CapsuleIcon.Get(CapsuleText.IsReady(t)));
                    holder.Add(shape);
                    label = new Label(t.IsYearCapsule ? "capsula anului" : "capsulă") { pickingMode = PickingMode.Ignore };
                    label.style.color = new Color(0.118f, 0.478f, 0.392f);
                }
                else
                {
                    var style = TraceStyle.Of(t);
                    if (t.mine) holder.AddToClassList("map-group__holder--mine");
                    var backdrop = TraceTypeBackdrop.Create(t.type, style.Color, 64f);
                    if (backdrop != null) holder.Add(backdrop);
                    shape.style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
                    holder.Add(shape);
                    label = new Label(style.Label) { pickingMode = PickingMode.Ignore };
                    label.style.color = style.Ink;
                }
                item.Add(holder);
                label.AddToClassList("map-group__label");
                item.Add(label);
                item.RegisterCallback<ClickEvent>(_ =>
                {
                    HideGroupPanel();
                    ShowGroup(group, index);
                });
                groupGrid.Add(item);
                // Urmele apar una după alta.
                item.schedule.Execute(() => item.RemoveFromClassList("map-group__item--enter")).ExecuteLater(60 + 40 * Math.Min(i, 12));
            }
            groupPanel.AddToClassList("map-group--enter");
            UIRouter.SetVisible(groupPanel, true);
            groupPanel.schedule.Execute(() => groupPanel.RemoveFromClassList("map-group--enter")).ExecuteLater(20);
        }

        void HideGroupPanel()
        {
            if (groupPanel == null) return;
            UIRouter.SetVisible(groupPanel, false);
            groupGrid.Clear();
        }

        void HideCard()
        {
            selected = null;
            cardGroup = null;
            UIRouter.SetVisible(cardNav, false);
            UIRouter.SetVisible(card, false);
        }

        static string Round(float meters) =>
            (meters < 100 ? Mathf.RoundToInt(meters / 5f) * 5 : meters < 1000 ? Mathf.RoundToInt(meters / 10f) * 10
                                                                               : Mathf.RoundToInt(meters / 50f) * 50).ToString();

        /// <summary>Direcția de la un punct la altul: „nord”, „nord-est”...</summary>
        static string Bearing(double lat1, double lng1, double lat2, double lng2)
        {
            double dy = lat2 - lat1;
            double dx = (lng2 - lng1) * Math.Cos((lat1 + lat2) * 0.5 * Math.PI / 180.0);
            double degrees = (Math.Atan2(dx, dy) * 180.0 / Math.PI + 360.0) % 360.0;
            string[] names = { "nord", "nord-est", "est", "sud-est", "sud", "sud-vest", "vest", "nord-vest" };
            return names[(int)Math.Round(degrees / 45.0) % 8];
        }

        static float TraceDistance(double lat1, double lng1, double lat2, double lng2)
        {
            const double earthRadius = 6371000.0;
            double toRad = Math.PI / 180.0;
            double dLat = (lat2 - lat1) * toRad;
            double dLng = (lng2 - lng1) * toRad * Math.Cos((lat1 + lat2) * 0.5 * toRad);
            return (float)(earthRadius * Math.Sqrt(dLat * dLat + dLng * dLng));
        }

        // ---------------------------------------------------------------- atingeri

        void OnPointerDown(PointerDownEvent evt)
        {
            pointerDown = true;
            dragging = false;
            pointerId = evt.pointerId;
            pointerStart = pointerLast = evt.localPosition;
            viewport.CapturePointer(evt.pointerId);
        }

        void OnPointerMove(PointerMoveEvent evt)
        {
            if (!pointerDown || evt.pointerId != pointerId) return;
            Vector2 position = evt.localPosition;
            if (!dragging && (position - pointerStart).magnitude > DragThreshold)
            {
                dragging = true;
                followMe = false;
                userMoved = true;
            }
            if (!dragging) return;

            Vector2 delta = position - pointerLast;
            pointerLast = position;
            double x = LngToX(centerLng, zoom) - delta.x;
            double y = LatToY(centerLat, zoom) - delta.y;
            centerLng = XToLng(x, zoom);
            centerLat = YToLat(y, zoom);
            Layout();
        }

        void OnPointerUp(PointerUpEvent evt)
        {
            if (!pointerDown || evt.pointerId != pointerId) return;
            pointerDown = false;
            if (viewport.HasPointerCapture(evt.pointerId)) viewport.ReleasePointer(evt.pointerId);
            if (dragging)
            {
                dragging = false;
                return;
            }

            // Dublă atingere: mărire.
            float now = Time.realtimeSinceStartup;
            if (now - lastTapTime < 0.3f)
            {
                lastTapTime = -1f;
                SetZoom(zoom + 1);
                return;
            }
            lastTapTime = now;

            // Atingere: cel mai apropiat semn de deget, dacă e destul de aproape.
            Vector2 tap = evt.localPosition;
            Marker best = null;
            float bestDistance = TapRadius;
            foreach (var m in markers)
            {
                if (m.Group == null) continue; // ascuns sau în grupul altui semn
                float d = (new Vector2(m.X, m.Y) - tap).magnitude;
                if (d < bestDistance) { bestDistance = d; best = m; }
            }
            if (best == null)
            {
                HideCard();
                HideGroupPanel();
                return;
            }
            if (best.Group.Count > 1)
            {
                // Urmele se despart dacă mărim harta: o mărim pe grup (2 trepte).
                if (zoom < MaxZoom && SplitsAtMaxZoom(best.Group))
                {
                    HideCard();
                    HideGroupPanel();
                    followMe = false;
                    userMoved = true;
                    centerLat = best.Trace.latitude;
                    centerLng = best.Trace.longitude;
                    SetZoom(Math.Min(zoom + 2, MaxZoom));
                    return;
                }
                // Sunt practic în același loc: le arătăm mari, într-o fereastră.
                ShowGroupPanel(best.Group);
                return;
            }
            HideGroupPanel();
            ShowGroup(best.Group, 0);
        }
    }
}
