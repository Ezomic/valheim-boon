using UnityEngine;
using UnityEngine.UI;

namespace Boon
{
    /// <summary>
    /// A fifth tab on the compendium bar, beside the raven, the valknut, the trophy and the
    /// swords, that opens the boons.
    ///
    /// A thing on screen rather than a key, which is the standing preference here - Stow was
    /// given a buildable post for the same reason. There was a keybind once and it is gone
    /// rather than merely unbound, so this tab is the only way in; the failure path below says
    /// so out loud, because a tab that will not clone would otherwise strand the panel.
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
                BoonPlugin.Log.LogWarning("No compendium tab to clone, and there is no keybind " +
                                          "any more - the boons panel has no way in. That is a " +
                                          "bug rather than a setting; please report it.");
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

            Icon(go, donor);

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
        /// Make room for a fifth icon rather than dropping it on top of a fourth.
        ///
        /// This went wrong for ten attempts because the bar was read through Buttons, and the
        /// PvP control is a Toggle. Three buttons - Texts, Skills, Trophies - at -162, -52 and
        /// 58, so 168 looked like the empty place after the last one. It is where PvP sits.
        ///
        /// Everything clickable is collected now, Button and Toggle alike, both being
        /// Selectable. The five are then spread evenly about the centre at the pitch the
        /// existing four already use, which is what actually makes space: at 110 apart, five
        /// span 440 of the panel's 570 and the outermost sits well inside its edge, so the
        /// wooden bar itself needs no stretching.
        /// </summary>
        private static void Place(InventoryGui gui, GameObject tab, Button donor)
        {
            var ours = tab.GetComponent<RectTransform>();
            if (ours == null) return;

            var icons = new System.Collections.Generic.List<RectTransform>();

            foreach (var selectable in gui.m_infoPanel.GetComponentsInChildren<Selectable>(true))
            {
                if (selectable == null) continue;

                var rect = selectable.GetComponent<RectTransform>();
                if (rect == null || rect == ours) continue;
                if (icons.Contains(rect)) continue;

                icons.Add(rect);
            }

            if (icons.Count == 0) return;

            icons.Sort((a, b) => a.anchoredPosition.x.CompareTo(b.anchoredPosition.x));

            // Ours goes on the end rather than into a row that has an order to it.
            icons.Add(ours);

            var pitch = Pitch(icons);
            var y = icons[0].anchoredPosition.y;
            var first = -pitch * (icons.Count - 1) * 0.5f;

            for (var i = 0; i < icons.Count; i++)
                icons[i].anchoredPosition = new Vector2(first + i * pitch, y);

            BoonPlugin.Log.LogInfo("Compendium bar re-spaced: " + icons.Count +
                                   " icons at " + (int)pitch + "px.");
        }

        /// <summary>
        /// The gap the bar already uses, measured between two of its own icons rather than
        /// invented. Falls back to a tab width and a bit when there is only one to measure.
        /// </summary>
        private static float Pitch(System.Collections.Generic.List<RectTransform> icons)
        {
            for (var i = 1; i < icons.Count; i++)
            {
                var gap = icons[i].anchoredPosition.x - icons[i - 1].anchoredPosition.x;
                if (gap > 1f) return gap;
            }

            return icons[0].rect.width * 1.15f;
        }

        /// <summary>
        /// A runestone for an icon, made from the same texture the panel draws its stones with
        /// - so the tab shows the thing it opens rather than a borrowed glyph that means
        /// something else.
        /// </summary>
        private static void Icon(GameObject tab, Button donor)
        {
            if (_icon == null)
            {
                var tex = Algiz();
                _icon = Sprite.Create(tex, new Rect(0f, 0f, tex.width, tex.height), new Vector2(0.5f, 0.5f));
                _icon.name = "BoonAlgiz";
            }

            // Swap one sprite. Nothing else.
            //
            // Every attempt before this built a layer of our own and guessed at the rest. The
            // log finally said what a tab on this bar actually is:
            //
            //   'Background'  104x104  black at 53%, sprite point3    the soft shadow plate
            //   'Image'        64x64   white,        sprite trophies  the glyph, as a mask
            //   button normal RGBA(1, 0.718, 0.36)                    the gold
            //
            // Three facts that between them explain every wrong version of this icon. The gold
            // is not inside the sprites and not on any Image.color - it is the button's
            // normalColor, applied to whichever graphic is the targetGraphic, and that graphic
            // is the glyph. So sampling "the donor's colour" could never find it, and stating
            // it by hand landed close but not equal. Sizing to the largest child got the 104px
            // shadow plate rather than the 64px glyph, which is why the rune stood taller than
            // its neighbours. And turning every inherited image off to be safe took the shadow
            // with it, which every other icon on that bar has.
            //
            // So there is nothing to build and nothing to state. Replace the sprite on the
            // graphic the button already tints, and size, shadow, gold, hover and press are all
            // inherited by construction - which is what "cloned from a tab already there" was
            // supposed to mean in the first place.
            var button = tab.GetComponent<Button>();
            var glyph = button != null ? button.targetGraphic as Image : null;

            // The targetGraphic is the glyph on every tab here. The fallback exists for a game
            // update that rearranges them: anything but the shadow plate, smallest first.
            if (glyph == null)
            {
                foreach (var image in tab.GetComponentsInChildren<Image>(true))
                {
                    if (image == null || image.gameObject == tab) continue;
                    if (image.name == "Background") continue;
                    if (glyph == null || image.rectTransform.rect.width < glyph.rectTransform.rect.width)
                        glyph = image;
                }

                BoonPlugin.Log.LogWarning("The tab's target graphic was not an Image; fell back to '" +
                                          (glyph != null ? glyph.name : "nothing") + "'.");
            }

            if (glyph == null)
            {
                BoonPlugin.Log.LogWarning("No glyph layer on the cloned tab - it will still open " +
                                          "the boons, wearing the donor's picture.");
                return;
            }

            glyph.sprite = _icon;
            glyph.color = Color.white;   // The gold comes from the button, exactly as vanilla's does.
            glyph.preserveAspect = true;
            glyph.enabled = true;
            glyph.raycastTarget = true;

            // Behind Verbose now. It ran ungated while the icon was being got right, because
            // ten attempts had gone by with nobody knowing what the layers were - and it is
            // what finally ended that, so it stays rather than being deleted. Turn Verbose on
            // and the next question about this bar is answered in numbers.
            if (!BoonConfig.Verbose.Value) return;

            foreach (var image in donor.GetComponentsInChildren<Image>(true))
            {
                if (image == null) continue;
                BoonPlugin.Log.LogInfo("  donor layer '" + image.name + "' " +
                                       (image.enabled ? "on" : "off") + " " +
                                       image.rectTransform.rect.size + " colour " + image.color +
                                       " sprite " + (image.sprite != null ? image.sprite.name : "none"));
            }

            BoonPlugin.Log.LogInfo("  donor '" + donor.name + "' normal " + donor.colors.normalColor +
                                   ", highlighted " + donor.colors.highlightedColor +
                                   ", target graphic " +
                                   (donor.targetGraphic != null ? donor.targetGraphic.name : "none"));

            BoonPlugin.Log.LogInfo("Boons tab: rune on '" + glyph.name + "' at " +
                                   glyph.rectTransform.rect.size + ", gold from the button.");
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
            //
            // Drawn to fill the box, because the glyph it sits in is 64x64 and the vanilla
            // marks on that bar - the valknut, the swords - run very nearly edge to edge.
            // The earlier numbers left a tenth of the box empty all round, which was invisible
            // while the rune was wrongly being drawn at the 104px shadow plate's size and
            // would read as a small rune now that it is not.
            var strokes = new[]
            {
                new[] { 0.50f, 0.94f, 0.50f, 0.08f },
                new[] { 0.50f, 0.52f, 0.10f, 0.10f },
                new[] { 0.50f, 0.52f, 0.90f, 0.10f },
            };

            // Stroke weight matched to the valknut beside it rather than picked. Half-width,
            // so the drawn line is 9% of the icon.
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
    }
}
