using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// Turns the cards a player holds into actual effect.
    ///
    /// Everything except the inventory row rides SE_Stats, the game's own stat-modifier
    /// status effect. That is the whole reason the catalogue can name fields directly: the
    /// game already sums these across active status effects for carry weight, armour,
    /// stamina costs and the rest, so a card needs no stat plumbing of its own - only a
    /// number written into a field the game was already reading.
    /// </summary>
    internal static class Effects
    {
        private const string EffectName = "Boon";

        private static SE_Stats _applied;
        private static string _appliedSignature;

        private static int _baseInventoryHeight = -1;
        private static FieldInfo _inventoryHeight;

        internal static void Reset()
        {
            _applied = null;
            _appliedSignature = null;
        }

        /// <summary>
        /// Bring the local player in line with <paramref name="ranks"/>. Cheap to call every
        /// frame: it does nothing unless the set of cards actually changed.
        /// </summary>
        internal static void Apply(Player player, Dictionary<string, int> ranks)
        {
            if (player == null) return;

            var signature = Signature(ranks);
            if (signature == _appliedSignature && _applied != null) return;
            _appliedSignature = signature;

            ApplyStats(player, ranks);
            ApplyInventoryRows(player, ranks);

            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo("Applied cards: " + (signature.Length == 0 ? "(none)" : signature));
        }

        private static void ApplyStats(Player player, Dictionary<string, int> ranks)
        {
            var seman = player.GetSEMan();
            if (seman == null) return;

            // Replace wholesale rather than editing in place. A status effect already held by
            // SEMan is keyed on its name hash, so adding a second with the same name is a
            // no-op - the old values would simply persist.
            var stats = ScriptableObject.CreateInstance<SE_Stats>();
            stats.name = EffectName;
            stats.m_name = EffectName;
            stats.m_ttl = 0f;   // 0 is permanent; anything else expires mid-session.

            var any = false;
            foreach (var kv in ranks)
            {
                var card = Cards.Get(kv.Key);
                if (card == null || card.IsSpecial || card.Field == null) continue;
                if (kv.Value <= 0) continue;

                // Cards accumulate by rank, and several cards may target the same field, so
                // read-add-write rather than assign.
                var current = (float)card.Field.GetValue(stats);
                card.Field.SetValue(stats, current + card.PerRank * kv.Value);
                any = true;
            }

            seman.RemoveStatusEffect(stats.NameHash(), quiet: true);
            if (any)
            {
                seman.AddStatusEffect(stats);
                _applied = stats;
            }
            else
            {
                _applied = null;
            }
        }

        /// <summary>
        /// The one card that is not a stat. Inventory capacity is two ints on Inventory and
        /// InventoryGrid re-reads them every frame, rebuilding its grid when they change, so
        /// adding a row needs no UI work at all.
        /// </summary>
        private static void ApplyInventoryRows(Player player, Dictionary<string, int> ranks)
        {
            var inventory = player.GetInventory();
            if (inventory == null) return;

            if (_inventoryHeight == null)
                _inventoryHeight = AccessTools.Field(typeof(Inventory), "m_height");

            if (_inventoryHeight == null)
            {
                BoonPlugin.Log.LogError("Inventory.m_height not found - the inventory row card cannot work.");
                return;
            }

            // Capture vanilla's own height the first time, before anything is added, so the
            // card is always measured from the real base rather than from a previous run's
            // result. Compounding here would grow the pack a row per reload.
            if (_baseInventoryHeight < 0)
            {
                _baseInventoryHeight = inventory.GetHeight();
                if (_baseInventoryHeight <= 0) _baseInventoryHeight = BoonConfig.InventoryBaseHeight.Value;
            }

            var extra = 0;
            foreach (var kv in ranks)
            {
                var card = Cards.Get(kv.Key);
                if (card == null || card.Effect != "*inventoryrow" || kv.Value <= 0) continue;
                extra += Mathf.RoundToInt(card.PerRank * kv.Value);
            }

            var want = _baseInventoryHeight + Mathf.Max(0, extra);
            if (inventory.GetHeight() != want) _inventoryHeight.SetValue(inventory, want);
        }

        private static string Signature(Dictionary<string, int> ranks)
        {
            if (ranks == null || ranks.Count == 0) return "";

            var ids = new List<string>(ranks.Keys);
            ids.Sort();

            var sb = new System.Text.StringBuilder();
            foreach (var id in ids)
            {
                if (sb.Length > 0) sb.Append(',');
                sb.Append(id).Append(':').Append(ranks[id]);
            }

            return sb.ToString();
        }
    }
}
