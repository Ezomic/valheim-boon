using System;
using System.Collections.Generic;
using System.Globalization;

namespace Rist
{
    /// <summary>
    /// What each skill is worth toward the character level.
    ///
    /// Added because the live server's ledger said the curve was not the problem. A character
    /// at level 12 held 1346 xp, and half of it - 666 - came from WoodCutting alone at level
    /// 36. Run, Jump and Crafting brought another 361. Everything earned in a fight came to
    /// 293, under a quarter of the whole. The character level was mostly a record of how many
    /// trees had been felled in the Meadows, which is the one activity in the game that is
    /// unlimited, safe, and available from the first minute.
    ///
    /// Raising LevelBaseXp or LevelExponent could not have fixed that. Both scale the curve
    /// uniformly, so the ratio survives untouched and the late game pays for it - which is
    /// exactly what happened when the pair was 60 and 1.5 and had to be walked back.
    ///
    /// So the weight sits on the income instead. The principle behind the default table is
    /// **risk**: a skill is worth full price when it is raised by something that can kill you,
    /// and a fraction when it is raised by repetition that cannot.
    /// </summary>
    internal static class Weights
    {
        private static readonly Dictionary<int, float> _map = new Dictionary<int, float>();

        /// <summary>The raw string the map was last built from, so a change re-parses it.
        /// This is not only about hand edits: Suite.Sync overwrites the entry with the host's
        /// value on connect, and a map cached from the local one would outlive it.</summary>
        private static string _from;
        private static bool _parsed;

        /// <summary>The multiplier for one skill, or DefaultSkillWeight if it is not named.</summary>
        internal static float For(int skillType)
        {
            Build();
            return _map.TryGetValue(skillType, out var w) ? w : RistConfig.DefaultSkillWeight.Value;
        }

        private static void Build()
        {
            var raw = RistConfig.SkillWeights.Value ?? "";
            if (_parsed && raw == _from) return;

            _from = raw;
            _parsed = true;
            _map.Clear();

            var bad = 0;
            foreach (var entry in raw.Split(','))
            {
                var text = entry.Trim();
                if (text.Length == 0) continue;

                var eq = text.IndexOf('=');
                if (eq <= 0) { bad++; continue; }

                var name = text.Substring(0, eq).Trim();
                var value = text.Substring(eq + 1).Trim();

                if (!float.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var weight))
                {
                    bad++;
                    continue;
                }

                // A name or a raw number. The number is there because the ledger and the wire
                // both speak in (int)Skills.SkillType, so a weight for a skill this game
                // version has not got a name for is still expressible.
                int type;
                if (int.TryParse(name, out type))
                {
                    // nothing further - already a type id
                }
                else
                {
                    try
                    {
                        type = (int)(Skills.SkillType)Enum.Parse(typeof(Skills.SkillType), name, true);
                    }
                    catch (Exception)
                    {
                        RistPlugin.Log.LogWarning("SkillWeights: '" + name + "' is not a skill name; ignored.");
                        bad++;
                        continue;
                    }
                }

                // Negative would pay a player for losing ground, and there is no sensible
                // reading of it. Clamped rather than refused so one bad number cannot cost
                // the whole table.
                _map[type] = weight < 0f ? 0f : weight;
            }

            RistPlugin.Log.LogInfo("Skill weights: " + _map.Count + " skill(s) weighted" +
                                   (bad > 0 ? ", " + bad + " unreadable entr(y/ies) ignored" : "") +
                                   ", everything else at " + RistConfig.DefaultSkillWeight.Value.ToString("0.##") + ".");
        }

        /// <summary>
        /// What a character holding this skill list is worth in total.
        ///
        /// Exact rather than estimated, and that is what lets it be used both to credit a
        /// joining character and to recompute one whose weights have changed. XP is granted
        /// per level-up weighted by the level reached, so a skill at N has already produced
        /// 1 + 2 + ... + N, which is N(N+1)/2. Multiply by the skill's weight, sum over the
        /// list, and the result is precisely the xp a character would hold if every one of
        /// those level-ups had been watched from here under the weights standing now.
        /// </summary>
        internal static float WorthOf(Dictionary<int, float> skills)
        {
            if (skills == null) return 0f;

            var worth = 0f;
            foreach (var kv in skills)
            {
                var n = kv.Value;
                if (n <= 0f) continue;
                worth += For(kv.Key) * n * (n + 1f) * 0.5f;
            }

            return worth * RistConfig.XpPerSkillLevel.Value;
        }
    }
}
