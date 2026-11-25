using System.Collections.Generic;
using UnityEngine;

namespace ThreeTimes.Past
{
    /// <summary>
    /// „Developarea” clădirilor din Trecut: pune _Reveal (0..1) pe toate materialele de sub un obiect, prin blocuri de
    /// proprietăți (păstrează culorile puse deja pe fiecare material, de exemplu la palate). Shaderele ThreeTimes/PastLit
    /// și ThreeTimes/SkyMask fac clădirea să apară din granulație; celelalte ignoră valoarea.
    /// </summary>
    public static class PastReveal
    {
        static readonly MaterialPropertyBlock Block = new MaterialPropertyBlock();
        static readonly List<Renderer> Renderers = new List<Renderer>();
        static readonly int RevealId = Shader.PropertyToID("_Reveal");
        static readonly int HeightId = Shader.PropertyToID("_RevealHeight");

        /// <param name="height">Înălțimea clădirii în coordonatele obiectului (m), ca să se developeze de jos în sus; 0 = n-o schimba.</param>
        public static void Apply(GameObject root, float reveal, float height = 0f)
        {
            if (root == null) return;
            root.GetComponentsInChildren(true, Renderers);
            foreach (var r in Renderers)
            {
                int count = r.sharedMaterials.Length;
                for (int i = 0; i < count; i++)
                {
                    r.GetPropertyBlock(Block, i);
                    Block.SetFloat(RevealId, reveal);
                    if (height > 0f) Block.SetFloat(HeightId, height);
                    r.SetPropertyBlock(Block, i);
                }
            }
            Renderers.Clear();
        }

        /// <summary>Curba developării: pornește încet (granulație rară), apoi se umple.</summary>
        public static float Ease(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }
    }
}
