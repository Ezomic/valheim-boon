using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rist
{
    /// <summary>
    /// How fast a claim is allowed to pay.
    ///
    /// Skills live on the client, so a skill-up report is a claim and nothing here verifies
    /// it - verification is impossible by construction. What is possible is bounding what a
    /// claim can be worth, and there are exactly two ways a forged report can be worth
    /// something: it can name a level far above what the character actually has, or it can
    /// arrive over and over. <see cref="Step"/> closes the first and <see cref="Afford"/>
    /// closes the second.
    ///
    /// Net's existing reports-per-minute limit is a third and blunter one. It caps how many
    /// messages are accepted, which is a different question from how much they are worth: at
    /// thirty reports a minute each claiming a level-100 skill, the old ceiling was three
    /// thousand XP a minute, which is roughly a character level every ten seconds.
    ///
    /// Everything here is a backstop rather than a verdict, so it logs and withholds and
    /// never disconnects anyone - the same posture the gate settled on when it stopped kicking.
    /// </summary>
    internal static class Throttle
    {
        /// <summary>
        /// Unspent earning allowance per owner, and when it was last topped up. A token bucket:
        /// it fills at MaxXpPerMinute and holds at most XpBurst, so a player who has been
        /// quiet for a while can be paid for a burst of level-ups at once without the cap
        /// making honest bursts feel wrong.
        /// </summary>
        private sealed class Purse
        {
            internal float Left;
            internal float At;
            internal bool Told;
        }

        private static readonly Dictionary<string, Purse> Purses =
            new Dictionary<string, Purse>(StringComparer.Ordinal);

        /// <summary>Wipe everything on a new connection. Called where the RPCs re-register.</summary>
        internal static void Forget()
        {
            Purses.Clear();
        }

        /// <summary>
        /// Whether a reported skill level is a plausible next step from what this server has
        /// itself watched that skill reach.
        ///
        /// A genuine level-up is exactly one above the last one, because Skills.Skill.Raise
        /// levels one at a time and calls Player.OnSkillLevelup for each - so a report of
        /// "Woodcutting is now 87" from a server that last saw 12 did not come from the game.
        /// Rejecting it also protects the baseline itself, which is otherwise raised to
        /// whatever was claimed and would hand a single forged packet a permanent alibi.
        ///
        /// Only enforced once this server holds a *complete* skill list for the owner, which
        /// is what the login exchange produces. Without one, an absent snapshot entry cannot
        /// be told apart from a skill the character has genuinely never used, and every first
        /// report would look like a jump. That also means turning CheckSkillBaseline off turns
        /// this off with it, which is the honest reading: no baseline, no comparison.
        /// </summary>
        internal static bool Step(RistRecord rec, string owner, int skillType, float skillLevel, out string why)
        {
            why = null;

            if (rec == null || !Gate.HasBaseline(owner)) return true;

            var seen = rec.Snapshot.TryGetValue(skillType, out var s) ? s : 0f;
            var allowed = seen + Mathf.Max(1f, RistConfig.MaxSkillLevelJump.Value);

            if (skillLevel <= allowed) return true;

            why = (Skills.SkillType)skillType + " jumped to " + skillLevel.ToString("0.#") +
                  " from a baseline of " + seen.ToString("0.#");
            return false;
        }

        /// <summary>
        /// Spend <paramref name="xp"/> out of the owner's earning allowance, or refuse it.
        ///
        /// This is the cap that does not care what the claim says. However convincing a report
        /// is, a character cannot earn more than time connected allows, and that is the one
        /// quantity the server measures for itself.
        ///
        /// The bucket starts full, so joining does not mean waiting to be allowed to earn.
        /// </summary>
        internal static bool Afford(long sender, string owner, float xp)
        {
            if (owner == null) return true;

            var perMinute = RistConfig.MaxXpPerMinute.Value;
            if (perMinute <= 0f) return true;   // Zero or below is off, not a hard stop.

            var ceiling = Mathf.Max(perMinute, RistConfig.XpBurst.Value);
            var now = Time.time;

            if (!Purses.TryGetValue(owner, out var purse))
            {
                purse = new Purse { Left = ceiling, At = now };
                Purses[owner] = purse;
            }

            purse.Left = Mathf.Min(ceiling, purse.Left + (now - purse.At) * (perMinute / 60f));
            purse.At = now;

            if (purse.Left < xp)
            {
                RistPlugin.Log.LogWarning("Earning cap reached for " + owner + " - " +
                                          xp.ToString("0.#") + " xp withheld (allowance " +
                                          purse.Left.ToString("0.#") + "). Either a very long " +
                                          "grinding session or a forged report.");

                // Once, not every time. Withholding is invisible by nature - the player simply
                // stops earning with nothing on screen to say why - and that silence is the
                // exact complaint that killed the old kick. Repeating it every level-up would
                // be its own problem.
                if (!purse.Told)
                {
                    purse.Told = true;
                    Net.SendNotice(sender, RistConfig.CappedMessage.Value);
                }

                return false;
            }

            purse.Left -= xp;
            return true;
        }
    }
}
