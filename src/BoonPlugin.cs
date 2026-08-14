using System.Collections.Generic;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace Boon
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInProcess("valheim.exe")]
    public class BoonPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.boon";
        public const string PluginName = "Boon";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        /// <summary>Peers already greeted this session: state pushed and profile asked for.</summary>
        private readonly HashSet<long> _greeted = new HashSet<long>();

        private void Awake()
        {
            Log = Logger;
            BoonConfig.Bind(Config);
            Cards.Load();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(SkillWatch));
            _harmony.PatchAll(typeof(DeathPenalty));
            _harmony.PatchAll(typeof(UiInput));

            Log.LogInfo(PluginName + " " + PluginVersion + " by " + PluginAuthor + " - ready.");
        }

        private void OnDestroy()
        {
            Ledger.Flush();
            if (_harmony != null) _harmony.UnpatchSelf();
        }

        private void Update()
        {
            // ZRoutedRpc does not exist at load and comes and goes with each session, so
            // registration is retried from here and is cheap once it has taken.
            Net.EnsureRegistered();

            if (!BoonConfig.Enabled.Value) return;

            if (Net.IsServer)
            {
                Ledger.Tick(Time.time);
                GreetPeers();
                MirrorLocalHost();
            }

            var player = Player.m_localPlayer;
            if (player != null && ClientState.Known) Effects.Apply(player, ClientState.Ranks);
        }

        private void OnGUI()
        {
            DraftUI.Draw();
        }

        /// <summary>
        /// Push a joining player their standing, and ask their client what the character has
        /// been up to. Both happen once per peer per session.
        /// </summary>
        private void GreetPeers()
        {
            if (ZNet.instance == null) return;

            var peers = ZNet.instance.GetPeers();
            if (peers == null) return;

            var live = new HashSet<long>();

            foreach (var peer in peers)
            {
                if (peer == null || !peer.IsReady()) continue;
                live.Add(peer.m_uid);

                if (!_greeted.Add(peer.m_uid)) continue;

                var owner = peer.m_socket != null ? peer.m_socket.GetHostName() : null;
                if (string.IsNullOrEmpty(owner)) continue;

                var rec = Ledger.For(owner.Replace("|", "_"));
                if (rec == null) continue;

                if (rec.Owed > 0 && rec.Offer.Count == 0) rec.RollOffer();

                Net.PushState(peer.m_uid, rec);

                if (BoonConfig.RequireFreshCharacter.Value) Net.AskProfile(peer.m_uid);
            }

            // Forget peers that have gone, so a reconnect is greeted again.
            _greeted.RemoveWhere(uid => !live.Contains(uid));
        }

        /// <summary>
        /// On a listen server or in singleplayer the host has no peer entry for itself, so
        /// its state is read straight from the ledger rather than sent over the wire.
        /// </summary>
        private void MirrorLocalHost()
        {
            if (ZNet.instance == null || ZNet.instance.IsDedicated()) return;
            if (Player.m_localPlayer == null) return;

            var rec = Ledger.For("localhost");
            if (rec == null) return;

            if (rec.Owed > 0 && rec.Offer.Count == 0) { rec.RollOffer(); Ledger.Touch(); }
            else if (rec.Owed <= 0 && rec.Offer.Count > 0) { rec.Offer.Clear(); Ledger.Touch(); }

            ClientState.FromWire(rec.ToWire());
        }
    }
}
