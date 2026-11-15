using UnityEngine;
using UnityEngine.UIElements;

namespace ThreeTimes.UI
{
    /// <summary>
    /// Fundalul care arată tipul unei urme în interfață (urmele simulate, previzualizarea din foaie):
    /// polaroid pentru poză, microfon pentru voce, alb și puțin transparent, în spatele formei.
    /// Aceleași proporții ca în AR (TraceBackdropMesh), în multipli ai mărimii formei.
    /// </summary>
    public static class TraceTypeBackdrop
    {
        static readonly Color Paper = new Color(1f, 1f, 1f, 0.6f);

        /// <summary>
        /// Un element de mărimea formei (size × size), poziționat absolut, de pus ÎNAINTE de formă
        /// în același părinte. Pentru text întoarce null.
        /// </summary>
        public static VisualElement Create(string traceType, Color tint, float size)
        {
            if (traceType != "PHOTO" && traceType != "VOICE") return null;

            var root = Box(0f, 0f, size, size);

            if (traceType == "PHOTO")
            {
                // Rama: de la 0,62 D deasupra centrului la 0,78 D dedesubt, lată de 1,24 D.
                var frame = Box(-0.12f * size, -0.12f * size, 1.24f * size, 1.40f * size);
                frame.style.backgroundColor = Paper;
                Radius(frame, 0.04f * size);
                frame.style.rotate = new Rotate(-5f);
                var photo = Box(0.10f * size, 0.10f * size, 1.04f * size, 0.97f * size);
                tint.a = 0.3f;
                photo.style.backgroundColor = tint;
                Radius(photo, 0.02f * size);
                frame.Add(photo);
                root.Add(frame);
            }
            else
            {
                // Coordonatele din AR (Y în sus, centrul formei în 0) trecute în coordonate de ecran.
                float Y(float up) => (0.5f - up) * size;
                float X(float right) => (0.5f + right) * size;

                // Suportul în U: o cutie cu margini doar jos și pe laterale, colțurile de jos rotunjite.
                var holder = Box(X(-0.405f), Y(0.05f), 0.81f * size, 0.405f * size);
                float border = 0.05f * size;
                holder.style.borderLeftWidth = border;
                holder.style.borderRightWidth = border;
                holder.style.borderBottomWidth = border;
                holder.style.borderLeftColor = Paper;
                holder.style.borderRightColor = Paper;
                holder.style.borderBottomColor = Paper;
                holder.style.borderBottomLeftRadius = 0.405f * size;
                holder.style.borderBottomRightRadius = 0.405f * size;
                root.Add(holder);

                var head = Box(X(-0.25f), Y(0.51f), 0.5f * size, 0.78f * size);
                head.style.backgroundColor = Paper;
                Radius(head, 0.25f * size);
                root.Add(head);

                tint.a = 0.45f;
                for (int i = 0; i < 3; i++)
                {
                    float y = 0.30f - i * 0.13f;
                    var line = Box(X(-0.13f), Y(y + 0.012f), 0.26f * size, 0.024f * size);
                    line.style.backgroundColor = tint;
                    root.Add(line);
                }

                var stem = Box(X(-0.025f), Y(-0.355f), 0.05f * size, 0.285f * size);
                stem.style.backgroundColor = Paper;
                root.Add(stem);

                var foot = Box(X(-0.2f), Y(-0.64f), 0.4f * size, 0.05f * size);
                foot.style.backgroundColor = Paper;
                Radius(foot, 0.025f * size);
                root.Add(foot);
            }
            return root;
        }

        /// <summary>Cât iese fundalul sub formă (px), ca eticheta de sub ea să nu se suprapună.</summary>
        public static float ExtraBelow(string traceType, float size) =>
            traceType == "PHOTO" ? 0.32f * size : traceType == "VOICE" ? 0.2f * size : 0f;

        static VisualElement Box(float left, float top, float width, float height)
        {
            var e = new VisualElement { pickingMode = PickingMode.Ignore };
            e.style.position = Position.Absolute;
            e.style.left = left;
            e.style.top = top;
            e.style.width = width;
            e.style.height = height;
            return e;
        }

        static void Radius(VisualElement e, float r)
        {
            e.style.borderTopLeftRadius = r;
            e.style.borderTopRightRadius = r;
            e.style.borderBottomLeftRadius = r;
            e.style.borderBottomRightRadius = r;
        }
    }
}
