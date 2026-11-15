using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using ThreeTimes.AR;
using ThreeTimes.Data;
using ThreeTimes.Platform;

namespace ThreeTimes.UI
{
    /// <summary>
    /// „Time postcard”: o carte poștală din ce vezi acum prin cameră, cu urmele din jur. Se face poza (fără interfață),
    /// se așază într-o ramă cu modul („acum”), locul, data, emoțiile din jur și logo-ul, apoi cartea întreagă se salvează
    /// ca imagine: în colecția din aplicație (Meniu → Cărțile mele poștale), iar la cerere în galerie sau trimisă
    /// prin meniul Android de partajare.
    /// </summary>
    public class PostcardView
    {
        static readonly string[] Months =
            { "ianuarie", "februarie", "martie", "aprilie", "mai", "iunie", "iulie", "august", "septembrie", "octombrie", "noiembrie", "decembrie" };

        readonly UIRouter router;
        readonly MonoBehaviour host;
        readonly GeospatialTracker tracker;
        readonly Func<List<SavedTrace>> visibleTraces;
        readonly VisualElement root, overlay, card, photo, emotions, savedImage, screenList, grid;
        readonly Label word, place, date, emotionsLabel, listEmpty;
        readonly Button deleteButton;

        Texture2D photoTexture, savedTexture;
        string current;   // numele fișierului cărții afișate
        bool busy;

        public bool IsOpen { get; private set; }
        public bool ListOpen => !screenList.ClassListContains("hidden");

        public PostcardView(VisualElement root, UIRouter router, MonoBehaviour host, GeospatialTracker tracker,
                            Func<List<SavedTrace>> visibleTraces)
        {
            this.root = root;
            this.router = router;
            this.host = host;
            this.tracker = tracker;
            this.visibleTraces = visibleTraces;

            overlay = root.Q("postcard-overlay");
            card = root.Q("postcard-card");
            photo = root.Q("postcard-photo");
            word = root.Q<Label>("postcard-word");
            place = root.Q<Label>("postcard-place");
            date = root.Q<Label>("postcard-date");
            emotions = root.Q("postcard-emotions");
            emotionsLabel = root.Q<Label>("postcard-emotions-label");
            savedImage = root.Q("postcard-saved");
            deleteButton = root.Q<Button>("postcard-delete");
            screenList = root.Q("screen-postcards");
            grid = root.Q("postcards-grid");
            listEmpty = root.Q<Label>("postcards-empty");

            root.Q<Button>("postcard-close").clicked += Close;
            root.Q<Button>("postcard-share").clicked += Share;
            root.Q<Button>("postcard-save").clicked += SaveToGallery;
            deleteButton.clicked += DeleteCurrent;
            root.Q<Button>("postcards-back").clicked += CloseList;
        }

        // ---------------------------------------------------------------- facerea cărții

        /// <summary>Face o carte poștală nouă din imaginea de acum. modeWord: „acum”, „1989”, „2030”.</summary>
        public void Create(string modeWord)
        {
            if (busy || IsOpen) return;
            host.StartCoroutine(CreateRoutine(modeWord));
        }

        IEnumerator CreateRoutine(string modeWord)
        {
            busy = true;

            // 1. Poza: ascundem toată interfața un cadru, ca să rămână doar ce se vede prin cameră (cu urmele).
            var previous = root.style.display;
            root.style.display = DisplayStyle.None;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            root.style.display = previous;

            // 2. Rama: modul, locul, data, emoțiile din jur.
            ClearPhoto();
            photoTexture = shot;
            photo.style.backgroundImage = new StyleBackground(photoTexture);
            word.text = modeWord;
            place.text = PlaceName();
            var now = DateTime.Now;
            date.text = $"{now.Day} {Months[now.Month - 1]} {now.Year} · {now:HH:mm}";
            FillEmotions();

            current = null;
            ShowCard(true);

            // 3. Așteptăm să se așeze rama pe ecran, apoi salvăm exact zona cărții ca imagine.
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();
            current = SaveCardFromScreen();
            busy = false;
            router.Toast(current != null
                ? "Cartea poștală e salvată în aplicație (Meniu → Cărțile mele poștale)."
                : "Cartea poștală nu s-a putut salva.", 3.5f);
        }

        string PlaceName()
        {
            if (tracker == null || !tracker.Fix.HasValue) return "";
            var f = tracker.Fix.Value;
            // Piața Victoriei (Operă–Catedrală) și Timișoara; în altă parte, coordonatele.
            if (f.Latitude > 45.7505 && f.Latitude < 45.7560 && f.Longitude > 21.2235 && f.Longitude < 21.2275)
                return "Piața Victoriei, Timișoara";
            if (f.Latitude > 45.70 && f.Latitude < 45.81 && f.Longitude > 21.14 && f.Longitude < 21.33)
                return "Timișoara";
            return $"{f.Latitude.ToString("F4", CultureInfo.InvariantCulture)}, {f.Longitude.ToString("F4", CultureInfo.InvariantCulture)}";
        }

        /// <summary>Iconițele emoțiilor din jur (cel mult 5, fără repetări) și câte urme sunt.</summary>
        void FillEmotions()
        {
            emotions.Clear();
            var traces = visibleTraces?.Invoke() ?? new List<SavedTrace>();
            var seen = new HashSet<string>();
            foreach (var t in traces)
            {
                var style = TraceStyle.Of(t);
                if (seen.Contains(style.Label) || seen.Count >= 5) continue;
                seen.Add(style.Label);
                var icon = new VisualElement { pickingMode = PickingMode.Ignore };
                icon.AddToClassList("postcard-emotion");
                icon.style.backgroundImage = new StyleBackground(TraceIcon.Get(style));
                emotions.Add(icon);
            }
            emotionsLabel.text = traces.Count == 0 ? "" : traces.Count == 1 ? "o urmă aici" : $"{traces.Count} urme aici";
        }

        /// <summary>Decupează din ecran zona cărții și o salvează (JPEG). Întoarce numele fișierului.</summary>
        string SaveCardFromScreen()
        {
            var screen = ScreenCapture.CaptureScreenshotAsTexture();
            if (screen == null) return null;
            try
            {
                float scale = Screen.width / root.layout.width;
                Rect bound = card.worldBound;
                int x = Mathf.Clamp(Mathf.RoundToInt(bound.xMin * scale), 0, Screen.width - 1);
                int w = Mathf.Clamp(Mathf.RoundToInt(bound.width * scale), 1, Screen.width - x);
                int h = Mathf.Clamp(Mathf.RoundToInt(bound.height * scale), 1, Screen.height);
                int yTop = Mathf.RoundToInt(bound.yMin * scale);
                int y = Mathf.Clamp(Screen.height - yTop - h, 0, Screen.height - h); // textura are originea jos

                var cardTexture = new Texture2D(w, h, TextureFormat.RGB24, false);
                cardTexture.SetPixels(screen.GetPixels(x, y, w, h));
                cardTexture.Apply();
                string name = PostcardStore.Save(cardTexture.EncodeToJPG(90));
                UnityEngine.Object.Destroy(cardTexture);
                return name;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut decupa cartea poștală: {e.Message}");
                return null;
            }
            finally
            {
                UnityEngine.Object.Destroy(screen);
            }
        }

        // ---------------------------------------------------------------- afișare

        /// <summary>fresh = cartea tocmai făcută (rama vie); altfel o carte din colecție (imaginea salvată).</summary>
        void ShowCard(bool fresh)
        {
            IsOpen = true;
            UIRouter.SetVisible(card, fresh);
            UIRouter.SetVisible(savedImage, !fresh);
            UIRouter.SetVisible(deleteButton, !fresh);
            UIRouter.SetVisible(overlay, true);
        }

        public void Close()
        {
            if (!IsOpen || busy) return;
            IsOpen = false;
            UIRouter.SetVisible(overlay, false);
            ClearPhoto();
            ClearSaved();
            current = null;
        }

        void ClearPhoto()
        {
            photo.style.backgroundImage = StyleKeyword.None;
            if (photoTexture != null) UnityEngine.Object.Destroy(photoTexture);
            photoTexture = null;
        }

        void ClearSaved()
        {
            savedImage.style.backgroundImage = StyleKeyword.None;
            if (savedTexture != null) UnityEngine.Object.Destroy(savedTexture);
            savedTexture = null;
        }

        // ---------------------------------------------------------------- trimite / salvează / șterge

        void Share()
        {
            if (busy || current == null) return;
            if (!AndroidGallery.Available)
            {
                router.Toast("Trimiterea merge doar pe telefon.");
                return;
            }
            string uri = EnsureInGallery();
            if (uri == null || !AndroidGallery.ShareImage(uri, "Din 3Times"))
                router.Toast("Nu am putut deschide trimiterea.");
        }

        void SaveToGallery()
        {
            if (busy || current == null) return;
            if (!AndroidGallery.Available)
            {
                router.Toast("Salvarea în galerie merge doar pe telefon. În aplicație e deja salvată.");
                return;
            }
            bool already = PostcardStore.GalleryUri(current).Length > 0;
            string uri = EnsureInGallery();
            router.Toast(uri == null ? "Nu am putut salva în galerie."
                       : already ? "E deja în galerie (Poze → 3Times)."
                       : "Salvată în galerie (Poze → 3Times).");
        }

        /// <summary>Pune cartea în galerie o singură dată și întoarce adresa ei de acolo.</summary>
        string EnsureInGallery()
        {
            string uri = PostcardStore.GalleryUri(current);
            if (uri.Length > 0) return uri;
            var bytes = PostcardStore.Bytes(current);
            if (bytes == null) return null;
            uri = AndroidGallery.SaveJpeg(bytes, current);
            if (uri != null) PostcardStore.SetGalleryUri(current, uri);
            return uri;
        }

        void DeleteCurrent()
        {
            if (busy || current == null) return;
            PostcardStore.Delete(current);
            router.Toast("Cartea poștală a fost ștearsă din aplicație.");
            Close();
            if (ListOpen) FillList();
        }

        // ---------------------------------------------------------------- colecția (Meniu → Cărțile mele poștale)

        public void OpenList()
        {
            FillList();
            UIRouter.SetVisible(screenList, true);
        }

        public void CloseList()
        {
            UIRouter.SetVisible(screenList, false);
            foreach (var child in grid.Children())
                if (child.userData is Texture2D t) UnityEngine.Object.Destroy(t);
            grid.Clear();
        }

        void FillList()
        {
            foreach (var child in grid.Children())
                if (child.userData is Texture2D t) UnityEngine.Object.Destroy(t);
            grid.Clear();

            var names = PostcardStore.All();
            UIRouter.SetVisible(listEmpty, names.Count == 0);
            foreach (var name in names)
            {
                var texture = PostcardStore.Load(name);
                if (texture == null) continue;
                var thumb = new Button { userData = texture };
                thumb.AddToClassList("postcard-thumb");
                thumb.style.backgroundImage = new StyleBackground(texture);
                string n = name;
                thumb.clicked += () => OpenSaved(n);
                grid.Add(thumb);
            }
        }

        void OpenSaved(string name)
        {
            ClearSaved();
            savedTexture = PostcardStore.Load(name);
            if (savedTexture == null) return;
            current = name;
            savedImage.style.backgroundImage = new StyleBackground(savedTexture);
            ShowCard(false);
        }
    }
}
