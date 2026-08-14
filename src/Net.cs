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
        internal static readonly Dictionary<string, int> Ranks = new Dictionary<string, int>();
        internal static readonly List<string> Offer = new List<string>();
        internal static bool Known;

        internal static int Level => Levels.LevelForXp(Xp);
        internal static bool HasOffer => Offer.Count > 0;

        internal static int RankOf(string id)
        {
            return id != null && Ranks.TryGetValue(id, out var r) ? r : 0;
        }

        internal static void Clear()
        {
            Xp = 0f;
            DraftsTaken = 0;
            Ranks.Clear();
            Offer.Clear();
            Known = false;
        }

        internal static void FromWire(string wire)
        {
            Clear();
            if (string.IsNullOrEmpty(wire)) return;

            var parts = wire.Split('|');
            if (parts.Length < 4) return;

            float.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out Xp);
            int.TryParse(parts[1], out DraftsTaken);

            foreach (var pair in parts[2].Split(','))
            {
                if (pair.Length == 0) continue;
                var bits = pair.Split(':');
                if (bits.Length != 2) continue;
                if (!int.TryParse(bits[1], out var rank)) continue;
                Ranks[bits[0]] = rank;
            }

            foreach (var id in parts[3].Split(','))
            {
                if (id.Length > 0) Offer.Add(id);
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
        private const string RpcSkillUp = "Boon_SkillUp";
        private const string RpcPick = "Boon_Pick";
        private const string RpcState = "Boon_State";
        private const string RpcAskProfile = "Boon_AskProfile";
        private const string RpcProfile = "Boon_Profile";

        private static ZRoutedRpc _registeredOn;

        // Sliding-window report counters, per owner. A backstop, not a verification: skills
        // live on the client, so a report is a claim. This caps how fast a claim can pay.
        private static readonly Dictionary<string, Queue<float>> _reports =
            new Dictionary<string, Queue<float>>();

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

            rpc.Register<int, float>(RpcSkillUp, OnSkillUp);
            rpc.Register<string>(RpcPick, OnPick);
            rpc.Register<string>(RpcState, OnState);
            rpc.Register(RpcAskProfile, OnAskProfile);
            rpc.Register<string>(RpcProfile, OnProfile);

            ClientState.Clear();
            Effects.Reset();

            BoonPlugin.Log.LogInfo("RPCs registered (" + (IsServer ? "server" : "client") + ").");
        }

        // ---- client to server ----------------------------------------------------------

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

            var rec = Ledger.For(owner);
            if (rec == null) return;

            var before = rec.Level;
            rec.Xp += Levels.XpForSkillUp(skillLevel);
            Ledger.Touch();

            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo(owner + " +" + Levels.XpForSkillUp(skillLevel).ToString("0.#") +
                                       " xp (skill " + (Skills.SkillType)skillType + " " + skillLevel +
                                       ") -> " + rec.Xp.ToString("0.#"));

            if (rec.Level > before)
                BoonPlugin.Log.LogInfo(owner + " reached Boon level " + rec.Level + ".");

            EnsureOffer(rec);
            PushState(sender, rec);
        }

        private static void OnPick(long sender, string cardId)
        {
            if (!IsServer || !BoonConfig.Enabled.Value) return;

            var owner = OwnerOf(sender);
            if (owner == null) return;

            var rec = Ledger.For(owner);
            if (rec == null) return;

            // The offer is the authority, not the pick. A client asking for a card it was not
            // shown - or asking twice for one it was - gets nothing.
            if (rec.Owed <= 0 || !rec.Offer.Contains(cardId))
            {
                BoonPlugin.Log.LogWarning("Rejected pick '" + cardId + "' from " + owner +
                                          " - not on offer" + (rec.Owed <= 0 ? " and nothing owed" : "") + ".");
                PushState(sender, rec);
                return;
            }

            var card = Cards.Get(cardId);
            if (card == null) { PushState(sender, rec); return; }

            var rank = rec.RankOf(cardId);
            if (rank >= BoonConfig.MaxRank.Value) { PushState(sender, rec); return; }

            rec.Ranks[cardId] = rank + 1;
            rec.DraftsTaken++;
            rec.Offer.Clear();
            Ledger.Touch();

            BoonPlugin.Log.LogInfo(owner + " took '" + card.Name + "' to rank " + rec.Ranks[cardId] + ".");

            EnsureOffer(rec);
            PushState(sender, rec);
        }

        private static void OnProfile(long sender, string facts)
        {
            if (!IsServer) return;
            Gate.Evaluate(sender, OwnerOf(sender), facts);
        }

        /// <summary>Roll a fresh offer if one is owed and none is standing.</summary>
        private static void EnsureOffer(BoonRecord rec)
        {
            if (rec.Owed > 0 && rec.Offer.Count == 0)
            {
                rec.RollOffer();
                Ledger.Touch();
            }
            else if (rec.Owed <= 0 && rec.Offer.Count > 0)
            {
                rec.Offer.Clear();
                Ledger.Touch();
            }
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
        /// The platform identity of the connection, read from the peer's own socket. This is
        /// established before any of our code runs and cannot be claimed by the client, which
        /// is exactly why the ledger is keyed on it rather than on Player.GetPlayerID.
        /// </summary>
        private static string OwnerOf(long sender)
        {
            if (ZNet.instance == null) return null;

            var peer = ZNet.instance.GetPeer(sender);
            if (peer != null && peer.m_socket != null)
            {
                var host = peer.m_socket.GetHostName();
                if (!string.IsNullOrEmpty(host)) return host.Replace("|", "_");
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
            ClientState.FromWire(wire);

            if (BoonConfig.Verbose.Value)
                BoonPlugin.Log.LogInfo("State: level " + ClientState.Level + ", " +
                                       ClientState.Ranks.Count + " cards, " +
                                       ClientState.Offer.Count + " on offer.");
        }

        private static void OnAskProfile(long sender)
        {
            if (ZRoutedRpc.instance == null) return;
            ZRoutedRpc.instance.InvokeRoutedRPC(sender, RpcProfile, Gate.LocalFacts());
        }
    }
}
