using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using HarmonyLib;

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

                sb.Append("otherWorlds=").Append(CountOtherWorlds(profile, currentWorld));
                sb.Append(";cheats=").Append(profile.m_usedCheats ? 1 : 0);
                sb.Append(";commands=").Append(profile.m_knownCommands != null ? profile.m_knownCommands.Count : 0);
                sb.Append(";stats=").Append(TotalStats(profile).ToString("0", CultureInfo.InvariantCulture));
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

        private static float TotalStats(PlayerProfile profile)
        {
            var total = 0f;
            if (profile.m_playerStats == null) return total;

            foreach (PlayerStatType type in Enum.GetValues(typeof(PlayerStatType)))
            {
                total += profile.m_playerStats[type];
            }

            return total;
        }

        // ---- server side ---------------------------------------------------------------

        /// <summary>
        /// Judge a client's reported facts. With GateEnforce off this only writes to the log,
        /// which is the intended way to start: the rule applies to the server owner's own
        /// character too, and it is worth reading what it would have blocked before it starts
        /// disconnecting people.
        /// </summary>
        internal static void Evaluate(long sender, string owner, string facts)
        {
            if (!BoonConfig.RequireFreshCharacter.Value) return;

            var parsed = ParseFacts(facts);
            var who = owner ?? ("peer " + sender);

            if (parsed == null)
            {
                BoonPlugin.Log.LogWarning("Gate: " + who + " sent unreadable profile facts ('" + facts + "').");
                return;
            }

            var reasons = new List<string>();

            if (Value(parsed, "otherWorlds") > 0)
                reasons.Add("has played on " + Value(parsed, "otherWorlds") + " other world(s)");

            if (Value(parsed, "cheats") > 0)
                reasons.Add("character is flagged as having used cheats");

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
