using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// Whether this server is willing to pay for the skill levels a character turned up with.
    ///
    /// It used to refuse the connection, and that was the wrong power for this mod to hold. A
    /// levelling mod deciding who may play means a bug in an XP system locks people out of the
    /// server, and it did: a character that had been used on another world was kicked with
    /// Valheim's generic screen and the reason only in the server's log, so the player could
    /// not tell a refusal from a crash. The travel check that produced that verdict has moved
    /// out to Threshold, where refusing a connection is the whole job and is done openly.
    ///
    /// What is left here is the part that was always Boon's own business: this server keeps a
    /// record of how high it watched each skill go, and if a character comes back higher than
    /// that, the gain did not happen here and is not paid for. The character plays normally.
    /// It simply earns nothing until its levels line up with what this server saw.
    ///
    /// That is a strictly smaller claim, and it needs no profile facts to make - the baseline
    /// is the server's own memory, which no client can reach or edit.
    /// </summary>
    internal static class Gate
    {
        /// <summary>
        /// Owners whose reported skills this server does not vouch for, for this session.
        ///
        /// Deliberately not persisted. It is a judgement about a login, re-made on the next
        /// one, and writing it to the ledger would turn a recoverable state into a permanent
        /// mark on a character that may simply have been fixed.
        /// </summary>
        private static readonly HashSet<string> Untrusted = new HashSet<string>(StringComparer.Ordinal);

        /// <summary>Whether XP should be withheld from this owner right now.</summary>
        internal static bool IsUntrusted(string owner)
        {
            return owner != null && BoonConfig.WithholdUntrustedXp.Value && Untrusted.Contains(owner);
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
        /// Judge a joining character on both counts: where it has been, and whether it came
        /// back stronger than this server watched it become.
        ///
        /// The two catch different things and neither subsumes the other. The travel check
        /// reads the character's own file, so it sees a trip even if nothing was gained - and
        /// can be defeated by editing that file. The snapshot compares against this server's
        /// own record, which no client can reach, so it survives an edited file - but it only
        /// notices a trip that actually produced levels. Together they cover both.
        ///
        /// With GateEnforce off this only writes to the log.
        /// </summary>
        internal static void Judge(long sender, string owner, string facts, string skills)
        {
            if (!BoonConfig.CheckSkillBaseline.Value) return;

            var who = owner ?? ("peer " + sender);
            var reasons = new List<string>();

            SnapshotReasons(skills, owner, who, reasons);

            if (owner != null) Untrusted.Remove(owner);

            if (reasons.Count == 0)
            {
                if (BoonConfig.Verbose.Value) BoonPlugin.Log.LogInfo("Gate: " + who + " passed.");
                return;
            }

            var why = string.Join(", ", reasons.ToArray());

            if (!BoonConfig.WithholdUntrustedXp.Value)
            {
                BoonPlugin.Log.LogWarning("Gate (not enforcing): would withhold XP from " + who + " - " + why + ".");
                return;
            }

            if (owner != null) Untrusted.Add(owner);
            BoonPlugin.Log.LogWarning("Gate: withholding XP from " + who + " - " + why + ".");

            // Say it to the player, not only to the log. The whole failure of the old
            // behaviour was that the person affected had no way to find out what happened, and
            // moving from a kick to a quiet withholding makes that worse rather than better -
            // nothing visible happens at all, you simply stop earning.
            Net.SendNotice(sender, BoonConfig.UntrustedMessage.Value);
        }

        /// <summary>
        /// Compare the reported skills against the highest levels this server itself watched
        /// them reach.
        ///
        /// A character with no snapshot yet has its current skills **adopted** as the baseline
        /// rather than being refused. Refusing would turn away everyone the first time this
        /// shipped, and a genuinely imported character is what the travel check is for - the
        /// two are deliberately layered.
        /// </summary>
        private static void SnapshotReasons(string skills, string owner, string who, List<string> reasons)
        {
            if (string.IsNullOrEmpty(owner)) return;

            var reported = ParseSkills(skills);
            if (reported == null) return;

            var rec = Ledger.For(owner);
            if (rec == null) return;

            if (!rec.HasSnapshot)
            {
                foreach (var kv in reported) rec.Snapshot[kv.Key] = kv.Value;
                Ledger.Touch();

                BoonPlugin.Log.LogInfo("Gate: adopted a first skill baseline for " + who +
                                       " (" + reported.Count + " skills). Future joins are compared against it.");
                return;
            }

            var slack = Mathf.Max(0f, BoonConfig.SkillDriftAllowance.Value);

            foreach (var kv in reported)
            {
                var seen = rec.Snapshot.TryGetValue(kv.Key, out var s) ? s : 0f;
                if (kv.Value <= seen + slack) continue;

                reasons.Add((Skills.SkillType)kv.Key + " is " + kv.Value.ToString("0.#") +
                            " but this server only saw it reach " + seen.ToString("0.#"));
            }

            // Passing means the report matched, so take it as the new baseline - it is the
            // freshest confirmation of a state this server agrees with.
            if (reasons.Count != 0) return;

            foreach (var kv in reported)
            {
                if (!rec.Snapshot.TryGetValue(kv.Key, out var s) || kv.Value > s)
                {
                    rec.Snapshot[kv.Key] = kv.Value;
                    Ledger.Touch();
                }
            }
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
