using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// What the local client currently believes about itself. Display and effects only -
    /// nothing here is authority, and the server never reads any of it back.
    /// </summary>
    internal static class ClientState
    {
        internal static float Xp;
        internal static int DraftsTaken;

        /// <summary>Card id to the levels that bought its ranks. A 0 is a rank whose level
        /// was never recorded - see BoonRecord.Taken.</summary>
        internal static readonly Dictionary<string, List<int>> Taken = new Dictionary<string, List<int>>();

        /// <summary>Ranks alone, rebuilt from Taken, because that is all Effects needs.</summary>
        internal static readonly Dictionary<string, int> Ranks = new Dictionary<string, int>();

        internal static bool Known;

        internal static int Level => Levels.LevelForXp(Xp);

        /// <summary>Picks earned and not yet spent. The server holds the same number and is
        /// the one that decides; this is for the panel to know what to offer.</summary>
        internal static int Owed => Mathf.Max(0, Level - DraftsTaken);

        internal static bool HasPick => Owed > 0;

        internal static int RankOf(string id)
        {
            return id != null && Ranks.TryGetValue(id, out var r) ? r : 0;
        }

        internal static List<int> LevelsOf(string id)
        {
            return id != null && Taken.TryGetValue(id, out var levels) ? levels : null;
        }

        /// <summary>
        /// Apply a pick locally the moment it is clicked, so the tile answers rather than
        /// waiting a round trip. Never authority: the server pushes the real state back on
        /// every pick, accepted or refused, and that overwrites this.
        ///
        /// The level guessed here is the same one the server will record - the level that
        /// granted the pick, DraftsTaken + 1 - so an accepted pick redraws identically and
        /// the correction is invisible.
        /// </summary>
        internal static void PredictTake(string id)
        {
            if (!Known || string.IsNullOrEmpty(id) || Owed <= 0) return;
            if (RankOf(id) >= BoonConfig.MaxRank.Value) return;

            if (!Taken.TryGetValue(id, out var levels))
            {
                levels = new List<int>();
                Taken[id] = levels;
            }

            levels.Add(DraftsTaken + 1);
            Ranks[id] = levels.Count;
            DraftsTaken++;
        }

        internal static void Clear()
        {
            Xp = 0f;
            DraftsTaken = 0;
            Taken.Clear();
            Ranks.Clear();
            Known = false;
        }

        internal static void FromWire(string wire)
        {
            Clear();
            if (string.IsNullOrEmpty(wire)) return;

            var parts = wire.Split('|');
            if (parts.Length < 3) return;

            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out Xp);
            int.TryParse(parts[1], out DraftsTaken);

            foreach (var entry in parts[2].Split(','))
            {
                if (entry.Length == 0) continue;

                var bits = entry.Split(':');
                if (bits.Length != 2) continue;

                var levels = new List<int>();
                foreach (var text in bits[1].Split(';'))
                {
                    if (text.Length == 0) continue;
                    if (int.TryParse(text, out var level)) levels.Add(level);
                }

                if (levels.Count == 0) continue;

                Taken[bits[0]] = levels;
                Ranks[bits[0]] = levels.Count;
            }

            Known = true;
        }
    }

    /// <summary>
    /// The wire between client and server.
    ///
    /// The split is deliberate and one-directional: the client reports that a skill went up
    /// and asks to take a card, and the server decides everything else. A client that lies
    /// about its level or hands itself a card is simply ignored, because the numbers it sends
    /// are never stored - only the events it claims, which the server then re-derives from.
    /// </summary>
    internal static class Net
    {
        private const string RpcHello = "Boon_Hello";
        private const string RpcSkillUp = "Boon_SkillUp";
        private const string RpcPick = "Boon_Pick";
        private const string RpcState = "Boon_State";
        private const string RpcAskProfile = "Boon_AskProfile";
        private const string RpcProfile = "Boon_Profile";
        private const string RpcNotice = "Boon_Notice";

        private static ZRoutedRpc _registeredOn;

        // Sliding-window report counters, per owner. A backstop, not a verification: skills
        // live on the client, so a report is a claim. This caps how fast a claim can pay.
        private static readonly Dictionary<string, Queue<float>> _reports =
            new Dictionary<string, Queue<float>>();

        /// <summary>
        /// Which character each connection is playing, learned from its hello.
        ///
        /// The ledger key needs both halves. The platform identity alone is per *account*, so
        /// every character on a machine shared one record and a new character inherited the
        /// last one's level and cards - which is exactly what happened the first time a
        /// character was remade. The character id alone comes from the client and could name
        /// someone else's record. Together, the platform half fences a player into their own
        /// account's records and the character half separates their characters within it.
        /// </summary>
        private static readonly Dictionary<long, long> _characters = new Dictionary<long, long>();

        internal static bool IsServer => ZNet.instance != null && ZNet.instance.IsServer();

        /// <summary>
        /// Idempotent and cheap. There is no single moment when the RPC layer exists, so this
        /// is called from Update and retried until it takes - the same shape the prefab
        /// registration recipes use.
        /// </summary>
        internal static void EnsureRegistered()
        {
            var rpc = ZRoutedRpc.instance;
            if (rpc == null) { _registeredOn = null; return; }
            if (ReferenceEquals(rpc, _registeredOn)) return;

            _registeredOn = rpc;
            _reports.Clear();
            _characters.Clear();

            rpc.Register<long>(RpcHello, OnHello);
            rpc.Register<int, float>(RpcSkillUp, OnSkillUp);
            rpc.Register<string>(RpcPick, OnPick);
            rpc.Register<string>(RpcState, OnState);
            rpc.Register(RpcAskProfile, OnAskProfile);
            rpc.Register<string, string>(RpcProfile, OnProfile);
            rpc.Register<string>(RpcNotice, OnNotice);

            ClientState.Clear();
            Effects.Reset();

            BoonPlugin.Log.LogInfo("RPCs registered (" + (IsServer ? "server" : "client") + ").");
        }

        // ---- client to server ----------------------------------------------------------

        /// <summary>
        /// Tell the server which character is being played. Sent once per session by every
        /// client, including the host - a routed RPC addressed to yourself is handled locally,
        /// so a listen server and singleplayer take exactly the same path as a remote client
        /// with no special casing.
        /// </summary>
        internal static void SayHello(long characterId)
        {
            if (ZRoutedRpc.instance == null || characterId == 0L) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcHello, characterId);
        }

        internal static void ReportSkillUp(Skills.SkillType type, float level)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcSkillUp, (int)type, level);
        }

        internal static void SendPick(string cardId)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(cardId)) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(RpcPick, cardId);
        }

        // ---- server handlers -----------------------------------------------------------

        /// <summary>
        /// A client naming the character it is playing. Everything else keys off this, so it
        /// is also where a joining player is handed their standing and asked about its history.
        /// </summary>
        private static void OnHello(long sender, long characterId)
        {
            if (!IsServer || !BoonConfig.Enabled.Value) return;

            var platform = PlatformOf(sender);
            if (platform == null || characterId == 0L) return;

            _characters[sender] = characterId;

            var rec = Ledger.For(Key(platform, characterId));
            if (rec == null) return;

            // Before anything is reported back, in case the catalogue has changed under a
            // record since it was last loaded.
            if (rec.Reconcile()) Ledger.Touch();

            BoonPlugin.Log.LogInfo("Hello from " + platform + " playing character " + characterId +
                                   " - level " + rec.Level + ", " + rec.Taken.Count + " cards, " +
                                   rec.Owed + " to spend.");

            PushState(sender, rec);

            if (BoonConfig.CheckSkillBaseline.Value) AskProfile(sender);
        }

        private static void OnSkillUp(long sender, int skillType, float skillLevel)
        {
            if (!IsServer || !BoonConfig.Enabled.Value) return;

            var owner = OwnerOf(sender);
            if (owner == null) return;

            // A skill cannot exceed 100 in vanilla, and a report claiming otherwise is either
            // a different mod or a forged packet. Either way it is not worth paying for.
            if (skillLevel <= 0f || skillLevel > 100f)
            {
                if (BoonConfig.Verbose.Value)
                    BoonPlugin.Log.LogWarning("Rejected skill-up from " + owner + ": level " + skillLevel);
                return;
            }

            if (!WithinRate(owner))
            {
                BoonPlugin.Log.LogWarning("Rate limit hit for " + owner +
                                          " - skill-up ignored. Either a very fast player or a forged report.");
                return;
            }

            // Untrusted characters play normally and simply earn nothing. This is the whole
            // of the consequence now - it used to be a kick - and it is checked here rather
            // than at the door because "should this gain be paid for" is the only question
            // Boon has any business answering.
            if (Gate.IsUntrusted(owner))
            {
                if (BoonConfig.Verbose.Value)
                    BoonPlugin.Log.LogInfo("Withheld XP from " + owner + " - skills not vouched for.");
                return;
            }

            var rec = Ledger.For(owner);
            if (rec == null) return;

            var before = rec.Level;
            rec.Xp += Levels.XpForSkillUp(skillLevel);

            // Keep the baseline current. Without this every level earned here would look
            // imported at the next join, and the gate would refuse the very players it just
            // watched earn it.
            if (!rec.Snapshot.TryGetValue(skillType, out var seen) || skillLevel > seen)
                rec.Snapshot[skillType] = skillLevel;

            Ledger.Touch();

            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo(owner + " +" + Levels.XpForSkillUp(skillLevel).ToString("0.#") +
                                       " xp (skill " + (Skills.SkillType)skillType + " " + skillLevel +
                                       ") -> " + rec.Xp.ToString("0.#"));

            if (rec.Level > before)
                BoonPlugin.Log.LogInfo(owner + " reached Boon level " + rec.Level + ".");

            PushState(sender, rec);
        }

        private static void OnPick(long sender, string cardId)
        {
            if (!IsServer || !BoonConfig.Enabled.Value) return;

            var owner = OwnerOf(sender);
            if (owner == null) return;

            var rec = Ledger.For(owner);
            if (rec == null) return;

            // With any card pickable, the authority is no longer "was this one of the three
            // we dealt" but simply "is a pick owed, and is there room in that card". The
            // count and the ceiling are both held here, so a client asking twice for one
            // pick, or asking past MaxRank, still gets nothing.
            if (rec.Owed <= 0)
            {
                BoonPlugin.Log.LogWarning("Rejected pick '" + cardId + "' from " + owner + " - nothing owed.");
                PushState(sender, rec);
                return;
            }

            var card = Cards.Get(cardId);
            if (card == null)
            {
                BoonPlugin.Log.LogWarning("Rejected pick '" + cardId + "' from " + owner + " - no such card.");
                PushState(sender, rec);
                return;
            }

            if (rec.RankOf(cardId) >= BoonConfig.MaxRank.Value)
            {
                BoonPlugin.Log.LogWarning("Rejected pick '" + cardId + "' from " + owner + " - already at max rank.");
                PushState(sender, rec);
                return;
            }

            rec.Take(cardId);
            Ledger.Touch();

            BoonPlugin.Log.LogInfo(owner + " took '" + card.Name + "' to rank " + rec.RankOf(cardId) +
                                   " with the level " + rec.DraftsTaken + " pick.");

            PushState(sender, rec);
        }

        private static void OnProfile(long sender, string facts, string skills)
        {
            if (!IsServer) return;
            Gate.Judge(sender, OwnerOf(sender), facts, skills);
        }

        internal static void PushState(long peerUid, BoonRecord rec)
        {
            if (ZRoutedRpc.instance == null || rec == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, RpcState, rec.ToWire());
        }

        internal static void AskProfile(long peerUid)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, RpcAskProfile);
        }

        /// <summary>
        /// The ledger key: platform identity, then the character being played. Null until the
        /// client has said hello, which is deliberate - a report that cannot be attributed to
        /// a character must not be paid to a guess.
        /// </summary>
        private static string OwnerOf(long sender)
        {
            if (!_characters.TryGetValue(sender, out var characterId))
            {
                if (BoonConfig.Verbose.Value)
                    BoonPlugin.Log.LogWarning("Message from an unidentified connection; ignored until it says hello.");
                return null;
            }

            var platform = PlatformOf(sender);
            return platform == null ? null : Key(platform, characterId);
        }

        /// <summary>
        /// Join the two halves with '@', never '|'.
        ///
        /// The ledger line is pipe-separated, so a key containing a pipe splits into extra
        /// fields and every value after it shifts along. The first version of the composite key
        /// used a pipe and wrote "v2|localhost|-608350150|0|0|||", which on reload parsed the
        /// character id as the XP total.
        /// </summary>
        private static string Key(string platform, long characterId)
        {
            return platform + "@" + characterId;
        }

        /// <summary>
        /// The platform identity of the connection, read from the peer's own socket. This is
        /// established before any of our code runs and cannot be claimed by the client, which
        /// is why it is half the key rather than trusting the client's word alone.
        /// </summary>
        private static string PlatformOf(long sender)
        {
            if (ZNet.instance == null) return null;

            var peer = ZNet.instance.GetPeer(sender);
            if (peer != null && peer.m_socket != null)
            {
                var host = peer.m_socket.GetHostName();

                // Neither separator may survive into a key: '|' splits the ledger line and '@'
                // splits the key itself.
                if (!string.IsNullOrEmpty(host)) return host.Replace("|", "_").Replace("@", "_");
            }

            // No peer means the sender is this process - a listen server's own host player.
            // A dedicated server never takes this branch.
            if (!ZNet.instance.IsDedicated()) return "localhost";

            BoonPlugin.Log.LogWarning("Could not identify the sender of a Boon RPC; ignored.");
            return null;
        }

        private static bool WithinRate(string owner)
        {
            if (!_reports.TryGetValue(owner, out var q))
            {
                q = new Queue<float>();
                _reports[owner] = q;
            }

            var now = Time.time;
            while (q.Count > 0 && now - q.Peek() > 60f) q.Dequeue();

            if (q.Count >= Mathf.Max(1f, BoonConfig.MaxSkillUpsPerMinute.Value)) return false;

            q.Enqueue(now);
            return true;
        }

        // ---- client handlers -----------------------------------------------------------

        private static void OnState(long sender, string wire)
        {
            var before = ClientState.Owed;

            ClientState.FromWire(wire);

            // Announce, never interrupt. The window used to open itself the moment a level
            // landed, which took the mouse and covered the screen mid-fight.
            if (ClientState.Owed > before)
            {
                var player = Player.m_localPlayer;
                if (player != null)
                    player.Message(MessageHud.MessageType.Center, BoonPanel.OpenHint());
            }

            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo("State: level " + ClientState.Level + ", " +
                                       ClientState.Ranks.Count + " cards, " +
                                       ClientState.Owed + " to spend.");
        }

        private static void OnAskProfile(long sender)
        {
            if (ZRoutedRpc.instance == null) return;

            // The facts slot is sent empty and kept only so the wire shape does not change.
            // It used to carry which other worlds this character had visited, which is now
            // Threshold's question - and it was always a strange thing for a levelling mod to
            // be asking, since it decided whether you could play rather than whether you earned.
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcProfile, "", Gate.LocalSkills());
        }

        /// <summary>
        /// Tell one player something, on their screen.
        ///
        /// Added because withholding XP is invisible by nature: nothing happens, you simply
        /// stop earning, and there is no moment where the game tells you why. A kick at least
        /// announced itself.
        /// </summary>
        internal static void SendNotice(long peerUid, string message)
        {
            if (ZRoutedRpc.instance == null || string.IsNullOrEmpty(message)) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(peerUid, RpcNotice, message);
        }

        private static void OnNotice(long sender, string message)
        {
            if (string.IsNullOrEmpty(message)) return;

            BoonPlugin.Log.LogWarning("Boon: " + message);

            var player = Player.m_localPlayer;
            if (player != null) player.Message(MessageHud.MessageType.Center, message);
        }
    }
}
