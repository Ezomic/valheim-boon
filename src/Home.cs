using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using BepInEx;

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
    /// What it protects against: taking your main character into a different world "just to
    /// look". That writes an entry into the character's own PlayerProfile.m_worldData, nothing
    /// ever removes it, and the gate then refuses that character on your server forever. The
    /// damage is done at the moment the world loads, so the only real defence is refusing to
    /// load it.
    ///
    /// Keyed on the profile's filename rather than its display name, because two characters
    /// can share a name and the filename is what the game itself uses to tell them apart.
    /// </summary>
    internal static class Home
    {
        private static readonly Dictionary<string, long> _homes =
            new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

        private static bool _loaded;

        private static string HomePath => Path.Combine(Paths.ConfigPath, "boon-home.txt");

        /// <summary>The world this character belongs to, or 0 if it has not been bound yet.</summary>
        internal static long Get(string profileFilename)
        {
            Load();

            if (string.IsNullOrEmpty(profileFilename)) return 0L;
            return _homes.TryGetValue(profileFilename, out var uid) ? uid : 0L;
        }

        /// <summary>
        /// Bind a character to a world the first time it is seen there. Never overwrites: a
        /// character that is already bound elsewhere has a problem to be told about, not one
        /// to silently paper over by re-pointing it at wherever it happens to be now.
        /// </summary>
        internal static void Bind(string profileFilename, long worldUid)
        {
            Load();

            if (string.IsNullOrEmpty(profileFilename) || worldUid == 0L) return;

            if (_homes.TryGetValue(profileFilename, out var existing))
            {
                if (existing == worldUid) return;

                BoonPlugin.Log.LogWarning("Character '" + profileFilename + "' is bound to world " +
                                          existing + " but is currently in world " + worldUid +
                                          ". Too late to stop it - that world is now written into " +
                                          "the character. Edit " + HomePath + " if the binding is wrong.");
                return;
            }

            _homes[profileFilename] = worldUid;
            Save();

            BoonPlugin.Log.LogInfo("Bound character '" + profileFilename + "' to world " + worldUid +
                                   ". It will not be allowed to start a different world.");
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
                if (!long.TryParse(bits[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var uid)) continue;

                _homes[bits[0]] = uid;
            }
        }

        private static void Save()
        {
            try
            {
                var lines = new List<string>
                {
                    "# Which world each character belongs to: characterFile|worldUid",
                    "# Boon refuses to start a character in any other world, because doing so",
                    "# permanently records that world in the character and locks it out of its",
                    "# own server. Delete a line to unbind that character.",
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
