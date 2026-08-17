using System.Collections.Generic;
using HarmonyLib;
using UnityEngine;

namespace Rist
{
    /// <summary>
    /// Rist's own inventory height, used only when Core is not installed.
    ///
    /// Core's InventoryRows is the right implementation and this is deliberately not a rival
    /// to it: it is patched in only when the Chainloader reports no Core, so the two never
    /// both write. When Core is present Rist claims through it exactly as before, and none of
    /// this runs.
    ///
    /// **The honest warning, which belongs in the README too.** Core's version exists because
    /// Inventory.m_height is a single private int with no owner. Two mods that both want extra
    /// rows each write it, the last writer wins, and a mod that writes only when its own state
    /// changes loses silently to one that writes every frame. Core adds every claim up and
    /// writes once, so mods stack instead of fighting. This class cannot do that - it knows
    /// only about Rist. Standalone Rist plus any other row-granting mod is a fight, and which
    /// side wins is a matter of frame ordering. That is the cost of not requiring Core, and it
    /// is why Core remains the recommended way to run this.
    ///
    /// What is faithfully copied rather than simplified is the load widening below. Skipping
    /// it does not degrade the feature, it destroys items - so it is not optional here.
    /// </summary>
    internal static class OwnInventoryRows
    {
        /// <summary>Rows Rist currently wants. Set by Effects, read by the tick.</summary>
        internal static int Claimed;

        private static System.Reflection.FieldInfo _height;
        private static Player _player;
        private static int _base = -1;
        private static int _applied = -1;
        private static bool _widened;

        /// <summary>
        /// How many rows the grid is opened to while a character is read off disk. Generous and
        /// temporary: nothing is drawn during a load and the next tick trims it straight back,
        /// never below what the items themselves occupy.
        /// </summary>
        private const int LoadSlack = 16;

        /// <summary>Driven from RistPlugin.Update, since there is no Core to own the timing.</summary>
        internal static void Tick()
        {
            var player = Player.m_localPlayer;
            if (player == null)
            {
                // A respawn builds a new Player with a fresh Inventory, and the old baseline
                // means nothing against it.
                _player = null;
                _base = -1;
                _applied = -1;
                return;
            }

            var inventory = player.GetInventory();
            if (inventory == null) return;

            if (!ReferenceEquals(player, _player))
            {
                _player = player;
                _base = inventory.GetHeight();
                _applied = -1;

                RistPlugin.Log.LogInfo("Inventory rows: vanilla height is " + _base + ".");
            }

            // Unless a load has just widened the grid, in which case this must run even with
            // nothing claimed - the widening is temporary and something has to take it back
            // down, or the inventory stays sixteen rows tall for the session.
            if (Claimed == 0 && !_widened && _applied < 0) return;
            if (Claimed == _applied) return;

            if (_height == null) _height = AccessTools.Field(typeof(Inventory), "m_height");
            if (_height == null)
            {
                RistPlugin.Log.LogError("Inventory.m_height not found - extra rows cannot work.");
                _applied = Claimed;
                return;
            }

            // Never below what is actually in the grid. Releasing rows is a real operation -
            // lose a rank and the row goes back - and the items standing in those rows must
            // not be sealed off behind the new edge.
            var wanted = Mathf.Max(_base + Claimed, Occupied(inventory));

            _applied = Claimed;
            _widened = false;
            _height.SetValue(inventory, wanted);

            RistPlugin.Log.LogInfo("Inventory rows: " + _base + " + " + Claimed +
                                   (wanted > _base + Claimed
                                       ? ", held at " + wanted + " by items in the grid"
                                       : "") + ".");
        }

        /// <summary>
        /// Opens the grid up before a character's items are read into it.
        ///
        /// Copied from Core deliberately and not trimmed. Without it every item in a claimed
        /// row is destroyed by loading the game, and nothing says so. Player.Load calls
        /// m_inventory.Load, which calls AddItem per stack, which begins:
        ///
        ///     if (x &lt; 0 || y &lt; 0 || x &gt;= m_width || y &gt;= m_height) return false;
        ///
        /// A saved position outside the current grid is dropped silently - not logged, not an
        /// error - and then written back out on the next save. Rows are applied from Update,
        /// which cannot run until Player.m_localPlayer exists, and that is after the load. So
        /// the grid is four rows tall at exactly the moment it matters and the bottom row is
        /// eaten on every relog.
        ///
        /// Widening rather than computing the right height, because the right height is not
        /// knowable here: the rank that grants the row has not been read yet at load time. The
        /// tick that follows already refuses to shrink below Occupied(), so the items decide
        /// what the grid ends up as, which is the correct authority.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Player), nameof(Player.Load))]
        private static void WidenBeforeLoad(Player __instance)
        {
            if (__instance == null) return;

            var inventory = __instance.GetInventory();
            if (inventory == null) return;

            if (_height == null) _height = AccessTools.Field(typeof(Inventory), "m_height");
            if (_height == null) return;

            // The baseline is captured here, before the widening, or the next tick would read
            // the widened value as vanilla and add the claim on top of it - and then do it
            // again on the following load. That compounds, which is the exact failure the
            // per-player capture at the top of Tick exists to avoid.
            _player = __instance;
            _base = inventory.GetHeight();
            _applied = -1;
            _widened = true;

            _height.SetValue(inventory, _base + LoadSlack);
        }

        /// <summary>
        /// One past the lowest row anything is standing in, so the grid is never cut above its
        /// own contents. Rows given back while occupied stay until they are emptied.
        /// </summary>
        private static int Occupied(Inventory inventory)
        {
            var lowest = 0;

            foreach (var item in inventory.GetAllItems())
            {
                if (item == null) continue;
                if (item.m_gridPos.y + 1 > lowest) lowest = item.m_gridPos.y + 1;
            }

            return lowest;
        }

        /// <summary>
        /// The window behind the slots, grown to cover the extra rows.
        ///
        /// Carried over rather than skipped because rows without it are not a smaller feature,
        /// they are a broken-looking one: the slots draw past the bottom edge of the wooden
        /// panel, over the world. Same caveat as the height itself - two mods both stretching
        /// this panel stretch it twice, and only Core can prevent that.
        /// </summary>
        internal static class Backdrop
        {
            private static InventoryGui _seen;
            private static int _shown = -1;

            private static readonly List<RectTransform> Panels = new List<RectTransform>();
            private static readonly List<float> Heights = new List<float>();

            // The container window sits under the player's, placed for a four row inventory.
            // Growing the one above it leaves the two overlapping - the bottom rows end up
            // behind the chest panel.
            private static RectTransform _container;
            private static Vector2 _containerBase;

            internal static void Tick()
            {
                var gui = InventoryGui.instance;
                if (gui == null || gui.m_player == null)
                {
                    _seen = null;
                    return;
                }

                if (!ReferenceEquals(gui, _seen))
                {
                    _seen = gui;
                    _shown = -1;
                    Capture(gui);
                }

                if (Claimed == _shown) return;

                _shown = Claimed;
                Resize(gui, Claimed);
            }

            private static void Capture(InventoryGui gui)
            {
                Panels.Clear();
                Heights.Clear();

                Remember(gui.m_player);

                _container = gui.m_container;
                if (_container != null) _containerBase = _container.anchoredPosition;

                // Found by the sprite it draws, then filtered by width. The sprite alone is not
                // enough: the armour and weight readouts down the right are cut from the same
                // woodpanel art, and growing those turns two small tabs into tall bars beside a
                // correctly sized panel.
                var full = gui.m_player.rect.width;

                foreach (var image in gui.m_player.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                {
                    if (image == null || image.sprite == null) continue;
                    if (image.sprite.name.IndexOf("woodpanel", System.StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (image.rectTransform.rect.width < full * 0.6f) continue;

                    Remember(image.rectTransform);
                }
            }

            private static void Remember(RectTransform rect)
            {
                if (rect == null || Panels.Contains(rect)) return;

                Panels.Add(rect);
                Heights.Add(rect.rect.height);
            }

            private static void Resize(InventoryGui gui, int rows)
            {
                // Not a guess: InventoryGrid lays its elements out at i * -m_elementSpace, so
                // one row is exactly that tall.
                var grid = gui.m_player.GetComponentInChildren<InventoryGrid>(true);
                if (grid == null || grid.m_elementSpace <= 0f) return;

                var added = rows * grid.m_elementSpace;

                for (var i = 0; i < Panels.Count; i++)
                {
                    if (Panels[i] == null) continue;

                    Panels[i].SetSizeWithCurrentAnchors(RectTransform.Axis.Vertical, Heights[i] + added);
                }

                // Pushed down by exactly what the inventory gained, from its own captured
                // baseline rather than by nudging it each time, so opening a chest twice does
                // not walk it off the screen.
                if (_container != null)
                    _container.anchoredPosition = _containerBase + new Vector2(0f, -added);
            }
        }
    }
}
