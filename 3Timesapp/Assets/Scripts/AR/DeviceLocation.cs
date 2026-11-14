using UnityEngine;

namespace ThreeTimes.AR
{
    /// <summary>
    /// Verifică dacă locația telefonului (GPS) e pornită din setări.
    /// Poziția propriu-zisă vine din ARCore Geospatial, nu de aici.
    /// </summary>
    public static class DeviceLocation
    {
        public static bool IsEnabled
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                try
                {
                    using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                    using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                    using (var manager = activity.Call<AndroidJavaObject>("getSystemService", "location"))
                    {
                        return manager.Call<bool>("isProviderEnabled", "gps")
                            || manager.Call<bool>("isProviderEnabled", "network");
                    }
                }
                catch (System.Exception e)
                {
                    Debug.LogWarning($"[3Times] Nu am putut verifica locația: {e.Message}");
                    return true; // nu blocăm aplicația; ARCore va raporta dacă lipsește locația
                }
#else
                return true;
#endif
            }
        }
    }
}
