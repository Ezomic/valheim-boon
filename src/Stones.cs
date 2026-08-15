using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The runestone discs, generated rather than borrowed.
    ///
    /// Authoring a texture is normally the wrong answer in this repo - the game has art and
    /// borrowing it matches by construction. It is right here for one reason: a shaded circle
    /// is a *shape*, not a material. There is nothing in the game to match it against, and
    /// generating it sidesteps the whole problem that cost three attempts on the wooden panel,
    /// because pixels written here are drawn in the same colour space they were written in.
    ///
    /// Two stones and a rim, built once and kept for the session. A stone is 128px and drawn
    /// at 108, so it is always downsampled rather than stretched.
    /// </summary>
    internal static class Stones
    {
        private const int Size = 128;

        internal static Texture2D Carved;   // worked granite, a boon that is held
        internal static Texture2D Raw;      // dark and unworked, nothing taken yet
        internal static Texture2D Rim;      // the gold ring on a fully carved stone
        internal static Texture2D Halo;     // a soft lift under the stone the cursor is on

        private static bool _built;

        internal static void Ensure()
        {
            if (_built) return;
            _built = true;

            // Pulled further apart after seeing them side by side: at the first values a
            // carved stone and a raw one read as the same grey, which lost the whole point
            // of a field you can scan for what you already hold.
            Carved = Disc(new Color(0.596f, 0.573f, 0.533f),
                          new Color(0.451f, 0.431f, 0.400f),
                          new Color(0.204f, 0.192f, 0.173f));

            Raw = Disc(new Color(0.220f, 0.212f, 0.200f),
                       new Color(0.176f, 0.169f, 0.157f),
                       new Color(0.106f, 0.102f, 0.094f));

            Rim = Ring(new Color(0.83f, 0.663f, 0.29f, 1f));
            Halo = Ring(new Color(0.83f, 0.663f, 0.29f, 0.45f), 3f);
        }

        /// <summary>
        /// A stone lit from the upper left, darkening to the rim, with a little noise so it
        /// does not read as a gradient.
        ///
        /// Written top-down: SetPixel counts y from the bottom and GUI.DrawTexture draws from
        /// the top, so the light would otherwise come from below and every stone would look
        /// like a hole rather than a boulder.
        /// </summary>
        private static Texture2D Disc(Color hi, Color mid, Color lo)
        {
            var tex = New();

            var radius = Size * 0.5f - 1f;
            var centre = new Vector2(Size * 0.5f, Size * 0.5f);
            var light = new Vector2(Size * 0.34f, Size * 0.30f);

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var p = new Vector2(x + 0.5f, Size - 1 - y + 0.5f);
                    var d = Vector2.Distance(p, centre);

                    if (d > radius + 1f)
                    {
                        tex.SetPixel(x, y, Color.clear);
                        continue;
                    }

                    var lit = Mathf.Clamp01(Vector2.Distance(p, light) / (radius * 1.55f));
                    var colour = Color.Lerp(hi, mid, lit);

                    var toRim = Mathf.Clamp01(d / radius);
                    colour = Color.Lerp(colour, lo, toRim * toRim);

                    // Seeded by position, so the grain is identical every run and the stone
                    // does not shimmer between sessions.
                    var grain = (Mathf.PerlinNoise(x * 0.09f, y * 0.09f) - 0.5f) * 0.07f;
                    colour.r = Mathf.Clamp01(colour.r + grain);
                    colour.g = Mathf.Clamp01(colour.g + grain);
                    colour.b = Mathf.Clamp01(colour.b + grain);

                    colour.a = Mathf.Clamp01((radius - d) * 1.4f);
                    tex.SetPixel(x, y, colour);
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D Ring(Color colour, float thickness = 2f)
        {
            var tex = New();

            var radius = Size * 0.5f - 1f;
            var centre = new Vector2(Size * 0.5f, Size * 0.5f);

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    var d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), centre);
                    var edge = Mathf.Abs(d - (radius - thickness * 0.5f));

                    var a = Mathf.Clamp01((thickness - edge) / thickness);
                    tex.SetPixel(x, y, new Color(colour.r, colour.g, colour.b, colour.a * a));
                }
            }

            tex.Apply();
            return tex;
        }

        private static Texture2D New()
        {
            return new Texture2D(Size, Size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };
        }
    }
}
