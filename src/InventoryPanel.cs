using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace Boon
{
    /// <summary>
    /// Grows the inventory window's wooden backdrop to cover the rows Deep pack adds.
    ///
    /// Adding a row is two ints on Inventory and InventoryGrid rebuilds itself from them, so
    /// the slots appear with no UI work at all - which is what made the card cheap. What does
    /// not follow is the window behind them: vanilla's inventory is always four rows, so
    /// nothing in the game has any reason to resize that panel, and the extra slots sat below
    /// the wood on bare screen.
    ///
    /// The pitch is not a guess. InventoryGrid lays its elements out at i * -m_elementSpace,
    /// so one row is exactly m_elementSpace tall and the backdrop needs that much more per
    /// extra row.
    ///
    /// Heights are captured once per InventoryGui and re-applied from that baseline rather
    /// than added to, for the same reason the row count itself is measured from vanilla's own
    /// height: anything that compounds grows a little more every time the window is rebuilt.
    /// </summary>
    internal static class InventoryPanel
    {
        private static InventoryGui _seen;
        private static int _applied = -1;

        private static readonly List<RectTransform> _panels = new List<RectTransform>();
        private static readonly List<float> _baseHeights = new List<float>();

        internal static void Update()
        {
            var gui = InventoryGui.instance;
            if (gui == null || gui.m_player == null)
            {
                _seen = null;
                return;
            }

            // The window is rebuilt with every world, and the baseline goes with it.
            if (!ReferenceEquals(gui, _seen))
            {
                _seen = gui;
                _applied = -1;
                Capture(gui);
            }

            var extra = Effects.ExtraRows;
            if (extra == _applied) return;

            _applied = extra;
            Resize(gui, extra);
        }

        private static void Capture(InventoryGui gui)
        {
            _panels.Clear();
            _baseHeights.Clear();

            Remember(gui.m_player);

            // The wood is a child Image rather than the root, and it is not always the same
            // child, so it is found by the sprite it draws. Anything else in there - the
            // grid, the weight readout - must not be stretched with it.
            foreach (var image in gui.m_player.GetComponentsInChildren<Image>(true))
            {
                if (image == null || image.sprite == null) continue;
                if (image.sprite.name.IndexOf("woodpanel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;

                Remember(image.rectTransform);
            }

            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo("Inventory backdrop: " + _panels.Count + " piece(s) to grow.");
        }

        private static void Remember(RectTransform rect)
        {
            if (rect == null || _panels.Contains(rect)) return;

            _panels.Add(rect);
            _baseHeights.Add(rect.rect.height);
        }

        private static void Resize(InventoryGui gui, int extra)
        {
            var pitch = PitchOf(gui);
            if (pitch <= 0f) return;

            for (var i = 0; i < _panels.Count; i++)
            {
                if (_panels[i] == null) continue;

                _panels[i].SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical,
                                                     _baseHeights[i] + Mathf.Max(0, extra) * pitch);
            }

            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo("Inventory backdrop grown by " + extra + " row(s) at " + pitch + "px.");
        }

        /// <summary>
        /// One row's height, read off the grid that draws the rows rather than measured from
        /// the panel. m_elementSpace is public and is the exact number the layout uses.
        /// </summary>
        private static float PitchOf(InventoryGui gui)
        {
            var grid = gui.m_player.GetComponentInChildren<InventoryGrid>(true);
            return grid != null ? grid.m_elementSpace : 0f;
        }
    }
}
