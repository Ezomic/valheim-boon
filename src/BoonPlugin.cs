using BepInEx;
using BepInEx.Logging;
using Ezomic.Core;
using HarmonyLib;
using UnityEngine;

namespace Boon
{
    [BepInPlugin(PluginGuid, PluginName, PluginVersion)]
    [BepInDependency("ezomic.valheim.core", BepInDependency.DependencyFlags.HardDependency)]
    // No BepInProcess. It used to say valheim.exe, which is a whitelist - and a dedicated
    // server runs valheim_server.exe, so the entire server half of this mod would never have
    // loaded there. The ledger, the gate and every authority decision live on the server; on a
    // dedicated host none of them would have existed and clients would have reported skill-ups
    // into nothing. Core and Wither were both bitten by exactly this.
    public class BoonPlugin : BaseUnityPlugin
    {
        public const string PluginGuid = "ezomic.valheim.boon";
        public const string PluginName = "Boon";
        public const string PluginVersion = "0.1.0";
        public const string PluginAuthor = "Robbin Thijssen";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private bool _saidHello;
        private bool _bound;
        private bool _keyHeld;

        private void Awake()
        {
            Log = Logger;
            BoonConfig.Bind(Config);
            // Everyone, not HostOnly. Both ends have to agree about this mod, and the
            // disagreement is silent when they do not: a client that cannot resolve a prefab
            // hash discards the ZDO rather than erroring - destroying what is already standing
            // in the world - and item data that differs desyncs inventories.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config);
            Cards.Load();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(SkillWatch));
            _harmony.PatchAll(typeof(DeathPenalty));
            _harmony.PatchAll(typeof(UiInput));
            _harmony.PatchAll(typeof(MenuGuard));
            _harmony.PatchAll(typeof(AttackSpeed));

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

            if (Net.IsServer) Ledger.Tick(Time.time);

            var player = Player.m_localPlayer;
            if (player == null)
            {
                // Left the world; the next one starts the introductions again.
                _saidHello = false;
                _bound = false;
                return;
            }

            SayHello();
            BindHome();
            ReadKey();

            if (ClientState.Known) Effects.Apply(player, ClientState.Ranks);

            // The cloned bar lives in the HUD canvas rather than in OnGUI, so it is driven
            // from here. It rebuilds itself whenever the Hud is, which is once per world.
            HudBar.Update();

            // Both hang off the inventory window rather than the HUD: the extra rows Deep pack
            // adds need the wooden backdrop grown to cover them, and the compendium bar needs
            // its fifth tab put back whenever that window is rebuilt.
            InventoryPanel.Update();
            InfoTab.Update();
        }

        private void OnGUI()
        {
            XpBar.Draw();
            BoonPanel.Draw();
        }

        /// <summary>
        /// Name the character being played, once per session.
        ///
        /// This is what the server keys the ledger on, together with the platform identity it
        /// reads off the socket itself. Sending it as a routed RPC rather than special-casing
        /// the host means singleplayer takes the identical path: a message addressed to
        /// yourself is handled locally, so the host introduces itself to itself.
        /// </summary>
        private void SayHello()
        {
            if (_saidHello || ZRoutedRpc.instance == null || Game.instance == null) return;

            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return;

            var id = Home.IdOf(profile);
            if (id == 0L) return;

            _saidHello = true;
            Net.SayHello(id);
        }

        /// <summary>
        /// Remember which world this character belongs to, the first time it is seen in one.
        /// After this the menu refuses to start it anywhere else, which is the only point at
        /// which that can still be prevented.
        /// </summary>
        private void BindHome()
        {
            if (_bound || !BoonConfig.ProtectCharacter.Value) return;
            if (ZNet.instance == null || Game.instance == null) return;

            var uid = ZNet.instance.GetWorldUID();
            if (uid == 0L) return;

            var profile = Game.instance.GetPlayerProfile();
            if (profile == null) return;

            _bound = true;
            Home.Bind(Home.IdOf(profile), profile.GetName(), uid);
        }

        /// <summary>
        /// Edge-triggered by hand, the same way Tether does it: a held key would otherwise
        /// open and close the window several times a second.
        /// </summary>
        private void ReadKey()
        {
            var down = BoonConfig.KeyBoon.Value.IsDown();
            if (!down) { _keyHeld = false; return; }
            if (_keyHeld) return;
            _keyHeld = true;

            // The chest window and the game menu both own the cursor already; opening over
            // them would fight for it.
            if (InventoryGui.IsVisible() || Menu.IsVisible()) return;

            BoonPanel.Toggle();
        }
    }
}
