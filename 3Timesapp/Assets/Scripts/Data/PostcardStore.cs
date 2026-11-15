using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Cărțile poștale făcute în aplicație (persistentDataPath/postcards/*.jpg), de revăzut din Meniu.
    /// Dacă o carte a fost salvată și în galerie, ținem minte adresa ei de acolo (pentru „Trimite”).
    /// </summary>
    public static class PostcardStore
    {
        static string Folder
        {
            get
            {
                string path = Path.Combine(Application.persistentDataPath, "postcards");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string Save(byte[] jpeg)
        {
            try
            {
                string name = $"3times-{DateTime.Now:yyyyMMdd-HHmmss}.jpg";
                File.WriteAllBytes(Path.Combine(Folder, name), jpeg);
                return name;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut salva cartea poștală: {e.Message}");
                return null;
            }
        }

        /// <summary>Numele cărților, de la cea mai nouă.</summary>
        public static List<string> All()
        {
            var names = new List<string>();
            try
            {
                foreach (var path in Directory.GetFiles(Folder, "*.jpg")) names.Add(Path.GetFileName(path));
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut citi cărțile poștale: {e.Message}");
            }
            names.Sort((a, b) => string.CompareOrdinal(b, a));
            return names;
        }

        public static byte[] Bytes(string name)
        {
            string path = Path.Combine(Folder, name);
            return File.Exists(path) ? File.ReadAllBytes(path) : null;
        }

        /// <summary>Cine o primește trebuie s-o distrugă când nu mai e nevoie.</summary>
        public static Texture2D Load(string name)
        {
            var bytes = Bytes(name);
            if (bytes == null) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (texture.LoadImage(bytes)) return texture;
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        public static void Delete(string name)
        {
            try
            {
                string path = Path.Combine(Folder, name);
                if (File.Exists(path)) File.Delete(path);
                PlayerPrefs.DeleteKey("postcard-uri-" + name);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut șterge cartea poștală: {e.Message}");
            }
        }

        public static string GalleryUri(string name) => PlayerPrefs.GetString("postcard-uri-" + name, "");

        public static void SetGalleryUri(string name, string uri)
        {
            PlayerPrefs.SetString("postcard-uri-" + name, uri ?? "");
            PlayerPrefs.Save();
        }
    }
}
