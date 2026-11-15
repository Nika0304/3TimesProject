using System;
using System.IO;
using UnityEngine;

namespace ThreeTimes.Media
{
    /// <summary>
    /// Pozele și înregistrările urmelor, ținute ca fișiere pe telefon (persistentDataPath/media).
    /// În traces.json se păstrează doar numele fișierului. Când serverul e gata, fișierele se urcă de aici.
    /// </summary>
    public static class MediaStore
    {
        /// <summary>Cel mai mare lat al unei poze salvate (px). Destul pentru telefon, fișier mic.</summary>
        public const int MaxPhotoSide = 1280;
        const int JpegQuality = 80;

        static string Folder
        {
            get
            {
                string path = Path.Combine(Application.persistentDataPath, "media");
                Directory.CreateDirectory(path);
                return path;
            }
        }

        public static string FullPath(string fileName) => Path.Combine(Folder, fileName);

        public static bool Exists(string fileName) =>
            !string.IsNullOrEmpty(fileName) && File.Exists(FullPath(fileName));

        public static void Delete(string fileName)
        {
            if (string.IsNullOrEmpty(fileName)) return;
            try
            {
                string path = FullPath(fileName);
                if (File.Exists(path)) File.Delete(path);
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut șterge {fileName}: {e.Message}");
            }
        }

        // ---------------------------------------------------------------- poze

        /// <summary>Micșorează poza dacă e nevoie și o salvează ca JPEG. Întoarce numele fișierului.</summary>
        public static string SavePhoto(Texture2D photo)
        {
            var small = Resize(photo, MaxPhotoSide);
            try
            {
                string name = $"photo-{Guid.NewGuid():N}.jpg";
                File.WriteAllBytes(FullPath(name), small.EncodeToJPG(JpegQuality));
                return name;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut salva poza: {e.Message}");
                return null;
            }
            finally
            {
                if (small != photo) UnityEngine.Object.Destroy(small);
            }
        }

        /// <summary>Citește o poză salvată. Cine o primește trebuie s-o distrugă când nu mai e nevoie.</summary>
        public static Texture2D LoadPhoto(string fileName)
        {
            if (!Exists(fileName)) return null;
            var texture = new Texture2D(2, 2, TextureFormat.RGB24, false);
            if (texture.LoadImage(File.ReadAllBytes(FullPath(fileName)))) return texture;
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        /// <summary>O copie micșorată (dacă latura cea mai mare depășește maxSide), altfel chiar textura primită.</summary>
        public static Texture2D Resize(Texture2D source, int maxSide)
        {
            int w = source.width, h = source.height;
            float k = Mathf.Min(1f, maxSide / (float)Mathf.Max(w, h));
            if (k >= 1f) return source;
            int tw = Mathf.Max(1, Mathf.RoundToInt(w * k));
            int th = Mathf.Max(1, Mathf.RoundToInt(h * k));

            var rt = RenderTexture.GetTemporary(tw, th, 0, RenderTextureFormat.ARGB32);
            var previous = RenderTexture.active;
            Graphics.Blit(source, rt);
            RenderTexture.active = rt;
            var result = new Texture2D(tw, th, TextureFormat.RGB24, false);
            result.ReadPixels(new Rect(0, 0, tw, th), 0, 0);
            result.Apply();
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(rt);
            return result;
        }

        // ---------------------------------------------------------------- voce (WAV, 16 biți, mono)

        public static string SaveWav(float[] samples, int channels, int frequency)
        {
            try
            {
                string name = $"voice-{Guid.NewGuid():N}.wav";
                using (var stream = new FileStream(FullPath(name), FileMode.Create))
                using (var writer = new BinaryWriter(stream))
                {
                    int dataBytes = samples.Length * 2;
                    writer.Write(new[] { 'R', 'I', 'F', 'F' });
                    writer.Write(36 + dataBytes);
                    writer.Write(new[] { 'W', 'A', 'V', 'E' });
                    writer.Write(new[] { 'f', 'm', 't', ' ' });
                    writer.Write(16);                          // mărimea blocului fmt
                    writer.Write((short)1);                    // PCM
                    writer.Write((short)channels);
                    writer.Write(frequency);
                    writer.Write(frequency * channels * 2);    // octeți pe secundă
                    writer.Write((short)(channels * 2));       // octeți pe cadru
                    writer.Write((short)16);                   // biți pe eșantion
                    writer.Write(new[] { 'd', 'a', 't', 'a' });
                    writer.Write(dataBytes);
                    foreach (float s in samples)
                        writer.Write((short)(Mathf.Clamp(s, -1f, 1f) * short.MaxValue));
                }
                return name;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut salva înregistrarea: {e.Message}");
                return null;
            }
        }

        /// <summary>Citește un WAV scris de SaveWav (PCM 16 biți) ca AudioClip.</summary>
        public static AudioClip LoadWav(string fileName)
        {
            if (!Exists(fileName)) return null;
            try
            {
                byte[] bytes = File.ReadAllBytes(FullPath(fileName));
                int channels = BitConverter.ToInt16(bytes, 22);
                int frequency = BitConverter.ToInt32(bytes, 24);

                // Căutăm blocul „data” (de obicei la octetul 36).
                int pos = 12;
                while (pos + 8 <= bytes.Length)
                {
                    string id = System.Text.Encoding.ASCII.GetString(bytes, pos, 4);
                    int size = BitConverter.ToInt32(bytes, pos + 4);
                    if (id == "data")
                    {
                        int count = Mathf.Min(size, bytes.Length - pos - 8) / 2;
                        var samples = new float[count];
                        for (int i = 0; i < count; i++)
                            samples[i] = BitConverter.ToInt16(bytes, pos + 8 + i * 2) / (float)short.MaxValue;
                        var clip = AudioClip.Create(fileName, count / Mathf.Max(1, channels), channels, frequency, false);
                        clip.SetData(samples, 0);
                        return clip;
                    }
                    pos += 8 + size;
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] Nu am putut citi înregistrarea: {e.Message}");
            }
            return null;
        }

        /// <summary>„0:07”, „0:30”.</summary>
        public static string FormatSeconds(float seconds)
        {
            int s = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{s / 60}:{s % 60:00}";
        }
    }
}
