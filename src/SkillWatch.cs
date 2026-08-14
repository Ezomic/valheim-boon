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

            // Logged on the sending side as well as the receiving side. The two together are
            // what tell you whether a missing grant means the hook never fired or the report
            // never arrived - with only the server's log, both look identical.
            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo("Skill up: " + skill + " reached " + level + ", reporting.");

            Net.ReportSkillUp(skill, level);
        }
    }
}
