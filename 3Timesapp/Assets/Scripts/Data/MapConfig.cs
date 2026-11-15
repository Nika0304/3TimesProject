using System;
using UnityEngine;

namespace ThreeTimes.Data
{
    /// <summary>
    /// Setările hărții: cheia CARTO și stilul. Se citesc din Assets/Resources/MapConfig.json, care NU intră
    /// în repo (ca și cheia ARCore). Modelul e în 3Timesapp/MapConfig.example.json.
    /// </summary>
    [Serializable]
    public class MapConfig
    {
        public string cartoKey = "";
        public string style = "light_all"; // light_all = Positron cu etichete

        static MapConfig cached;

        public bool HasKey => !string.IsNullOrWhiteSpace(cartoKey);

        public string TileUrl(int z, int x, int y) =>
            $"https://basemaps.cartocdn.com/rastertiles/{style}/{z}/{x}/{y}@2x.png?key={cartoKey.Trim()}";

        public static MapConfig Load()
        {
            if (cached != null) return cached;
            var asset = Resources.Load<TextAsset>("MapConfig");
            try
            {
                cached = asset != null ? JsonUtility.FromJson<MapConfig>(asset.text) : new MapConfig();
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[3Times] MapConfig.json nu e valid: {e.Message}");
                cached = new MapConfig();
            }
            if (!cached.HasKey) Debug.LogWarning("[3Times] Lipsește cheia CARTO (Assets/Resources/MapConfig.json)");
            return cached ?? new MapConfig();
        }
    }
}
