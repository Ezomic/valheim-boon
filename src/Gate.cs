using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Boon
{
    /// <summary>
    /// What this server has watched each of a character's skills reach.
    ///
    /// This used to be a judgement and is now only a measurement, and the difference is the
    /// whole history of the file. It began by refusing the connection outright, which is the
    /// wrong power for a levelling mod to hold: a bug in an XP system then locks people out of
    /// a server, and it did - the player got Valheim's generic kick screen with the reason only
    /// in the server's log. That check moved to Threshold, where turning people away is the
    /// entire job and is done openly with its own message.
    ///
    /// What replaced it here was softer but not actually better: a character whose skills sat
    /// above the baseline was marked untrusted and paid no XP "until they line up again". They
    /// never could. The withholding returned before the snapshot was updated, and the login
    /// comparison only adopted a new baseline when it had found nothing wrong - so the baseline
    /// froze at the moment of judgement while the player's real skills only ever climbed. The
    /// gap widened forever, on that character, on that server, while the message on screen
    /// promised a recovery that no amount of play could reach.
    ///
    /// So the judgement is gone and the measurement stays. An imported character is simply
    /// **not paid for what it did elsewhere** - which is the property this was always after,
    /// and which Boon already had and already documents: nothing is ever backfilled, so a
    /// character arriving at skill 50 starts at Boon level 0 regardless. Preventing a character
    /// from being used across worlds at all is a door policy, and the door is Threshold's.
    ///
    /// The baseline itself is still worth keeping, for a reason that has nothing to do with
    /// trust: <see cref="Throttle.Step"/> needs it to tell a plausible next level from a forged
    /// one, and an absent entry has to be distinguishable from a skill never used.
    /// </summary>
    internal static class Gate
    {
        /// <summary>
        /// Owners this server holds a complete skill list for, learned from the login exchange.
        ///
        /// Kept because an absent snapshot entry is ambiguous on its own: it means either "this
        /// skill has never been used" or "we have never looked". Anything comparing a report
        /// against the baseline has to know which, and only the login exchange can say - it is
        /// the one moment every skill is reported at once.
        /// </summary>
        private static readonly HashSet<string> Baselined = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether this server has seen this owner's whole skill list this session.</summary>
        internal static bool HasBaseline(string owner)
        {
            return owner != null && Baselined.Contains(owner);
        }

        /// <summary>
        /// Drop what was learned, for a new connection. Per-session by design: it describes a
        /// login rather than a character.
        /// </summary>
        internal static void Forget()
        {
            Baselined.Clear();
        }

        /// <summary>
        /// Every skill and the level it currently sits at, as "type:level" pairs.
        /// </summary>
        internal static string LocalSkills()
        {
            var player = Player.m_localPlayer;
            if (player == null) return "";

            var skills = player.GetSkills();
            if (skills == null) return "";

            var sb = new StringBuilder();
            foreach (var skill in skills.GetSkillList())
            {
                if (skill == null || skill.m_info == null) continue;
                if (sb.Length > 0) sb.Append(',');
                sb.Append((int)skill.m_info.m_skill).Append(':')
                  .Append(skill.m_level.ToString("R", CultureInfo.InvariantCulture));
            }

            return sb.ToString();
        }

        // ---- server side ---------------------------------------------------------------

        /// <summary>
        /// Take a joining character's skill list as the baseline, whatever it says.
        ///
        /// No comparison and no verdict. A character that turns up higher than this server last
        /// saw it gained that somewhere else and is paid nothing for it - not because it is
        /// refused, but because XP only ever comes from a level-up watched from here. Adopting
        /// the higher number is what stops the next honest level-up looking like a forgery to
        /// <see cref="Throttle.Step"/>.
        ///
        /// The list is self-reported, and always was. It is not load-bearing on its own: what
        /// bounds a forged one is Step's single-level ceiling and the earning cap over connected
        /// time, neither of which asks the client anything.
        /// </summary>
        internal static void Judge(long sender, string owner, string facts, string skills)
        {
            if (!BoonConfig.CheckSkillBaseline.Value) return;
            if (string.IsNullOrEmpty(owner)) return;

            var reported = ParseSkills(skills);
            if (reported == null) return;

            var rec = Ledger.For(owner);
            if (rec == null) return;

            var first = !rec.HasSnapshot;
            Baselined.Add(owner);

            var raised = 0;
            foreach (var kv in reported)
            {
                if (rec.Snapshot.TryGetValue(kv.Key, out var seen) && kv.Value <= seen) continue;
                rec.Snapshot[kv.Key] = kv.Value;
                raised++;
            }

            if (raised > 0) Ledger.Touch();

            if (first)
                BoonPlugin.Log.LogInfo("Baseline adopted for " + owner + " (" + reported.Count +
                                       " skills). Only level-ups from here earn anything.");
            else if (raised > 0 && BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo("Baseline for " + owner + " raised on " + raised +
                                       " skill(s) - gained elsewhere, so not paid for.");
        }

        private static Dictionary<int, float> ParseSkills(string wire)
        {
            if (wire == null) return null;

            var map = new Dictionary<int, float>();
            foreach (var pair in wire.Split(','))
            {
                if (pair.Length == 0) continue;
                var bits = pair.Split(':');
                if (bits.Length != 2) continue;
                if (!int.TryParse(bits[0], out var type)) continue;
                if (!float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var level)) continue;
                map[type] = level;
            }

            return map;
        }
    }
}
