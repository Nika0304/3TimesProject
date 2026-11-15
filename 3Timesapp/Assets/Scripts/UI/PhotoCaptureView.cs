using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR.ARFoundation;
using ThreeTimes.AR;
using ThreeTimes.Media;

namespace ThreeTimes.UI
{
    /// <summary>De unde vine poza unei urme. Fără galerie: poza se face mereu pe loc.</summary>
    public enum PhotoSource
    {
        Selfie,          // camera din față
        Place,           // camera din spate, doar locul
        PlaceWithTraces  // camera din spate, cu urmele din AR în cadru
    }

    /// <summary>
    /// Ecranul de fotografiat pentru o urmă. Pentru loc (cu sau fără urme) folosim chiar imaginea
    /// din AR: ascundem interfața un cadru și salvăm ecranul. Pentru selfie, camera din spate e ținută
    /// de ARCore, așa că punem sesiunea AR pe pauză, pornim camera din față și o reluăm după poză.
    /// </summary>
    public class PhotoCaptureView
    {
        readonly UIRouter router;
        readonly MonoBehaviour host;
        readonly VisualElement root, overlay, previewFrame, screenMain;
        readonly Image preview;
        readonly Label hint;

        Action<string> onDone;
        PhotoSource source;
        WebCamTexture webcam;
        bool webcamFront;
        ARSession pausedSession;
        bool busy;
        IVisualElementScheduledItem layoutJob;

        public bool IsOpen { get; private set; }

        public PhotoCaptureView(VisualElement root, UIRouter router, MonoBehaviour host)
        {
            this.root = root;
            this.router = router;
            this.host = host;
            overlay = root.Q("capture-overlay");
            previewFrame = root.Q("capture-preview-frame");
            hint = root.Q<Label>("capture-hint");
            screenMain = root.Q("screen-main");

            preview = new Image { scaleMode = ScaleMode.ScaleAndCrop, pickingMode = PickingMode.Ignore };
            preview.AddToClassList("capture-preview");
            previewFrame.Add(preview);

            root.Q<Button>("capture-shutter").clicked += Shoot;
            root.Q<Button>("capture-cancel").clicked += Cancel;
        }

        /// <summary>Deschide ecranul de fotografiat. onDone primește numele fișierului sau null (renunțare).</summary>
        public void Open(PhotoSource from, Action<string> done)
        {
            if (IsOpen) return;
            IsOpen = true;
            busy = false;
            source = from;
            onDone = done;

            hint.text = from == PhotoSource.Selfie ? "Selfie, aici, în locul urmei."
                      : from == PhotoSource.Place ? "Poza locului, fără urme."
                      : "Poza locului, cu urmele din jur.";
            UIRouter.SetVisible(screenMain, false);
            UIRouter.SetVisible(previewFrame, false);
            UIRouter.SetVisible(overlay, true);

            if (from == PhotoSource.Selfie) host.StartCoroutine(StartSelfie());
            else SetTracesVisible(from == PhotoSource.PlaceWithTraces);
        }

        public void Cancel()
        {
            if (!IsOpen || busy) return;
            Finish(null);
        }

        void Shoot()
        {
            if (!IsOpen || busy) return;
            if (source == PhotoSource.Selfie)
            {
                if (webcam == null || !webcam.isPlaying || webcam.width <= 16) return;
                host.StartCoroutine(CaptureSelfie());
            }
            else
            {
                host.StartCoroutine(CaptureScreen());
            }
        }

        // ---------------------------------------------------------------- locul (imaginea din AR)

        IEnumerator CaptureScreen()
        {
            busy = true;
            // Ascundem toată interfața, ca în poză să rămână doar ce se vede prin cameră.
            var previousDisplay = root.style.display;
            root.style.display = DisplayStyle.None;
            yield return null;
            yield return null;
            yield return new WaitForEndOfFrame();

            var shot = ScreenCapture.CaptureScreenshotAsTexture();
            root.style.display = previousDisplay;

            string file = shot != null ? MediaStore.SavePhoto(shot) : null;
            if (shot != null) UnityEngine.Object.Destroy(shot);
            busy = false;
            Finish(file, failed: file == null);
        }

        static void SetTracesVisible(bool visible)
        {
            var traces = UnityEngine.Object.FindFirstObjectByType<TraceManager>();
            if (traces != null) traces.SetTracesVisible(visible);
        }

        // ---------------------------------------------------------------- selfie

        IEnumerator StartSelfie()
        {
            busy = true;
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                busy = false;
                router.Toast("Nu am găsit o cameră pentru selfie.");
                Finish(null);
                yield break;
            }
            int index = 0;
            for (int i = 0; i < devices.Length; i++)
                if (devices[i].isFrontFacing) { index = i; break; }
            webcamFront = devices[index].isFrontFacing;

            // Camera din spate e folosită de ARCore: punem AR-ul pe pauză cât facem selfie-ul.
            var session = UnityEngine.Object.FindFirstObjectByType<ARSession>();
            if (session != null && session.enabled)
            {
                session.enabled = false;
                pausedSession = session;
                yield return null;
                yield return null;
            }

            webcam = new WebCamTexture(devices[index].name, 1280, 720, 30);
            webcam.Play();
            float waited = 0f;
            while (IsOpen && webcam != null && webcam.width <= 16 && waited < 4f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            busy = false;
            if (!IsOpen) yield break;
            if (webcam == null || webcam.width <= 16)
            {
                router.Toast("Camera pentru selfie nu a pornit.");
                Finish(null);
                yield break;
            }

            preview.image = webcam;
            UIRouter.SetVisible(previewFrame, true);
            layoutJob = previewFrame.schedule.Execute(LayoutPreview).Every(100);
            LayoutPreview();
        }

        /// <summary>Rotește și oglindește previzualizarea, ca selfie-ul să apară drept, ca într-o oglindă.</summary>
        void LayoutPreview()
        {
            if (webcam == null) return;
            float w = previewFrame.layout.width, h = previewFrame.layout.height;
            if (float.IsNaN(w) || w <= 0f) return;

            int angle = Normalize(webcam.videoRotationAngle);
            bool sideways = angle == 90 || angle == 270;
            float pw = sideways ? h : w, ph = sideways ? w : h;
            preview.style.width = pw;
            preview.style.height = ph;
            preview.style.left = (w - pw) / 2f;
            preview.style.top = (h - ph) / 2f;
            preview.style.rotate = new Rotate(angle);
            Mirror(sideways, out float sx, out float sy);
            preview.style.scale = new Scale(new Vector3(sx, sy, 1f));
        }

        /// <summary>
        /// Oglindirile, în spațiul imaginii (înainte de rotire): cea verticală a camerei și,
        /// pentru camera din față, cea orizontală de pe ecran (care după o rotire de 90° devine verticală).
        /// </summary>
        void Mirror(bool sideways, out float sx, out float sy)
        {
            sx = 1f;
            sy = webcam.videoVerticallyMirrored ? -1f : 1f;
            if (webcamFront)
            {
                if (sideways) sy = -sy;
                else sx = -sx;
            }
        }

        IEnumerator CaptureSelfie()
        {
            busy = true;
            yield return null;

            int w = webcam.width, h = webcam.height;
            Color32[] pixels = webcam.GetPixels32();
            int angle = Normalize(webcam.videoRotationAngle);
            Mirror(angle == 90 || angle == 270, out float sx, out float sy);

            // Aceleași operații ca la previzualizare: întâi oglindirile, apoi rotirea.
            if (sx < 0f) pixels = FlipX(pixels, w, h);
            if (sy < 0f) pixels = FlipY(pixels, w, h);
            for (int r = 0; r < angle / 90; r++)
            {
                pixels = RotateClockwise(pixels, w, h);
                (w, h) = (h, w);
            }

            var photo = new Texture2D(w, h, TextureFormat.RGBA32, false);
            photo.SetPixels32(pixels);
            photo.Apply();
            string file = MediaStore.SavePhoto(photo);
            UnityEngine.Object.Destroy(photo);
            busy = false;
            Finish(file, failed: file == null);
        }

        static int Normalize(int angle) => ((angle % 360) + 360) % 360 / 90 * 90;

        static Color32[] FlipX(Color32[] src, int w, int h)
        {
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
                for (int x = 0; x < w; x++)
                    dst[y * w + x] = src[y * w + (w - 1 - x)];
            return dst;
        }

        static Color32[] FlipY(Color32[] src, int w, int h)
        {
            var dst = new Color32[src.Length];
            for (int y = 0; y < h; y++)
                Array.Copy(src, (h - 1 - y) * w, dst, y * w, w);
            return dst;
        }

        /// <summary>Rotire cu 90° în sensul acelor de ceasornic (pixelii pornesc din colțul stânga-jos).</summary>
        static Color32[] RotateClockwise(Color32[] src, int w, int h)
        {
            // Rezultatul are lățimea h și înălțimea w.
            var dst = new Color32[src.Length];
            for (int y2 = 0; y2 < w; y2++)
                for (int x2 = 0; x2 < h; x2++)
                    dst[y2 * h + x2] = src[x2 * w + (w - 1 - y2)];
            return dst;
        }

        // ---------------------------------------------------------------- închidere

        void Finish(string file, bool failed = false)
        {
            layoutJob?.Pause();
            layoutJob = null;
            if (webcam != null)
            {
                webcam.Stop();
                UnityEngine.Object.Destroy(webcam);
                webcam = null;
            }
            preview.image = null;
            if (pausedSession != null)
            {
                pausedSession.enabled = true;
                pausedSession = null;
            }
            SetTracesVisible(true);

            UIRouter.SetVisible(overlay, false);
            UIRouter.SetVisible(screenMain, true);
            IsOpen = false;
            if (failed) router.Toast("Poza nu a putut fi salvată. Mai încearcă.");

            var done = onDone;
            onDone = null;
            done?.Invoke(file);
        }
    }
}
