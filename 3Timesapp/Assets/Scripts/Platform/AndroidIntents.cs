using UnityEngine;

namespace ThreeTimes.Platform
{
    /// <summary>Deschide ecrane din setările Android (permisiuni, locație).</summary>
    public static class AndroidIntents
    {
        public static void OpenAppSettings() => Open("android.settings.APPLICATION_DETAILS_SETTINGS", withPackage: true);

        public static void OpenLocationSettings() => Open("android.settings.LOCATION_SOURCE_SETTINGS", withPackage: false);

        static void Open(string action, bool withPackage)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var intent = new AndroidJavaObject("android.content.Intent", action))
                {
                    if (withPackage)
                    {
                        using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                        using (var uri = uriClass.CallStatic<AndroidJavaObject>("fromParts", "package", Application.identifier, null))
                        {
                            intent.Call<AndroidJavaObject>("setData", uri);
                        }
                    }
                    const int FlagActivityNewTask = 0x10000000;
                    intent.Call<AndroidJavaObject>("addFlags", FlagActivityNewTask);
                    activity.Call("startActivity", intent);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut deschide {action}: {e.Message}");
            }
#else
            Debug.Log($"[3Times] (Editor) Pe telefon s-ar deschide: {action}");
#endif
        }
    }
}
