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
        public const string PluginVersion = "1.0.0";
        public const string PluginAuthor = "Robbin Thijssen";

        internal static ManualLogSource Log;

        private Harmony _harmony;

        private bool _saidHello;

        private void Awake()
        {
            Log = Logger;
            BoonConfig.Bind(Config);
            // Everyone, not HostOnly. Both ends have to agree about this mod, and the
            // disagreement is silent when they do not: a client that cannot resolve a prefab
            // hash discards the ZDO rather than erroring - destroying what is already standing
            // in the world - and item data that differs desyncs inventories.
            Suite.Register(PluginGuid, PluginName, PluginVersion, Config);

            // The host's curve is the one that counts. Without this a client with a different
            // LevelBaseXp reads a different level out of the same xp, and every number on its
            // screen disagrees with the server that decides them - which is how a cheapened
            // test curve on one machine made a dedicated server refuse every pick.
            Suite.Sync(BoonConfig.XpPerSkillLevel, BoonConfig.LevelBaseXp, BoonConfig.LevelExponent,
                       BoonConfig.MaxRank, BoonConfig.BonusEvery);
            Cards.Load();

            _harmony = new Harmony(PluginGuid);
            _harmony.PatchAll(typeof(SkillWatch));
            _harmony.PatchAll(typeof(DeathPenalty));
            _harmony.PatchAll(typeof(UiInput));
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
                return;
            }

            SayHello();

            if (ClientState.Known) Effects.Apply(player, ClientState.Ranks);

            // The cloned bar lives in the HUD canvas rather than in OnGUI, so it is driven
            // from here. It rebuilds itself whenever the Hud is, which is once per world.
            HudBar.Update();

            // The compendium bar needs its fifth tab put back whenever the inventory window
            // is rebuilt. The extra rows and the backdrop behind them are Core's, because two
            // mods claiming rows must not each write the same private int.
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

            var id = PlayerIdOf(profile);
            if (id == 0L) return;

            _saidHello = true;
            Net.SayHello(id);
        }

        /// <summary>
        /// The character's unique id, which is half of the ledger key.
        ///
        /// Kept here rather than shared, now that the character-protection code this used to
        /// sit beside has moved to Threshold. It is four lines of reflection; a dependency
        /// between two unrelated mods would cost more than the duplication does.
        ///
        /// m_playerID rather than the profile's filename: the filename is the character's
        /// name, which is reused the moment a character is deleted and another made with the
        /// same name, and keying on it once gave a brand new character an older one's record.
        /// </summary>
        private static long PlayerIdOf(PlayerProfile profile)
        {
            if (profile == null) return 0L;

            if (_playerId == null) _playerId = AccessTools.Field(typeof(PlayerProfile), "m_playerID");
            if (_playerId == null)
            {
                Log.LogError("PlayerProfile.m_playerID not found - Boon cannot identify characters.");
                return 0L;
            }

            return _playerId.GetValue(profile) is long id ? id : 0L;
        }

        private static System.Reflection.FieldInfo _playerId;

    }
}
