using System;
using UnityEngine;

namespace Rist
{
    /// <summary>
    /// The character level curve.
    ///
    /// Deliberately its own number rather than a reading of the skill list. XP arrives from
    /// skill level-ups, but weighted by the level reached, so the level tracks how far a
    /// character has actually been pushed rather than how many cheap early levels it has
    /// collected.
    /// </summary>
    internal static class Levels
    {
        /// <summary>XP granted for a single skill reaching <paramref name="skillLevel"/>.</summary>
        internal static float XpForSkillUp(float skillLevel)
        {
            // Level reached, not a flat rate. Vanilla's own skill cost curve is
            // pow(level+1, 1.5), so early levels are nearly free; paying a flat amount for
            // each would rain cards in the first hours and dry up exactly when the deep
            // ranks start to matter. It also removes the incentive to grind a fresh cheap
            // skill from zero purely to farm picks.
            return Mathf.Max(1f, skillLevel) * RistConfig.XpPerSkillLevel.Value;
        }

        /// <summary>Cumulative XP needed to have reached <paramref name="level"/>.</summary>
        internal static float XpForLevel(int level)
        {
            if (level <= 0) return 0f;
            return RistConfig.LevelBaseXp.Value * Mathf.Pow(level, RistConfig.LevelExponent.Value);
        }

        /// <summary>The level a given total XP amounts to.</summary>
        internal static int LevelForXp(float xp)
        {
            if (xp <= 0f) return 0;

            var b = RistConfig.LevelBaseXp.Value;
            var e = RistConfig.LevelExponent.Value;
            if (b <= 0f || e <= 0f) return 0;

            // Inverse of XpForLevel. Floored, so the level ticks over exactly when the
            // cumulative threshold is met.
            var level = Mathf.FloorToInt(Mathf.Pow(xp / b, 1f / e));
            return Mathf.Max(0, level);
        }

        /// <summary>Progress through the current level, 0..1, for the HUD.</summary>
        internal static float Progress(float xp)
        {
            var level = LevelForXp(xp);
            var start = XpForLevel(level);
            var end = XpForLevel(level + 1);
            if (end <= start) return 0f;
            return Mathf.Clamp01((xp - start) / (end - start));
        }
    }
}
