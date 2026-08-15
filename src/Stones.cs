using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The runestone discs, generated rather than borrowed.
    ///
    /// Authoring a texture is normally the wrong answer in this repo - the game has art and
    /// borrowing it matches by construction. It is right here for one reason: a shaded circle
    /// is a *shape*, not a material. There is nothing in the game to match it against, and
    /// generating it sidesteps the problem that cost three attempts on the wooden panel,
    /// because pixels written here are drawn in the space they were written in.
    ///
    /// Built to the mockup's own numbers rather than by eye. The first version added Perlin
    /// grain to keep it from reading as a gradient, and at 0.09 frequency downsampled from
    /// 128 to 100 it dimpled - twenty-five golf balls. The mockup has no noise in it at all:
    /// a stone is four gradient stops from a point up and left of centre, a shadow along the
    /// bottom of the inner rim, a faint highlight along the top of it, and a soft shadow
    /// underneath. That is the whole recipe, and smoothness is most of what makes it read as
    /// stone rather than as a ball.
    /// </summary>
    internal static class Stones
    {
        private const int Size = 128;

        // A margin inside the texture for the drop shadow to live in, so the stone can cast
        // one without being clipped by its own bounds.
        private const float Margin = 7f;

        internal static Texture2D Carved;   // worked granite, a boon that is held
        internal static Texture2D Raw;      // dark and unworked, nothing taken yet
        internal static Texture2D Rim;      // the gold ring on a fully carved stone
        internal static Texture2D Halo;     // a soft lift under the stone the cursor is on

        private static bool _built;

        // The mockup's stops, in order, as (position, colour). CSS reads them as percentages
        // of the distance from the gradient's origin to the farthest corner.
        private static readonly float[] CarvedStops = { 0f, 0.44f, 0.78f, 1f };
        private static readonly Color[] CarvedColours =
        {
            Hex(0x7C, 0x77, 0x6F), Hex(0x60, 0x5C, 0x55), Hex(0x42, 0x3F, 0x3A), Hex(0x32, 0x2F, 0x2A),
        };

        private static readonly float[] RawStops = { 0f, 0.46f, 1f };
        private static readonly Color[] RawColours =
        {
            Hex(0x45, 0x42, 0x3D), Hex(0x3A, 0x37, 0x33), Hex(0x2E, 0x2C, 0x28),
        };

        internal static void Ensure()
        {
            if (_built) return;
            _built = true;

            Carved = Disc(CarvedStops, CarvedColours, 0.55f, 0.10f);
            Raw = Disc(RawStops, RawColours, 0.60f, 0f);

            Rim = Ring(new Color(0.83f, 0.663f, 0.29f, 1f));
            Halo = Ring(new Color(0.83f, 0.663f, 0.29f, 0.45f), 3f);
        }

        /// <summary>
        /// One stone.
        ///
        /// <paramref name="bottomShade"/> and <paramref name="topLight"/> are the two inset
        /// shadows from the mockup: a dark band along the lower inside edge that gives the
        /// stone its weight, and a thin light one along the upper edge that reads as the lit
        /// face. Both fade over the outer fifth of the radius, which is what an inset shadow
        /// with a ten pixel blur does on a hundred pixel circle.
        /// </summary>
        private static Texture2D Disc(float[] stops, Color[] colours, float bottomShade, float topLight)
        {
            var tex = New();

            var radius = Size * 0.5f - Margin;
            var centre = new Vector2(Size * 0.5f, Size * 0.5f);

            // Up and to the left, as the mockup's "circle at 34% 28%". Written in screen terms
            // - y counted from the top - because that is how the result is drawn.
            var origin = new Vector2(Size * 0.34f, Size * 0.28f);

            // The far end of the gradient is the far edge of the *stone*, not of the texture
            // it is drawn into. Measuring to the texture corner was why the first build came
            // out pale: the disc only reaches about three quarters of that distance, so the
            // two darkest stops fell outside the circle and were never drawn, and the stone
            // topped out near #46433D instead of #322F2A.
            var far = Vector2.Distance(origin, centre) + radius;

            for (var y = 0; y < Size; y++)
            {
                for (var x = 0; x < Size; x++)
                {
                    // SetPixel counts y from the bottom and GUI.DrawTexture draws from the
                    // top, so every calculation here is done in flipped space and written
                    // back the other way up. Without this the light comes from below and a
                    // stone reads as a hole.
                    var p = new Vector2(x + 0.5f, Size - 1 - y + 0.5f);
                    var d = Vector2.Distance(p, centre);

                    var colour = Sample(stops, colours, Mathf.Clamp01(Vector2.Distance(p, origin) / far));

                    var intoRim = Mathf.Clamp01((radius - d) / (radius * 0.2f));
                    var vertical = (p.y - centre.y) / radius;

                    if (bottomShade > 0f)
                        colour = Color.Lerp(colour, Color.black,
                                            Mathf.Clamp01(vertical) * (1f - intoRim) * bottomShade);

                    if (topLight > 0f)
                        colour = Color.Lerp(colour, Color.white,
                                            Mathf.Clamp01(-vertical) * (1f - intoRim) * topLight);

                    // The stone itself, antialiased over the last pixel of its edge.
                    var stone = Mathf.Clamp01(radius - d);

                    // A soft shadow under it, offset down the way the mockup's is.
                    var shadow = Mathf.Clamp01(1f - (Vector2.Distance(p, centre + new Vector2(0f, -3f)) - radius) / 6f);
                    shadow = Mathf.Clamp01(shadow) * 0.5f * (1f - stone);

                    colour.a = stone;
                    if (stone < 1f) colour = Color.Lerp(new Color(0f, 0f, 0f, shadow), colour, stone);

                    tex.SetPixel(x, y, colour);
                }
            }

            tex.Apply();
            return tex;
        }

        /// <summary>Multi-stop gradient sampling, the way a CSS gradient interpolates.</summary>
        private static Color Sample(float[] stops, Color[] colours, float t)
        {
            for (var i = 1; i < stops.Length; i++)
            {
                if (t > stops[i]) continue;

                var span = stops[i] - stops[i - 1];
                var k = span <= 0f ? 0f : (t - stops[i - 1]) / span;
                return Color.Lerp(colours[i - 1], colours[i], k);
            }

            return colours[colours.Length - 1];
        }

        private static Texture2D Ring(Color colour, float thickness = 2f)
        {
            var tex = New();

            var radius = Size * 0.5f - Margin;
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

        private static Color Hex(int r, int g, int b)
        {
            return new Color(r / 255f, g / 255f, b / 255f, 1f);
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
