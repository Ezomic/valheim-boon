using UnityEngine;
using UnityEngine.UI;

namespace Boon
{
    /// <summary>
    /// A fifth tab on the compendium bar, beside the raven, the valknut, the trophy and the
    /// swords, that opens the boons.
    ///
    /// A thing on screen rather than a key, which is the standing preference here - Stow was
    /// given a buildable post for the same reason and both its keys still exist in config,
    /// unbound. F7 stays bound and keeps working; this is simply where anyone would look for
    /// it first, next to the skills it sits alongside conceptually.
    ///
    /// Cloned from a tab that is already there rather than built, so it arrives with the
    /// game's own button frame, hover tint and click sound. Two things have to be taken off a
    /// clone or it keeps behaving like its donor: the inherited onClick listeners, which would
    /// still open the trophies, and the Localize component, which restores the donor's label
    /// the next time the language changes.
    /// </summary>
    internal static class InfoTab
    {
        private static GameObject _tab;
        private static InventoryGui _seen;
        private static Sprite _icon;
        private static bool _failed;

        internal static void Update()
        {
            if (_failed || !BoonConfig.Enabled.Value || !BoonConfig.ShowInfoTab.Value) return;

            var gui = InventoryGui.instance;
            if (gui == null || gui.m_infoPanel == null)
            {
                _seen = null;
                return;
            }

            // The window is rebuilt with every world, taking our tab with it.
            if (ReferenceEquals(gui, _seen) && _tab != null) return;

            _seen = gui;
            Build(gui);
        }

        private static void Build(InventoryGui gui)
        {
            var donor = Donor(gui);
            if (donor == null)
            {
                _failed = true;
                BoonPlugin.Log.LogWarning("No compendium tab to clone - the boons tab is off. " +
                                          KeyHint() + " still opens them.");
                return;
            }

            var go = Object.Instantiate(donor.gameObject, donor.transform.parent);
            go.name = "BoonTab";

            var button = go.GetComponent<Button>();
            if (button != null)
            {
                // The clone still carries the donor's listeners, which would open the trophies.
                button.onClick.RemoveAllListeners();
                button.onClick.AddListener(Open);
            }

            // Otherwise the next language change puts the donor's label back on our tab.
            foreach (var localize in go.GetComponentsInChildren<Localize>(true)) Object.Destroy(localize);

            Icon(go);

            Place(gui, go, donor);

            _tab = go;
            BoonPlugin.Log.LogInfo("Boons tab added to the compendium bar.");
        }

        /// <summary>
        /// The rightmost existing tab, so the new one goes on the end rather than into the
        /// middle of a row that has an order to it.
        /// </summary>
        private static Button Donor(InventoryGui gui)
        {
            Button best = null;
            var bestX = float.MinValue;

            foreach (var button in gui.m_infoPanel.GetComponentsInChildren<Button>(true))
            {
                if (button == null || button.name == "BoonTab") continue;

                var rect = button.GetComponent<RectTransform>();
                if (rect == null) continue;

                if (rect.anchoredPosition.x > bestX)
                {
                    bestX = rect.anchoredPosition.x;
                    best = button;
                }
            }

            return best;
        }

        /// <summary>
        /// Put the new tab after the last one.
        ///
        /// The first attempt always offset by hand and landed the rune on top of the swords.
        /// A bar laid out by a LayoutGroup gives every child the same anchoredPosition - the
        /// group decides where they go - so the measured gap between two tabs came out at
        /// zero and the clone sat exactly on its donor. When a group is present the right
        /// answer is to set nothing and let it place the fifth child itself.
        /// </summary>
        private static void Place(InventoryGui gui, GameObject tab, Button donor)
        {
            if (gui.m_infoPanel.GetComponent<LayoutGroup>() != null)
            {
                BoonPlugin.Log.LogInfo("Boons tab placed by the bar's own layout group.");
                return;
            }

            var rect = tab.GetComponent<RectTransform>();
            var donorRect = donor.GetComponent<RectTransform>();
            if (rect == null || donorRect == null) return;

            var gap = Spacing(gui);

            // Distinct positions, not just two of them: if every tab reports the same x then
            // nothing here is placing them and a measured gap is meaningless. The donor's own
            // width is the honest fallback.
            if (gap < 1f) gap = donorRect.rect.width * 1.15f;

            rect.anchoredPosition = donorRect.anchoredPosition + new Vector2(gap, 0f);
            BoonPlugin.Log.LogInfo("Boons tab placed " + (int)gap + "px after the last one.");
        }

        private static float Spacing(InventoryGui gui)
        {
            var xs = new System.Collections.Generic.List<float>();

            foreach (var button in gui.m_infoPanel.GetComponentsInChildren<Button>(true))
            {
                if (button == null || button.name == "BoonTab") continue;

                var rect = button.GetComponent<RectTransform>();
                if (rect == null) continue;

                var x = rect.anchoredPosition.x;
                if (!xs.Contains(x)) xs.Add(x);
            }

            if (xs.Count < 2) return 0f;

            xs.Sort();
            return xs[xs.Count - 1] - xs[xs.Count - 2];
        }

        /// <summary>
        /// A runestone for an icon, made from the same texture the panel draws its stones with
        /// - so the tab shows the thing it opens rather than a borrowed glyph that means
        /// something else.
        /// </summary>
        private static void Icon(GameObject tab)
        {
            if (_icon == null)
            {
                var tex = Algiz();
                _icon = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _icon.name = "BoonAlgiz";
            }

            // The *last* child Image, not the first. A tab is a background blob with its glyph
            // drawn over the top, and taking the first one turned the blob into a stone and
            // left the donor's trophy sitting on it. Later in the hierarchy is drawn on top,
            // which is the glyph by definition.
            var button = tab.GetComponent<Button>();
            Image glyph = null;

            foreach (var image in tab.GetComponentsInChildren<Image>(true))
            {
                if (image == null) continue;
                if (button != null && image.gameObject == button.gameObject) continue;

                glyph = image;
            }

            // Colour is left alone on purpose: the donor's gold is what makes this match the
            // raven and the trophy, and the silhouette is white so it takes that tint.
            if (glyph != null) glyph.sprite = _icon;
        }

        /// <summary>
        /// Algiz, drawn as three strokes.
        ///
        /// Strokes rather than a character in the rune font, for the reason a screen of tofu
        /// squares already taught once: a font may or may not carry the glyph you ask it for,
        /// and an Image needs a Sprite regardless. Three line segments have neither problem
        /// and stay crisp at whatever size the bar happens to use.
        ///
        /// White, so the donor tab's own colour tints it - which is what makes it the same
        /// gold as the raven and the trophy without that gold being written here.
        /// </summary>
        private static Texture2D Algiz()
        {
            const int size = 128;

            // Normalised, y counted downward the way the shape was drawn and judged. Stem,
            // then the two arms rising from its middle.
            var strokes = new[]
            {
                new[] { 0.50f, 0.88f, 0.50f, 0.14f },
                new[] { 0.50f, 0.50f, 0.18f, 0.16f },
                new[] { 0.50f, 0.50f, 0.82f, 0.16f },
            };

            var half = size * 0.045f;
            var tex = new Texture2D(size, size, TextureFormat.RGBA32, false)
            {
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.HideAndDontSave,
            };

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    // SetPixel counts y from the bottom; the strokes above are written top
                    // down, so the row is flipped here rather than in the numbers.
                    var p = new Vector2(x + 0.5f, size - 1 - y + 0.5f);

                    var nearest = float.MaxValue;
                    foreach (var s in strokes)
                    {
                        var d = Distance(p, new Vector2(s[0] * size, s[1] * size),
                                            new Vector2(s[2] * size, s[3] * size));
                        if (d < nearest) nearest = d;
                    }

                    // Distance to the nearest segment gives round caps and joins for free,
                    // which is what the drawn version had.
                    tex.SetPixel(x, y, new Color(1f, 1f, 1f, Mathf.Clamp01(half - nearest + 0.5f)));
                }
            }

            tex.Apply();
            return tex;
        }

        private static float Distance(Vector2 p, Vector2 a, Vector2 b)
        {
            var ab = b - a;
            var t = Mathf.Clamp01(Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, ab.sqrMagnitude));
            return Vector2.Distance(p, a + ab * t);
        }

        private static void Open()
        {
            // The panel is full screen and hides itself while the inventory is up, so the
            // inventory has to go first or the click would appear to do nothing.
            if (InventoryGui.instance != null) InventoryGui.instance.Hide();
            BoonPanel.Open();
        }

        private static string KeyHint()
        {
            return BoonPanel.KeyName();
        }
    }
}
