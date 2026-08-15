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

        // StatusEffect.NameHash() hashes the UnityEngine.Object name, so this can be computed
        // once without instantiating anything to ask.
        private static readonly int EffectHash = EffectName.GetStableHashCode();

        private static SE_Stats _applied;
        private static string _appliedSignature;
        private static Player _appliedTo;

        private static int _baseInventoryHeight = -1;
        private static FieldInfo _inventoryHeight;

        internal static void Reset()
        {
            if (_applied != null) Object.Destroy(_applied);

            _applied = null;
            _appliedSignature = null;
            _appliedTo = null;
            _baseInventoryHeight = -1;
        }

        /// <summary>
        /// Bring the local player in line with <paramref name="ranks"/>. Cheap to call every
        /// frame: it returns immediately unless the cards or the player actually changed.
        /// </summary>
        internal static void Apply(Player player, Dictionary<string, int> ranks)
        {
            if (player == null) return;

            var signature = Signature(ranks);

            // Both halves matter. Comparing only the signature would miss a respawn, which
            // builds a fresh Player with a fresh SEMan holding none of this. An earlier
            // version also tested "_applied != null", which is null exactly when you hold no
            // cards - so with an empty hand it re-applied every frame, leaking a
            // ScriptableObject per frame and filling the log.
            if (ReferenceEquals(player, _appliedTo) && signature == _appliedSignature) return;

            _appliedTo = player;
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

            // Work out what is owed before building anything, so an empty hand costs no
            // allocation at all.
            var totals = new Dictionary<FieldInfo, float>();
            foreach (var kv in ranks)
            {
                if (kv.Value <= 0) continue;

                var card = Cards.Get(kv.Key);
                if (card == null || card.IsSpecial || card.Field == null) continue;

                // Several cards may target the same field, so accumulate rather than assign.
                totals.TryGetValue(card.Field, out var running);
                totals[card.Field] = running + card.PerRank * kv.Value;
            }

            // Replace wholesale rather than editing in place: SEMan keys on the name hash, so
            // adding a second effect with the same name is a no-op and the old values would
            // simply persist.
            seman.RemoveStatusEffect(EffectHash, quiet: true);

            if (_applied != null)
            {
                Object.Destroy(_applied);
                _applied = null;
            }

            if (totals.Count == 0) return;

            var stats = ScriptableObject.CreateInstance<SE_Stats>();
            stats.name = EffectName;
            stats.m_name = EffectName;
            stats.m_ttl = 0f;   // 0 is permanent; anything else expires mid-session.

            // Two of the game's modifiers are gated behind a skill rather than applying to
            // everything, and a fresh SE_Stats has both as SkillType.None:
            //
            //   ModifyRaiseSkill  runs only if m_raiseSkill != 0
            //   ModifyAttack      runs m_damageModifier only if it matches m_modifyAttackSkill
            //
            // Without these two lines Quick study did nothing whatsoever, at any rank, and a
            // damage card could never have worked. Set unconditionally because both are inert
            // when no card targets them - m_raiseSkillModifier is 0, and m_damageModifier is
            // left at its neutral 1.
            stats.m_raiseSkill = Skills.SkillType.All;
            stats.m_modifyAttackSkill = Skills.SkillType.All;

            foreach (var kv in totals)
            {
                // Some fields count from 1 rather than 0 - see Card.IsOneBased. A card still
                // carries the plain fraction, because that is what reads correctly both in the
                // catalogue and on the tile; the conversion belongs here, once.
                var value = Card.IsOneBased(kv.Key.Name) ? 1f + kv.Value : kv.Value;
                kv.Key.SetValue(stats, value);
            }

            seman.AddStatusEffect(stats);
            _applied = stats;
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
                if (kv.Value <= 0) continue;

                var card = Cards.Get(kv.Key);
                if (card == null || card.Effect != "*inventoryrow") continue;
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
