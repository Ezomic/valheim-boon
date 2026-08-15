using System.Collections.Generic;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// Borrowed UI art, so the panel is made of the game's own wood rather than of flat
    /// rectangles.
    ///
    /// The first version drew everything with 1x1 solid textures in Unity's default Arial and
    /// read as exactly what it was. Valheim's windows are nine-sliced carved panels in a Norse
    /// serif; no arrangement of coloured rectangles gets close to that. This is the same
    /// argument as borrowing a material instead of authoring a texture, and the same argument
    /// HudBar makes for cloning a bar rather than drawing one.
    ///
    /// Nothing is loaded from disk. Every sprite and font the game has built is already in
    /// memory, and Resources.FindObjectsOfTypeAll reaches them whether or not anything is
    /// currently showing them - which matters, because the inventory window is inactive most
    /// of the time and its art would otherwise be unreachable.
    ///
    /// Everything here is best-effort. A missing donor leaves the old flat look in place
    /// rather than throwing, the same way an unresolved prefab name is logged and skipped.
    /// </summary>
    internal static class Skin
    {
        internal static Texture2D Panel;
        internal static RectOffset PanelBorder;

        internal static Texture2D Tile;
        internal static RectOffset TileBorder;

        internal static Font Face;

        private static bool _tried;

        /// <summary>Whether anything at all was borrowed, for the panel to decide with.</summary>
        internal static bool HasPanel => Panel != null;
        internal static bool HasTile => Tile != null;

        // Ordered by preference, then a keyword sweep behind them. These names come from other
        // people's mods and from the game's own conventions rather than from anything verified
        // here, which is exactly why Dump exists: the first run says what is really loaded and
        // the list gets corrected from that rather than from guesswork.
        private static readonly string[] PanelNames =
        {
            "woodpanel_trophys", "woodpanel_settings", "woodpanel_password", "woodpanel",
            "panel_dark", "darken_blob",
        };

        private static readonly string[] TileNames =
        {
            "item_background", "inventory_slot", "slot_background", "button_fill", "bkg",
        };

        private static readonly string[] FaceNames =
        {
            "AveriaSerifLibre-Bold", "AveriaSerifLibre-Regular", "Norse", "Norsebold",
        };

        internal static void Ensure()
        {
            if (_tried) return;
            _tried = true;

            Dump();

            Panel = Find(PanelNames, "woodpanel", out PanelBorder);
            Tile = Find(TileNames, "slot", out TileBorder);
            Face = FindFace();

            BoonPlugin.Log.LogInfo("Skin: panel=" + (Panel != null ? Panel.name : "none") +
                                   ", tile=" + (Tile != null ? Tile.name : "none") +
                                   ", font=" + (Face != null ? Face.name : "default"));
        }

        /// <summary>
        /// Name every font and every plausibly useful sprite, once, so the lists above can be
        /// replaced with real names instead of guesses. Behind Verbose because it is a page of
        /// log and is only interesting while the skin is being fitted.
        /// </summary>
        private static void Dump()
        {
            if (!BoonConfig.Verbose.Value) return;

            var fonts = Resources.FindObjectsOfTypeAll<Font>();
            var fontNames = new List<string>();
            foreach (var f in fonts) if (f != null) fontNames.Add(f.name);
            BoonPlugin.Log.LogInfo("Skin: " + fontNames.Count + " fonts loaded: " +
                                   string.Join(", ", fontNames.ToArray()));

            // Sprites run to thousands - every item icon is one - so this is filtered to the
            // words a window is likely to be built from, and capped.
            var wanted = new[] { "panel", "wood", "slot", "frame", "border", "button", "darken", "bkg", "background" };
            var hits = new List<string>();

            foreach (var sprite in Resources.FindObjectsOfTypeAll<Sprite>())
            {
                if (sprite == null || sprite.name == null) continue;

                var lower = sprite.name.ToLowerInvariant();
                var match = false;
                foreach (var word in wanted) if (lower.Contains(word)) { match = true; break; }
                if (!match) continue;

                hits.Add(sprite.name + " " + (int)sprite.rect.width + "x" + (int)sprite.rect.height +
                         (sprite.border == Vector4.zero ? "" : " 9slice") + (sprite.packed ? " packed" : ""));

                if (hits.Count >= 120) break;
            }

            BoonPlugin.Log.LogInfo("Skin: candidate sprites (" + hits.Count + "):\n  " +
                                   string.Join("\n  ", hits.ToArray()));
        }

        private static Texture2D Find(string[] preferred, string keyword, out RectOffset border)
        {
            border = new RectOffset(0, 0, 0, 0);

            var all = Resources.FindObjectsOfTypeAll<Sprite>();

            foreach (var name in preferred)
            {
                foreach (var sprite in all)
                {
                    if (sprite == null || sprite.name != name) continue;

                    var tex = Bake(sprite);
                    if (tex == null) continue;

                    border = BorderOf(sprite);
                    return tex;
                }
            }

            // Nothing named; take the largest nine-sliced sprite whose name carries the
            // keyword. Largest because a window frame is the big one and the small ones with
            // the same word in their name are its pieces.
            Sprite best = null;
            foreach (var sprite in all)
            {
                if (sprite == null || sprite.name == null) continue;
                if (!sprite.name.ToLowerInvariant().Contains(keyword)) continue;
                if (sprite.border == Vector4.zero) continue;

                if (best == null || sprite.rect.width * sprite.rect.height > best.rect.width * best.rect.height)
                    best = sprite;
            }

            if (best == null) return null;

            var baked = Bake(best);
            if (baked == null) return null;

            border = BorderOf(best);
            return baked;
        }

        private static RectOffset BorderOf(Sprite sprite)
        {
            // Sprite.border is (left, bottom, right, top); RectOffset is (left, right, top,
            // bottom). Getting this pair the wrong way round stretches the carved edge across
            // the middle of the panel, which looks like a corrupted texture rather than like a
            // swapped argument.
            var b = sprite.border;
            return new RectOffset((int)b.x, (int)b.z, (int)b.w, (int)b.y);
        }

        /// <summary>
        /// Copy a sprite's own rectangle out of whatever atlas it lives in, into a standalone
        /// texture IMGUI can use.
        ///
        /// Through a RenderTexture rather than GetPixels, because Valheim's UI textures are not
        /// Read/Write enabled and GetPixels would throw on them. A blit with a scale and offset
        /// selects the sprite's region of the sheet, and ReadPixels off the RenderTexture works
        /// regardless of the source's import settings - the same readback devkit uses to rip
        /// textures, and the reason textures are never the part of a rip that fails.
        /// </summary>
        private static Texture2D Bake(Sprite sprite)
        {
            var source = sprite.texture;
            if (source == null) return null;

            var rect = sprite.textureRect;
            var w = Mathf.RoundToInt(rect.width);
            var h = Mathf.RoundToInt(rect.height);
            if (w <= 0 || h <= 0) return null;

            var previous = RenderTexture.active;
            var rt = RenderTexture.GetTemporary(w, h, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);

            try
            {
                var scale = new Vector2(rect.width / source.width, rect.height / source.height);
                var offset = new Vector2(rect.x / source.width, rect.y / source.height);

                Graphics.Blit(source, rt, scale, offset);

                RenderTexture.active = rt;

                var baked = new Texture2D(w, h, TextureFormat.RGBA32, false);
                baked.ReadPixels(new Rect(0f, 0f, w, h), 0, 0);
                baked.Apply();
                baked.name = sprite.name;

                // Point filtering, because the source is a pixel-art panel and the whole point
                // of borrowing it is that it matches. Bilinear would soften the carved edge
                // against everything vanilla draws beside it.
                baked.filterMode = FilterMode.Point;
                baked.wrapMode = TextureWrapMode.Clamp;
                baked.hideFlags = HideFlags.HideAndDontSave;

                return baked;
            }
            catch (System.Exception e)
            {
                BoonPlugin.Log.LogWarning("Skin: could not bake '" + sprite.name + "': " + e.Message);
                return null;
            }
            finally
            {
                RenderTexture.active = previous;
                RenderTexture.ReleaseTemporary(rt);
            }
        }

        /// <summary>
        /// A real Font, not a TMP_FontAsset - IMGUI cannot use the latter. The game may only
        /// ship the TMP versions, in which case this finds nothing and the panel keeps Arial.
        /// That is the single biggest remaining tell, and there is no way around it without
        /// either shipping a font file or rebuilding the panel in Unity UI.
        /// </summary>
        private static Font FindFace()
        {
            var all = Resources.FindObjectsOfTypeAll<Font>();

            foreach (var name in FaceNames)
            {
                foreach (var font in all)
                {
                    if (font != null && font.name == name) return font;
                }
            }

            // Anything that is not the default is still closer than the default.
            foreach (var font in all)
            {
                if (font == null || font.name == null) continue;
                if (font.name.StartsWith("Arial") || font.name.StartsWith("Liberation")) continue;
                return font;
            }

            return null;
        }
    }
}
