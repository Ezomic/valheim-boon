using System;
using System.Collections.Generic;
using System.IO;
using BepInEx;

namespace Boon
{
    /// <summary>
    /// The server's record of every player, on the server's own disk.
    ///
    /// This is the point of the whole design. Valheim keeps characters client-side - even
    /// ZNet.SaveOtherPlayerProfiles only sends each client an RPC telling it to save its own
    /// profile locally - so the server never sees a character file and anything stored there
    /// is forgeable. Levels and cards therefore live here, keyed by the platform identity of
    /// the connection, where a client cannot reach them.
    /// </summary>
    internal static class Ledger
    {
        private static readonly Dictionary<string, BoonRecord> _records =
            new Dictionary<string, BoonRecord>();

        private static bool _loaded;
        private static bool _dirty;
        private static float _nextFlush;

        private static string LedgerPath => Path.Combine(Paths.ConfigPath, "boon-ledger.txt");

        internal static BoonRecord For(string owner)
        {
            Load();

            if (string.IsNullOrEmpty(owner)) return null;

            if (!_records.TryGetValue(owner, out var rec))
            {
                rec = new BoonRecord { Owner = owner };
                _records[owner] = rec;
                _dirty = true;
            }

            return rec;
        }

        internal static void Touch()
        {
            _dirty = true;
        }

        private static void Load()
        {
            if (_loaded) return;
            _loaded = true;

            if (!File.Exists(LedgerPath))
            {
                BoonPlugin.Log.LogInfo("No ledger yet; starting a fresh one at " + LedgerPath);
                return;
            }

            var bad = 0;
            foreach (var line in File.ReadAllLines(LedgerPath))
            {
                if (line.Trim().Length == 0) continue;

                var rec = BoonRecord.Parse(line);
                if (rec == null) { bad++; continue; }
                _records[rec.Owner] = rec;
            }

            BoonPlugin.Log.LogInfo("Ledger loaded: " + _records.Count + " players" +
                                   (bad > 0 ? ", " + bad + " unreadable lines skipped" : "") + ".");
        }

        /// <summary>
        /// Write if anything changed. On a timer rather than on every grant, because skill-ups
        /// can arrive several times a second and the file would be rewritten each time.
        /// </summary>
        internal static void Tick(float time)
        {
            if (!_dirty || time < _nextFlush) return;
            _nextFlush = time + 10f;
            Flush();
        }

        internal static void Flush()
        {
            if (!_dirty || !_loaded) return;

            try
            {
                var lines = new List<string>(_records.Count);
                foreach (var rec in _records.Values) lines.Add(rec.Serialise());

                // Write beside and move into place, so a crash mid-write cannot leave every
                // player's progress truncated.
                var tmp = LedgerPath + ".tmp";
                File.WriteAllLines(tmp, lines.ToArray());
                if (File.Exists(LedgerPath)) File.Delete(LedgerPath);
                File.Move(tmp, LedgerPath);

                _dirty = false;
            }
            catch (Exception e)
            {
                BoonPlugin.Log.LogError("Could not write the ledger: " + e.Message);
            }
        }
    }
}
