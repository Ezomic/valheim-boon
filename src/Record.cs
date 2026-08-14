using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Boon
{
    /// <summary>
    /// One player's standing with Boon: how much XP they have earned on this server, which
    /// cards they hold and at what rank, and which three they are currently being offered.
    ///
    /// Held by the server, never by the character. That is the whole anti-cheat premise - a
    /// character file sits on the player's own disk and can be edited, so nothing that
    /// decides rewards may live there.
    ///
    /// Keyed by <see cref="Owner"/>, the platform user id read from the peer's own socket
    /// server-side (ISocket.GetHostName). Deliberately not the character's Player.GetPlayerID:
    /// that arrives from the client and could name someone else's record. The socket identity
    /// is established by the platform before any of our code runs, so it cannot be claimed.
    /// </summary>
    internal sealed class BoonRecord
    {
        internal string Owner = "";
        internal float Xp;

        /// <summary>Levels already paid out as draft offers. Lags Level when picks are owed.</summary>
        internal int DraftsTaken;

        /// <summary>Card id to rank.</summary>
        internal readonly Dictionary<string, int> Ranks = new Dictionary<string, int>();

        /// <summary>The ids currently on the table. Empty when nothing is owed.</summary>
        internal readonly List<string> Offer = new List<string>();

        /// <summary>
        /// The highest level this server has itself watched each skill reach, keyed by
        /// (int)Skills.SkillType.
        ///
        /// This is the answer to the one hole the travel check cannot close. A character file
        /// is client-side and can be hand-edited to strip its world history, but it cannot
        /// reach this - the baseline lives here. A character coming back with a skill above
        /// what this server saw it reach gained that level somewhere else, whatever its file
        /// claims about where it has been.
        /// </summary>
        internal readonly Dictionary<int, float> Snapshot = new Dictionary<int, float>();

        internal bool HasSnapshot => Snapshot.Count > 0;

        internal int Level => Levels.LevelForXp(Xp);

        /// <summary>How many picks the player still owes, and so how many drafts to run.</summary>
        internal int Owed => Math.Max(0, Level - DraftsTaken);

        internal int RankOf(string id)
        {
            return id != null && Ranks.TryGetValue(id, out var r) ? r : 0;
        }

        /// <summary>
        /// Roll the cards on offer.
        ///
        /// Seeded from the owner and the draft number rather than left to chance, so that
        /// quitting on a bad offer and coming back re-offers the same three. Without that the
        /// pick is theatre - anyone can reroll until they get what they wanted.
        /// </summary>
        internal void RollOffer()
        {
            Offer.Clear();

            var pool = new List<Card>();
            foreach (var card in Cards.All)
            {
                if (RankOf(card.Id) < BoonConfig.MaxRank.Value) pool.Add(card);
            }

            if (pool.Count == 0) return;

            var seed = unchecked((Owner ?? "").GetStableHashCode() * 31 + DraftsTaken * 92821);
            var rng = new System.Random(seed);

            var want = Math.Min(BoonConfig.OfferCount.Value, pool.Count);
            for (var i = 0; i < want; i++)
            {
                var pick = rng.Next(pool.Count);
                Offer.Add(pool[pick].Id);
                pool.RemoveAt(pick);
            }
        }

        // ---- serialisation -------------------------------------------------------------
        //
        // A flat line rather than JSON: the ledger is read and written by this mod alone, it
        // has to survive being opened in a text editor on a server, and a dependency for
        // four fields is not worth it.
        //
        //   v2|owner|xp|draftsTaken|id:rank,id:rank|offerId,offerId|skillType:level,...
        //
        // v1 lines are the same without the trailing snapshot and are still read, so a ledger
        // written before the snapshot existed keeps working - those players simply have their
        // baseline adopted on next join.

        internal string Serialise()
        {
            var sb = new StringBuilder();
            sb.Append("v2|").Append(Owner).Append('|')
              .Append(Xp.ToString("R", CultureInfo.InvariantCulture)).Append('|')
              .Append(DraftsTaken).Append('|');

            AppendRanks(sb);
            sb.Append('|').Append(string.Join(",", Offer.ToArray())).Append('|');

            var first = true;
            foreach (var kv in Snapshot)
            {
                if (!first) sb.Append(',');
                sb.Append(kv.Key).Append(':').Append(kv.Value.ToString("R", CultureInfo.InvariantCulture));
                first = false;
            }

            return sb.ToString();
        }

        internal static BoonRecord Parse(string line)
        {
            if (string.IsNullOrEmpty(line)) return null;

            var parts = line.Split('|');
            if (parts.Length < 6) return null;
            if (parts[0] != "v1" && parts[0] != "v2") return null;
            if (parts[1].Length == 0) return null;
            if (!float.TryParse(parts[2], NumberStyles.Float, CultureInfo.InvariantCulture, out var xp)) return null;
            if (!int.TryParse(parts[3], out var drafts)) return null;

            var rec = new BoonRecord { Owner = parts[1], Xp = xp, DraftsTaken = drafts };

            foreach (var pair in parts[4].Split(','))
            {
                if (pair.Length == 0) continue;
                var bits = pair.Split(':');
                if (bits.Length != 2) continue;
                if (!int.TryParse(bits[1], out var rank)) continue;
                rec.Ranks[bits[0]] = rank;
            }

            foreach (var offerId in parts[5].Split(','))
            {
                if (offerId.Length > 0) rec.Offer.Add(offerId);
            }

            if (parts[0] == "v2" && parts.Length >= 7)
            {
                foreach (var pair in parts[6].Split(','))
                {
                    if (pair.Length == 0) continue;
                    var bits = pair.Split(':');
                    if (bits.Length != 2) continue;
                    if (!int.TryParse(bits[0], out var type)) continue;
                    if (!float.TryParse(bits[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var level)) continue;
                    rec.Snapshot[type] = level;
                }
            }

            return rec;
        }

        /// <summary>
        /// The form sent to the client for display. Deliberately not the ledger line: the
        /// client never needs the owner id, and must never be handed anything it could send
        /// back as authority.
        ///
        ///   xp|draftsTaken|id:rank,id:rank|offerId,offerId
        /// </summary>
        internal string ToWire()
        {
            var sb = new StringBuilder();
            sb.Append(Xp.ToString("R", CultureInfo.InvariantCulture)).Append('|').Append(DraftsTaken).Append('|');
            AppendRanks(sb);
            sb.Append('|').Append(string.Join(",", Offer.ToArray()));
            return sb.ToString();
        }

        private void AppendRanks(StringBuilder sb)
        {
            var first = true;
            foreach (var kv in Ranks)
            {
                if (!first) sb.Append(',');
                sb.Append(kv.Key).Append(':').Append(kv.Value);
                first = false;
            }
        }
    }
}
