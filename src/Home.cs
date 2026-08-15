using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using BepInEx;
using HarmonyLib;

namespace Boon
{
    /// <summary>
    /// Which world each character on this machine belongs to.
    ///
    /// This is **protection, not enforcement**, and the distinction is why it can live on the
    /// client at all. The ledger and the gate cannot trust anything the client holds, because
    /// a player editing those would be cheating. Editing this only lets you damage your own
    /// character, so a plain text file you can open and correct is exactly right.
    ///
    /// What it protects against: taking a character into a different world "just to look".
    /// That writes an entry into PlayerProfile.m_worldData, nothing ever removes it, and the
    /// fresh-character gate then refuses that character on its own server forever.
    ///
    /// Keyed on the profile's **player id**, not its filename. The filename is the character's
    /// name, which is reused the moment a character is deleted and another made with the same
    /// name - the first version of this keyed on the filename and refused a brand new
    /// character because an older one had shared its name. m_playerID comes from
    /// Utils.GenerateUID() at creation and is unique per character.
    /// </summary>
    internal static class Home
    {
        private static readonly Dictionary<long, long> _homes = new Dictionary<long, long>();
        private static FieldInfo _playerId;
        private static bool _loaded;

        private static string HomePath => Path.Combine(Paths.ConfigPath, "boon-home.txt");

        /// <summary>The character's unique id, or 0 if it cannot be read.</summary>
        internal static long IdOf(PlayerProfile profile)
        {
            if (profile == null) return 0L;

            if (_playerId == null) _playerId = AccessTools.Field(typeof(PlayerProfile), "m_playerID");
            if (_playerId == null)
            {
                BoonPlugin.Log.LogError("PlayerProfile.m_playerID not found - character protection is off.");
                return 0L;
            }

            var value = _playerId.GetValue(profile);
            return value is long id ? id : 0L;
        }

        /// <summary>The world this character belongs to, or 0 if it has not been bound yet.</summary>
        internal static long Get(long playerId)
        {
            Load();

            if (playerId == 0L) return 0L;
            return _homes.TryGetValue(playerId, out var uid) ? uid : 0L;
        }

        /// <summary>
        /// Bind a character to a world the first time it is seen there. Never overwrites: a
        /// character already bound elsewhere has a problem to be told about, not one to paper
        /// over by re-pointing it at wherever it happens to be now.
        /// </summary>
        internal static void Bind(long playerId, string name, long worldUid)
        {
            Load();

            if (playerId == 0L || worldUid == 0L) return;

            if (_homes.TryGetValue(playerId, out var existing))
            {
                if (existing == worldUid) return;

                BoonPlugin.Log.LogWarning("Character '" + name + "' (" + playerId + ") is bound to world " +
                                          existing + " but is in world " + worldUid + ". Too late to stop " +
                                          "it - that world is now written into the character.");
                return;
            }

            _homes[playerId] = worldUid;
            Save();

            BoonPlugin.Log.LogInfo("Bound character '" + name + "' (" + playerId + ") to world " + worldUid + ".");
        }

        /// <summary>Forget a binding, so the character may be taken anywhere again.</summary>
        internal static void Forget(long playerId)
        {
            Load();
            if (_homes.Remove(playerId)) Save();
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            if (!File.Exists(HomePath)) return;

            foreach (var line in File.ReadAllLines(HomePath))
            {
                var text = line.Trim();
                if (text.Length == 0 || text[0] == '#') continue;

                var bits = text.Split('|');
                if (bits.Length != 2) continue;

                // Lines from the first version were keyed by character name and are silently
                // dropped here - a name is not an identity, which is the bug this replaced.
                if (!long.TryParse(bits[0], NumberStyles.Integer, CultureInfo.InvariantCulture, out var id)) continue;
                if (!long.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid)) continue;

                _homes[id] = uid;
            }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Which world each character belongs to: playerId|worldUid",
                    "# Boon warns before starting a character in any other world, because doing",
                    "# so permanently records that world in the character and locks it out of",
                    "# its own server. Delete a line to unbind that character.",
                };

                foreach (var kv in _homes) lines.Add(kv.Key + "|" + kv.Value);

                File.WriteAllLines(HomePath, lines.ToArray());
            }
            catch (Exception e)
            {
                BoonPlugin.Log.LogError("Could not write " + HomePath + ": " + e.Message);
            }
        }
    }
}
