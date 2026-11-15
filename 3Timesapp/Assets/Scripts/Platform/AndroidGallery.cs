using System;
using UnityEngine;

namespace ThreeTimes.Platform
{
    /// <summary>
    /// Salvează o imagine în galeria telefonului (Poze → album „3Times”) și o trimite prin meniul Android de partajare.
    /// Partajăm adresa din galerie (content://), așa că nu e nevoie de un FileProvider în manifest.
    /// Pe Android 9 și mai vechi, salvarea în galerie ar cere și permisiunea de scriere; telefonul de test e mai nou.
    /// </summary>
    public static class AndroidGallery
    {
        public static bool Available
        {
            get
            {
#if UNITY_ANDROID && !UNITY_EDITOR
                return true;
#else
                return false;
#endif
            }
        }

        /// <summary>Salvează JPEG-ul în galerie și întoarce adresa lui (content://...), sau null.</summary>
        public static string SaveJpeg(byte[] jpeg, string fileName)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var resolver = activity.Call<AndroidJavaObject>("getContentResolver"))
                using (var values = new AndroidJavaObject("android.content.ContentValues"))
                using (var media = new AndroidJavaClass("android.provider.MediaStore$Images$Media"))
                using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                {
                    int sdk = version.GetStatic<int>("SDK_INT");
                    values.Call("put", "_display_name", fileName);
                    values.Call("put", "mime_type", "image/jpeg");
                    if (sdk >= 29)
                    {
                        values.Call("put", "relative_path", "Pictures/3Times");
                        using (var pending = new AndroidJavaObject("java.lang.Integer", 1))
                            values.Call("put", "is_pending", pending);
                    }

                    var collection = media.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");
                    var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                    if (uri == null) return null;

                    using (var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri))
                    {
                        // Java are octeți cu semn: trimitem un sbyte[].
                        var bytes = new sbyte[jpeg.Length];
                        Buffer.BlockCopy(jpeg, 0, bytes, 0, jpeg.Length);
                        stream.Call("write", bytes);
                        stream.Call("close");
                    }

                    if (sdk >= 29)
                    {
                        using (var done = new AndroidJavaObject("android.content.ContentValues"))
                        using (var zero = new AndroidJavaObject("java.lang.Integer", 0))
                        {
                            done.Call("put", "is_pending", zero);
                            resolver.Call<int>("update", uri, done, null, null);
                        }
                    }
                    return uri.Call<string>("toString");
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut salva în galerie: {e.Message}");
                return null;
            }
#else
            Debug.Log($"[3Times] (Editor) Pe telefon, {fileName} s-ar salva în galerie.");
            return null;
#endif
        }

        /// <summary>Deschide meniul Android de partajare pentru o imagine din galerie.</summary>
        public static bool ShareImage(string contentUri, string message)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer"))
                using (var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity"))
                using (var uriClass = new AndroidJavaClass("android.net.Uri"))
                using (var uri = uriClass.CallStatic<AndroidJavaObject>("parse", contentUri))
                using (var intent = new AndroidJavaObject("android.content.Intent", "android.intent.action.SEND"))
                using (var intentClass = new AndroidJavaClass("android.content.Intent"))
                {
                    intent.Call<AndroidJavaObject>("setType", "image/jpeg");
                    intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.STREAM", uri);
                    if (!string.IsNullOrEmpty(message))
                        intent.Call<AndroidJavaObject>("putExtra", "android.intent.extra.TEXT", message);
                    const int FlagGrantReadUriPermission = 0x00000001;
                    intent.Call<AndroidJavaObject>("addFlags", FlagGrantReadUriPermission);
                    using (var chooser = intentClass.CallStatic<AndroidJavaObject>("createChooser", intent, "Trimite cartea poștală"))
                        activity.Call("startActivity", chooser);
                }
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut deschide partajarea: {e.Message}");
                return false;
            }
#else
            Debug.Log("[3Times] (Editor) Pe telefon s-ar deschide meniul de partajare.");
            return false;
#endif
        }
    }
}
