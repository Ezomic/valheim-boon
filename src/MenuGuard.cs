using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;

namespace Boon
{
    /// <summary>
    /// Refuses to start a world that would damage the selected character.
    ///
    /// FejdStartup.OnWorldStart is the commit point for a local world, and it is the last
    /// moment anything can be done. Once the world loads, PlayerProfile.m_worldData gains an
    /// entry for it, nothing in the game ever removes that entry, and the fresh-character gate
    /// will refuse the character on its own server from then on. There is no undo short of
    /// restoring a backup taken beforehand - which is precisely what nobody has when they
    /// needed one.
    ///
    /// So the block goes here, in front of the door, rather than anywhere after it.
    ///
    /// Only local worlds are covered. Joining a server cannot be checked the same way, because
    /// the world's identity is not known until after connecting - but that path is far less
    /// dangerous, since the server's own gate refuses the character before it ever spawns.
    /// </summary>
    internal static class MenuGuard
    {
        private static FieldInfo _world, _profiles, _profileIndex;

        [HarmonyPrefix]
        [HarmonyPatch(typeof(FejdStartup), nameof(FejdStartup.OnWorldStart))]
        private static bool GuardWorldStart(FejdStartup __instance)
        {
            if (!BoonConfig.ProtectCharacter.Value) return true;

            var world = SelectedWorld(__instance);
            var profile = SelectedProfile(__instance);
            if (world == null || profile == null) return true;

            var home = Home.Get(profile.m_filename);

            // Nothing recorded means nothing to protect yet - the character has not been
            // accepted anywhere, so any world is a legitimate first one.
            if (home == 0L || home == world.m_uid) return true;

            BoonPlugin.Log.LogWarning("Refused to start world '" + world.m_name + "' (" + world.m_uid +
                                      ") with character '" + profile.GetName() + "' - it belongs to world " +
                                      home + ".");

            UnifiedPopup.Push(new WarningPopup(
                "Boon: wrong world for this character",
                profile.GetName() + " belongs to a different world.\n\n" +
                "Loading '" + world.m_name + "' would permanently record that world in this " +
                "character and lock it out of its own server. There is no way to undo it " +
                "except restoring a backup.\n\n" +
                "Pick another character, or edit boon-home.txt if this binding is wrong.",
                UnifiedPopup.Pop,
                localizeText: false));

            return false;
        }

        private static World SelectedWorld(FejdStartup fejd)
        {
            if (_world == null) _world = AccessTools.Field(typeof(FejdStartup), "m_world");
            return _world != null ? _world.GetValue(fejd) as World : null;
        }

        private static PlayerProfile SelectedProfile(FejdStartup fejd)
        {
            if (_profiles == null) _profiles = AccessTools.Field(typeof(FejdStartup), "m_profiles");
            if (_profileIndex == null) _profileIndex = AccessTools.Field(typeof(FejdStartup), "m_profileIndex");
            if (_profiles == null || _profileIndex == null) return null;

            if (!(_profiles.GetValue(fejd) is List<PlayerProfile> list)) return null;

            var index = (int)_profileIndex.GetValue(fejd);
            if (index < 0 || index >= list.Count) return null;

            return list[index];
        }
    }
}
