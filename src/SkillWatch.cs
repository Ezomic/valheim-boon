using HarmonyLib;

namespace Boon
{
    /// <summary>
    /// The client's only job in earning levels: say that a skill went up.
    ///
    /// Player.OnSkillLevelup is called once per skill level-up, from Skills.RaiseSkill, which
    /// makes it the single hook for "something was learned". Note what is *not* sent - no
    /// totals, no XP, no level. The client reports an event and the server derives everything
    /// from it, so a client that lies can at most claim an event happened, which the rate
    /// limit already bounds.
    ///
    /// Nothing here modifies the skill system. Vanilla skills are read, never written.
    /// </summary>
    [HarmonyPatch(typeof(Player), nameof(Player.OnSkillLevelup))]
    internal static class SkillWatch
    {
        private static void Postfix(Player __instance, Skills.SkillType skill, float level)
        {
            if (!BoonConfig.Enabled.Value) return;

            // Other players' characters exist on this client too; only our own skill-ups are
            // ours to report.
            if (__instance != Player.m_localPlayer) return;

            Net.ReportSkillUp(skill, level);
        }
    }
}
