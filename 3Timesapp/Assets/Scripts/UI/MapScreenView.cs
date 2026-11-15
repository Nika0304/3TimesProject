using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Networking;
using UnityEngine.UIElements;
using ThreeTimes.AR;
using ThreeTimes.Data;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Harta urmelor din orașul tău (Timișoara sau zona din jurul tău): fundal CARTO Positron (bucăți de 256 px, proiecția Web Mercator), urmele din jur ca forme
    /// mici (cu polaroid / microfon după tip) și poziția ta. Se trage cu degetul, se mărește din butoane sau
    /// cu dublă atingere. Atingerea unei urme arată o previzualizare: emoția, tipul și cât de departe e.
    /// Conținutul altora se deschide doar de aproape, prin cameră; urmele tale se pot deschide de aici.
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
        readonly MapConfig config;
        readonly PhoneLocation phone = new PhoneLocation();

        readonly VisualElement screen, viewport, tileLayer, markerLayer, me, card, cardShapeHolder;
        readonly Label status, cardTitle, cardKind, cardDistance, cardHint;
        readonly Button cardOpen;

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
        readonly List<(SavedTrace trace, VisualElement marker)> markers = new List<(SavedTrace, VisualElement)>();
        SavedTrace selected;
        bool fetching;
        bool fetchedOnce;
        double fetchLat, fetchLng;
        float fetchRadius, lastFetchTime = -10f;

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
                             Action<SavedTrace> openTrace)
        {
            this.router = router;
            this.host = host;
            this.tracker = tracker;
            this.loadTraces = loadTraces;
            this.openTrace = openTrace;
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
            UIRouter.SetVisible(screen, false);
        }

        /// <summary>Butonul „Înapoi” de pe Android: întâi închide previzualizarea, apoi harta.</summary>
        public void Back()
        {
            if (selected != null) HideCard();
            else Close();
        }

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

        /// <summary>Centrul hărții rămâne în zona orașului.</summary>
        void ClampCenter()
        {
            centerLat = Math.Max(areaMinLat, Math.Min(areaMaxLat, centerLat));
            centerLng = Math.Max(areaMinLng, Math.Min(areaMaxLng, centerLng));
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

            foreach (var (trace, marker) in markers)
                Place(marker, LngToX(trace.longitude, zoom) - left, LatToY(trace.latitude, zoom) - top, w, h);

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
            if (fetching || dragging) return;
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
                if (IsOpen) BuildMarkers(list);
            });
        }

        void BuildMarkers(List<SavedTrace> list)
        {
            markerLayer.Clear();
            markers.Clear();
            foreach (var t in list)
            {
                var style = TraceStyle.Of(t);

                var marker = new VisualElement { pickingMode = PickingMode.Ignore };
                marker.AddToClassList("map-marker");
                if (t.mine) marker.AddToClassList("map-marker--mine");
                var backdrop = TraceTypeBackdrop.Create(t.type, style.Color, MarkerSize);
                if (backdrop != null) marker.Add(backdrop);
                var shape = new VisualElement { pickingMode = PickingMode.Ignore };
                shape.AddToClassList("map-marker__shape");
                shape.style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
                marker.Add(shape);

                markerLayer.Add(marker);
                markers.Add((t, marker));
            }
            Layout();
        }

        void ShowCard(SavedTrace t)
        {
            selected = t;
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

        void HideCard()
        {
            selected = null;
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

            // Atingere: cea mai apropiată urmă de deget, dacă e destul de aproape.
            Vector2 tap = evt.localPosition;
            SavedTrace best = null;
            float bestDistance = TapRadius;
            foreach (var (trace, marker) in markers)
            {
                if (marker.style.display == DisplayStyle.None) continue;
                var center = new Vector2(marker.style.left.value.value, marker.style.top.value.value);
                float d = (center - tap).magnitude;
                if (d < bestDistance) { bestDistance = d; best = trace; }
            }
            if (best != null) ShowCard(best);
            else HideCard();
        }
    }
}
