using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// One card. The effect is the literal name of a public float field on the
    /// game's own SE_Stats, which is why the catalogue is a text file rather than a switch
    /// statement: the game already ships about forty-five of these modifiers, and naming
    /// one turns it into a card without any code here knowing it exists.
    /// </summary>
    internal sealed class Card
    {
        internal string Id;
        internal string Name;
        internal string Flavour;
        internal string Effect;
        internal float PerRank;

        /// <summary>Resolved SE_Stats field. Null for specials, which Effects handles by hand.</summary>
        internal FieldInfo Field;

        internal bool IsSpecial => Effect.Length > 0 && Effect[0] == '*';

        /// <summary>
        /// The green line under the card name. Generated rather than written in the
        /// catalogue so that a new card is one line and cannot drift out of step with the
        /// number it actually applies.
        /// </summary>
        internal string Describe(int rank)
        {
            var total = PerRank * Mathf.Max(1, rank);
            if (!Labels.TryGetValue(Effect, out var label)) label = Effect;

            if (Percent.Contains(Effect))
            {
                var pct = total * 100f;
                return (pct >= 0f ? "+" : "−") + Mathf.Abs(pct).ToString("0.#", CultureInfo.InvariantCulture) + "% " + label;
            }

            return (total >= 0f ? "+" : "−") + Mathf.Abs(total).ToString("0.#", CultureInfo.InvariantCulture) + " " + label;
        }

        /// <summary>
        /// Fields whose neutral value is 1 rather than 0.
        ///
        /// The game reads these as multipliers and several test them before use - the three
        /// regen ones with a literal `if (m_xRegenMultiplier > 1f)`, which silently skipped a
        /// card writing 0.08 and made Long wind and Swift-mending do nothing at all at any
        /// rank. m_damageModifier is worse than silent: it multiplies the hit, so a raw 0.03
        /// would have cut damage to 3% rather than adding it.
        ///
        /// The catalogue still carries the plain fraction and Effects adds the 1 on the way
        /// in, so a card line and its tile both read the way anyone would expect.
        /// </summary>
        internal static bool IsOneBased(string effect)
        {
            return OneBased.Contains(effect);
        }

        private static readonly HashSet<string> OneBased = new HashSet<string>
        {
            "m_healthRegenMultiplier", "m_staminaRegenMultiplier", "m_eitrRegenMultiplier",
            "m_damageModifier",
        };

        // Only for display. A field with no entry falls back to its own name, which is ugly
        // but never wrong, and is a visible prompt to add it here.
        private static readonly Dictionary<string, string> Labels = new Dictionary<string, string>
        {
            { "m_addMaxCarryWeight", "carry weight" },
            { "m_addArmor", "armour" },
            { "*inventoryrow", "inventory row" },
            { "m_runStaminaUseModifier", "run stamina" },
            { "m_attackStaminaUseModifier", "attack stamina" },
            { "m_blockStaminaUseModifier", "block stamina" },
            { "m_swimStaminaUseModifier", "swim stamina" },
            { "m_jumpStaminaUseModifier", "jump stamina" },
            { "m_sneakStaminaUseModifier", "sneak stamina" },
            { "m_staminaRegenMultiplier", "stamina regen" },
            { "m_healthRegenMultiplier", "health regen" },
            { "m_eitrRegenMultiplier", "eitr regen" },
            { "m_fallDamageModifier", "fall damage" },
            { "m_stealthModifier", "stealth" },
            { "m_noiseModifier", "noise" },
            { "m_staggerModifier", "stagger taken" },
            { "m_raiseSkillModifier", "skill gain" },
            { "m_speedModifier", "movement speed" },
            { "m_damageModifier", "damage" },
            { "m_dodgeStaminaUseModifier", "dodge stamina" },
            { "m_swimSpeedModifier", "swim speed" },
            { "m_timedBlockBonus", "parry bonus" },
        };

        private static readonly HashSet<string> Percent = new HashSet<string>
        {
            "m_runStaminaUseModifier", "m_attackStaminaUseModifier", "m_blockStaminaUseModifier",
            "m_swimStaminaUseModifier", "m_jumpStaminaUseModifier", "m_sneakStaminaUseModifier",
            "m_staminaRegenMultiplier", "m_healthRegenMultiplier", "m_eitrRegenMultiplier",
            "m_fallDamageModifier", "m_stealthModifier", "m_noiseModifier", "m_staggerModifier",
            "m_raiseSkillModifier", "m_speedModifier", "m_damageModifier",
            "m_dodgeStaminaUseModifier", "m_swimSpeedModifier", "m_timedBlockBonus",
        };
    }

    /// <summary>
    /// The catalogue, read once from cards.txt beside the DLL.
    /// </summary>
    internal static class Cards
    {
        private static readonly List<Card> _all = new List<Card>();
        private static readonly Dictionary<string, Card> _byId = new Dictionary<string, Card>();

        internal static IReadOnlyList<Card> All => _all;

        internal static Card Get(string id)
        {
            return id != null && _byId.TryGetValue(id, out var c) ? c : null;
        }

        internal static void Load()
        {
            _all.Clear();
            _byId.Clear();

            var dir = Path.GetDirectoryName(typeof(Cards).Assembly.Location);
            var path = Path.Combine(dir ?? ".", "cards.txt");

            if (!File.Exists(path))
            {
                BoonPlugin.Log.LogError("cards.txt not found beside the DLL at " + path +
                                        " - no cards can be taken.");
                return;
            }

            var lineNo = 0;
            foreach (var raw in File.ReadAllLines(path))
            {
                lineNo++;
                var line = raw.Trim();
                if (line.Length == 0 || line[0] == '#') continue;

                var parts = line.Split('|');
                if (parts.Length < 5)
                {
                    BoonPlugin.Log.LogWarning("cards.txt line " + lineNo + ": expected 5 fields, got " +
                                              parts.Length + " - skipped.");
                    continue;
                }

                var card = new Card
                {
                    Id = parts[0].Trim(),
                    Name = parts[1].Trim(),
                    Flavour = parts[2].Trim(),
                    Effect = parts[3].Trim(),
                };

                if (!float.TryParse(parts[4].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture,
                                    out card.PerRank))
                {
                    BoonPlugin.Log.LogWarning("cards.txt line " + lineNo + ": '" + parts[4].Trim() +
                                              "' is not a number - skipped.");
                    continue;
                }

                if (card.Id.Length == 0)
                {
                    BoonPlugin.Log.LogWarning("cards.txt line " + lineNo + ": blank id - skipped.");
                    continue;
                }

                if (_byId.ContainsKey(card.Id))
                {
                    BoonPlugin.Log.LogWarning("cards.txt line " + lineNo + ": duplicate id '" + card.Id +
                                              "' - skipped.");
                    continue;
                }

                // An unknown field name is logged and skipped rather than thrown, matching how
                // the game treats a prefab name that does not resolve. A typo costs one card,
                // not the whole catalogue.
                if (!card.IsSpecial)
                {
                    card.Field = typeof(SE_Stats).GetField(card.Effect,
                        BindingFlags.Public | BindingFlags.Instance);

                    if (card.Field == null || card.Field.FieldType != typeof(float))
                    {
                        BoonPlugin.Log.LogWarning("cards.txt line " + lineNo + ": SE_Stats has no public " +
                                                  "float field '" + card.Effect + "' - card '" + card.Id +
                                                  "' skipped.");
                        continue;
                    }
                }
                else if (card.Effect != "*inventoryrow")
                {
                    BoonPlugin.Log.LogWarning("cards.txt line " + lineNo + ": unknown special '" +
                                              card.Effect + "' - card '" + card.Id + "' skipped.");
                    continue;
                }

                _all.Add(card);
                _byId[card.Id] = card;
            }

            BoonPlugin.Log.LogInfo("Loaded " + _all.Count + " cards from cards.txt.");
        }
    }
}
