using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The fresh-character check.
    ///
    /// This, not the rate limit, is the real defence. If a character has only ever been on
    /// this world, then every skill level it holds was necessarily earned here, and there is
    /// nothing to verify. Rate limiting only bounds the damage of a forged report; the gate
    /// removes the reason to forge one.
    ///
    /// Checked on *every* login rather than only the first, so a character taken away to a
    /// creative world and brought back is caught on return rather than waved through because
    /// it was clean once.
    ///
    /// The honest limit: PlayerProfile is client-side, so these facts are self-reported and a
    /// purpose-built client can forge them. What it does catch is the ordinary case - an
    /// unmodified player who levelled elsewhere or used devcommands - because the game itself
    /// records both and has no reason to lie.
    /// </summary>
    internal static class Gate
    {
        private static FieldInfo _worldData;

        /// <summary>
        /// Facts about the local character, gathered on request from the server.
        ///
        ///   otherWorlds=N;cheats=0|1;commands=N;stats=F
        /// </summary>
        internal static string LocalFacts()
        {
            var sb = new StringBuilder();

            try
            {
                var profile = Game.instance != null ? Game.instance.GetPlayerProfile() : null;
                if (profile == null) return "error=noprofile";

                var currentWorld = ZNet.instance != null ? ZNet.instance.GetWorldUID() : 0L;

                // Only what the decision actually uses. An earlier version also summed
                // PlayerStats, which threw on the PlayerStatType.Count member that is not in
                // the dictionary - and because that killed the whole gather, the gate received
                // "error=exception" and checked nothing at all. A fact nothing decides on is
                // not worth a failure mode.
                sb.Append("otherWorlds=").Append(CountOtherWorlds(profile, currentWorld));
                sb.Append(";cheats=").Append(profile.m_usedCheats ? 1 : 0);
                sb.Append(";commands=").Append(profile.m_knownCommands != null ? profile.m_knownCommands.Count : 0);
            }
            catch (Exception e)
            {
                BoonPlugin.Log.LogWarning("Could not gather profile facts: " + e.Message);
                return "error=exception";
            }

            return sb.ToString();
        }

        /// <summary>
        /// How many worlds other than this one this character has played on.
        ///
        /// PlayerProfile.m_worldData is a Dictionary&lt;long, WorldPlayerData&gt; keyed by
        /// world UID - one entry per world the character has spawned in. It is private, hence
        /// the reflection, and it is the single most direct answer to "has this character been
        /// used elsewhere".
        /// </summary>
        private static int CountOtherWorlds(PlayerProfile profile, long currentWorld)
        {
            if (_worldData == null)
                _worldData = AccessTools.Field(typeof(PlayerProfile), "m_worldData");

            if (_worldData == null)
            {
                BoonPlugin.Log.LogError("PlayerProfile.m_worldData not found - the gate cannot see other worlds.");
                return -1;
            }

            if (!(_worldData.GetValue(profile) is IDictionary map)) return -1;

            var count = 0;
            foreach (DictionaryEntry entry in map)
            {
                if (!(entry.Key is long uid)) continue;
                if (uid != currentWorld) count++;
            }

            return count;
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
            if (!BoonConfig.RequireFreshCharacter.Value) return;

            var who = owner ?? ("peer " + sender);
            var reasons = new List<string>();

            TravelReasons(facts, who, reasons);
            SnapshotReasons(skills, owner, who, reasons);

            if (reasons.Count == 0)
            {
                if (BoonConfig.Verbose.Value) BoonPlugin.Log.LogInfo("Gate: " + who + " passed.");
                return;
            }

            var why = string.Join(", ", reasons.ToArray());

            if (!BoonConfig.GateEnforce.Value)
            {
                BoonPlugin.Log.LogWarning("Gate (not enforcing): would have refused " + who + " - " + why + ".");
                return;
            }

            BoonPlugin.Log.LogWarning("Gate: refusing " + who + " - " + why + ".");

            var peer = ZNet.instance != null ? ZNet.instance.GetPeer(sender) : null;
            if (peer == null) return;

            // Send the reason before dropping, the same way ZNet turns away a wrong-version
            // or banned client - it invokes "Error" with a ConnectionStatus and then
            // disconnects. Without this the player is dropped with no explanation at all and
            // has no way to tell a refusal from a crash.
            if (peer.m_rpc != null)
                peer.m_rpc.Invoke("Error", (int)ZNet.ConnectionStatus.ErrorKicked);

            ZNet.instance.Disconnect(peer);
        }

        private static void TravelReasons(string facts, string who, List<string> reasons)
        {
            var parsed = ParseFacts(facts);
            if (parsed == null)
            {
                BoonPlugin.Log.LogWarning("Gate: " + who + " sent unreadable profile facts ('" + facts + "').");
                return;
            }

            if (Value(parsed, "otherWorlds") > 0)
                reasons.Add("has played on " + Value(parsed, "otherWorlds") + " other world(s)");

            if (Value(parsed, "cheats") > 0)
                reasons.Add("character is flagged as having used cheats");
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

        private static Dictionary<string, float> ParseFacts(string facts)
        {
            if (string.IsNullOrEmpty(facts) || facts.StartsWith("error=")) return null;

            var map = new Dictionary<string, float>();
            foreach (var pair in facts.Split(';'))
            {
                var bits = pair.Split('=');
                if (bits.Length != 2) continue;
                if (!float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var v)) continue;
                map[bits[0]] = v;
            }

            return map.Count == 0 ? null : map;
        }

        private static float Value(Dictionary<string, float> map, string key)
        {
            return map.TryGetValue(key, out var v) ? v : 0f;
        }
    }
}
