using HarmonyLib;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The three attack-speed cards, split by what is in your hands.
    ///
    /// This is the first thing in Boon that patches a gameplay path rather than riding a stat
    /// the game already sums, and it is here because there is no other way: SE_Stats has no
    /// attack-speed field and SEMan has no ModifyAttackSpeed among its twenty-two hooks. Swing
    /// speed is the animator's speed, full stop.
    ///
    /// What makes it cheap anyway is that vanilla already owns that number and already puts it
    /// back. CharacterAnimEvent.CustomFixedUpdate runs
    ///
    ///     if (!InAttack() &amp;&amp; !InMinorAction() &amp;&amp; !InEmote() &amp;&amp; CanMove())
    ///         m_animator.speed = 1f;
    ///
    /// every fixed update, so a raised speed is reset the moment the swing ends. There is no
    /// restore to write and no timer to leak. Two more things fall out of the same design:
    /// ZSyncAnimation writes m_animator.speed into the ZDO on the owner and reads it on
    /// remotes, so other players see the faster swing without any network code of ours; and
    /// FreezeFrame captures the current speed into m_pauseSpeed before the hit-pause and
    /// restores it after, so the hit-stop still works.
    ///
    /// The speed is set through CharacterAnimEvent.Speed, which is public and does exactly
    /// this - no reflection, and the one seam the game itself offers.
    /// </summary>
    internal static class AttackSpeed
    {
        internal const string Melee = "*attackspeed:melee";
        internal const string Tools = "*attackspeed:tools";
        internal const string Ranged = "*attackspeed:ranged";

        /// <summary>
        /// Which card, if any, covers the thing being swung.
        ///
        /// Read off m_skillType rather than the item type, because that is what the game
        /// itself dispatches on and it separates a pickaxe from a sword without a name list.
        ///
        /// Two edges worth knowing. An axe is SkillType.Axes whether it is meeting a tree or a
        /// greydwarf - one item, one animation, no way to tell during the swing - so felling
        /// timber rides the melee card, not the tool card. And a staff is ElementalMagic or
        /// BloodMagic, which no card covers: casting stays at vanilla speed until there is a
        /// fourth card for it.
        /// </summary>
        private static string CategoryOf(ItemDrop.ItemData weapon)
        {
            if (weapon == null || weapon.m_shared == null) return null;

            switch (weapon.m_shared.m_skillType)
            {
                case Skills.SkillType.Bows:
                case Skills.SkillType.Crossbows:
                    return Ranged;

                case Skills.SkillType.Pickaxes:
                case Skills.SkillType.WoodCutting:
                    return Tools;

                case Skills.SkillType.Swords:
                case Skills.SkillType.Knives:
                case Skills.SkillType.Clubs:
                case Skills.SkillType.Polearms:
                case Skills.SkillType.Spears:
                case Skills.SkillType.Axes:
                case Skills.SkillType.Unarmed:
                    return Melee;
            }

            // The hammer, hoe and cultivator carry no skill at all, so they fall through to
            // the item type. They are still Attacks and still animate.
            return weapon.m_shared.m_itemType == ItemDrop.ItemData.ItemType.Tool ? Tools : null;
        }

        [HarmonyPatch(typeof(Attack), nameof(Attack.Start))]
        [HarmonyPostfix]
        private static void Started(bool __result, Humanoid character, CharacterAnimEvent animEvent,
                                    ItemDrop.ItemData weapon)
        {
            if (!__result || !BoonConfig.Enabled.Value) return;
            if (character == null || animEvent == null) return;

            // Local player only. The cards live in ClientState, which is this client's own
            // standing - another player's swing is driven by their game and arrives here
            // through the ZDO already at the right speed.
            if (!ReferenceEquals(character, Player.m_localPlayer)) return;

            var category = CategoryOf(weapon);
            if (category == null) return;

            var bonus = Effects.TotalFor(category);
            if (bonus <= 0f) return;

            // Clamped, because this multiplies an animation rather than a number in a table.
            // A mis-typed catalogue line could otherwise run the whole character at twenty
            // times speed, and animation events are what land the hit.
            bonus = Mathf.Min(bonus, Mathf.Max(0f, BoonConfig.AttackSpeedMax.Value));

            animEvent.Speed(1f + bonus);
        }
    }
}
