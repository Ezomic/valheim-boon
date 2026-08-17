using HarmonyLib;

namespace Rist
{
    /// <summary>
    /// Dying no longer costs skill progress.
    ///
    /// Valheim already has a world modifier for this - Game.m_skillReductionRate, fed by the
    /// SkillReductionRate global key - and it is not enough on its own. Skills.OnDeath calls
    /// LowerAllSkills(m_DeathLowerFactor * rate), and inside that method only the level loss
    /// is scaled by the factor:
    ///
    ///     m_level -= m_level * factor;      // scaled - zero factor means no loss
    ///     m_accumulator = 0f;               // NOT scaled - always wiped
    ///     ...ShowMessage("$msg_skills_lowered")  // NOT scaled - always shown
    ///
    /// So setting the world's skill reduction to zero still throws away partial progress
    /// toward the next level in every skill, and still announces that skills were lowered.
    /// On a skill in the seventies, where one level is a long grind, the accumulator is the
    /// part that actually hurts. Skipping the method removes all three together.
    /// </summary>
    [HarmonyPatch(typeof(Skills), nameof(Skills.OnDeath))]
    internal static class DeathPenalty
    {
        private static bool Prefix()
        {
            // False skips the original entirely.
            return !RistConfig.RemoveDeathSkillLoss.Value;
        }
    }
}
