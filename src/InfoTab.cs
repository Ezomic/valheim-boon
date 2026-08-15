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

            // Placed after the last tab by hand, because the bar is laid out by hand: the four
            // tabs sit at fixed positions rather than in a layout group, so a clone would
            // otherwise land exactly on top of its donor.
            var rect = go.GetComponent<RectTransform>();
            var donorRect = donor.GetComponent<RectTransform>();
            if (rect != null && donorRect != null)
                rect.anchoredPosition = donorRect.anchoredPosition + new Vector2(Spacing(gui), 0f);

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
        /// The gap between two existing tabs, so the fifth one is spaced like the other four
        /// rather than by a number invented here.
        /// </summary>
        private static float Spacing(InventoryGui gui)
        {
            var xs = new System.Collections.Generic.List<float>();

            foreach (var button in gui.m_infoPanel.GetComponentsInChildren<Button>(true))
            {
                if (button == null || button.name == "BoonTab") continue;

                var rect = button.GetComponent<RectTransform>();
                if (rect != null) xs.Add(rect.anchoredPosition.x);
            }

            if (xs.Count < 2) return 64f;

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
            Stones.Ensure();
            if (Stones.Carved == null) return;

            if (_icon == null)
            {
                _icon = Sprite.Create(Stones.Carved,
                                      new Rect(0f, 0f, Stones.Carved.width, Stones.Carved.height),
                                      new Vector2(0.5f, 0.5f));
                _icon.name = "BoonStone";
            }

            // The glyph is a child Image, not the button's own background - swapping the wrong
            // one replaces the frame and the tab disappears. The child is the one that is not
            // on the object carrying the Button.
            var button = tab.GetComponent<Button>();

            foreach (var image in tab.GetComponentsInChildren<Image>(true))
            {
                if (image == null) continue;
                if (button != null && image.gameObject == button.gameObject) continue;

                image.sprite = _icon;
                image.color = new Color(0.83f, 0.75f, 0.6f, 1f);
                break;
            }
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
