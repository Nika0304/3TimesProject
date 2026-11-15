using System;
using UnityEngine;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ThreeTimes.AR
{
    /// <summary>
    /// Poziția aproximativă a telefonului din GPS / rețea (Android LocationManager), pentru hartă,
    /// când camera e oprită. Cu camera pornită, harta folosește poziția mult mai precisă din ARCore.
    /// Proiectul folosește doar noul Input System, așa că nu ne bazăm pe Input.location.
    /// În Editor întoarce un punct fix în Piața Victoriei.
    /// </summary>
    public class PhoneLocation
    {
        public double Latitude { get; private set; }
        public double Longitude { get; private set; }
        public float AccuracyMeters { get; private set; }
        public bool HasFix { get; private set; }
        public bool PermissionDenied { get; private set; }

        readonly object gate = new object();

#if UNITY_ANDROID && !UNITY_EDITOR
        AndroidJavaObject manager;
        Listener listener;
        bool asking;

        /// <summary>Primește pozițiile de la Android (pe firul principal Android, nu pe cel Unity).</summary>
        class Listener : AndroidJavaProxy
        {
            readonly PhoneLocation owner;
            public Listener(PhoneLocation owner) : base("android.location.LocationListener") { this.owner = owner; }

            // Android 12+ poate trimite și o listă de poziții, pe aceeași metodă; o luăm pe ultima.
            void onLocationChanged(AndroidJavaObject location)
            {
                try
                {
                    var target = location;
                    using (var cls = location.Call<AndroidJavaObject>("getClass"))
                    {
                        string name = cls.Call<string>("getName");
                        if (!name.Contains("Location"))
                        {
                            int size = location.Call<int>("size");
                            if (size == 0) return;
                            target = location.Call<AndroidJavaObject>("get", size - 1);
                        }
                    }
                    owner.Set(target.Call<double>("getLatitude"), target.Call<double>("getLongitude"),
                              target.Call<float>("getAccuracy"));
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[3Times] Poziție GPS ilizibilă: {e.Message}");
                }
            }

            void onProviderEnabled(string provider) { }
            void onProviderDisabled(string provider) { }
            void onStatusChanged(string provider, int status, AndroidJavaObject extras) { }
            void onFlushComplete(int requestCode) { }
        }
#endif

        void Set(double lat, double lng, float accuracy)
        {
            lock (gate)
            {
                // Păstrăm poziția mai precisă dacă cea nouă e mult mai slabă și vine imediat după.
                if (HasFix && accuracy > AccuracyMeters * 3f && accuracy > 50f) return;
                Latitude = lat;
                Longitude = lng;
                AccuracyMeters = accuracy;
                HasFix = true;
            }
        }

        public void Start()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (listener != null) return;
            if (!Permission.HasUserAuthorizedPermission(Permission.FineLocation))
            {
                if (asking) return;
                asking = true;
                var callbacks = new PermissionCallbacks();
                callbacks.PermissionGranted += _ => { asking = false; PermissionDenied = false; Start(); };
                callbacks.PermissionDenied += _ => { asking = false; PermissionDenied = true; };
                Permission.RequestUserPermission(Permission.FineLocation, callbacks);
                return;
            }
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    manager = activity.Call<AndroidJavaObject>("getSystemService", "location");
                listener = new Listener(this);
                using (var looperClass = new AndroidJavaClass("android.os.Looper"))
                using (var looper = looperClass.CallStatic<AndroidJavaObject>("getMainLooper"))
                {
                    foreach (var provider in new[] { "network", "gps" })
                    {
                        if (!manager.Call<bool>("isProviderEnabled", provider)) continue;
                        // Ultima poziție cunoscută, ca harta să nu pornească goală.
                        var last = manager.Call<AndroidJavaObject>("getLastKnownLocation", provider);
                        if (last != null)
                            Set(last.Call<double>("getLatitude"), last.Call<double>("getLongitude"), last.Call<float>("getAccuracy"));
                        manager.Call("requestLocationUpdates", provider, 2000L, 1f, listener, looper);
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut porni GPS-ul pentru hartă: {e.Message}");
                listener = null;
            }
#else
            Set(45.75270, 21.22530, 10f);
#endif
        }

        public void Stop()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            if (manager != null && listener != null)
            {
                try { manager.Call("removeUpdates", listener); }
                catch (Exception e) { Debug.LogWarning($"[3Times] removeUpdates: {e.Message}"); }
            }
            listener = null;
#endif
        }
    }
}
