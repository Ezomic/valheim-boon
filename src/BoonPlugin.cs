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
            _harmony.PatchAll(typeof(MenuGuard));

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
                SeedLocalHost();
            }

            var player = Player.m_localPlayer;
            if (player == null) return;

            BindHome();

            if (ClientState.Known) Effects.Apply(player, ClientState.Ranks);
        }

        private void OnGUI()
        {
            DraftUI.Draw();
        }

        /// <summary>
        /// Remember which world this character belongs to, the first time it is seen in one.
        /// After this the menu will refuse to start it anywhere else, which is the only point
        /// at which that can still be prevented.
        /// </summary>
        private bool _bound;

        private void BindHome()
        {
            if (!BoonConfig.ProtectCharacter.Value) return;

            // Cleared when leaving a world, so joining another one binds again rather than
            // being skipped for the rest of the process.
            if (ZNet.instance == null || Game.instance == null) { _bound = false; return; }
            if (_bound) return;

            var uid = ZNet.instance.GetWorldUID();
            if (uid == 0L) return;

            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return;

            _bound = true;
            Home.Bind(profile.m_filename, uid);
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
        /// Singleplayer and listen servers work without any special casing, because
        /// ZRoutedRpc handles a message addressed to yourself locally:
        ///
        ///     if (targetPeerID == m_id || targetPeerID == 0L) HandleRoutedRPC(data);
        ///
        /// and GetServerPeerID returns m_id when you are the server. So skill reports, picks
        /// and state pushes all loop straight back and resolve against the local ledger.
        ///
        /// The one thing that does not happen is the greeting: the host has no peer entry for
        /// itself, so GreetPeers never sees it and nothing seeds the opening state. That is
        /// all this does, once. Everything after arrives through the same path as a client's.
        /// </summary>
        private void SeedLocalHost()
        {
            if (ZNet.instance == null || ZNet.instance.IsDedicated()) return;
            if (Player.m_localPlayer == null) return;

            // Known is cleared when the RPC layer re-registers, so this re-seeds once per
            // session and is otherwise a single comparison per frame. An earlier version
            // rebuilt the whole state from strings every frame, which allocated two strings
            // and a dictionary per frame for no gain.
            if (ClientState.Known) return;

            var rec = Ledger.For("localhost");
            if (rec == null) return;

            if (rec.Owed > 0 && rec.Offer.Count == 0) { rec.RollOffer(); Ledger.Touch(); }
            else if (rec.Owed <= 0 && rec.Offer.Count > 0) { rec.Offer.Clear(); Ledger.Touch(); }

            ClientState.FromWire(rec.ToWire());
        }
    }
}
