using HarmonyLib;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// Making the draft window actually usable takes four patches, not one - the same set
    /// devkit needed, and for the same reasons.
    ///
    /// Blocking input stops the player swinging an axe while choosing. Tripping
    /// InInventoryEtc stops the camera swinging. Neither frees the mouse: GameCamera
    /// re-locks and hides the cursor every frame unless one of ten named vanilla interfaces
    /// is visible, and a modded window is in none of them. Without the fourth patch this is
    /// a window you can look at and cannot click.
    /// </summary>
    internal static class UiInput
    {
        [HarmonyPostfix]
        [HarmonyPatch(typeof(Player), "TakeInput")]
        private static void BlockInput(ref bool __result)
        {
            // Composes with other mods doing the same: both return false, and false wins.
            if (DraftUI.IsOpen) __result = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerController), "TakeInput")]
        private static void BlockController(ref bool __result)
        {
            if (DraftUI.IsOpen) __result = false;
        }

        [HarmonyPostfix]
        [HarmonyPatch(typeof(PlayerController), "InInventoryEtc")]
        private static void HoldLookStill(ref bool __result)
        {
            if (DraftUI.IsOpen) __result = true;
        }

        /// <summary>
        /// A postfix rather than a prefix, because the method must still do its normal work
        /// for every other case - this only overrides the outcome while the window is up.
        /// </summary>
        [HarmonyPostfix]
        [HarmonyPatch(typeof(GameCamera), nameof(GameCamera.UpdateMouseCapture))]
        private static void FreeCursor()
        {
            if (!DraftUI.IsOpen) return;

            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// Escape defers the offer rather than opening the game's menu.
        ///
        /// Deferring rather than refusing: the pick is still owed and the same three come
        /// back, so nothing is lost. A modal that cannot be dismissed would be a window that
        /// gets you killed for opening at the wrong moment.
        /// </summary>
        [HarmonyPrefix]
        [HarmonyPatch(typeof(Menu), nameof(Menu.Show))]
        private static bool EscapeDefers()
        {
            if (!DraftUI.IsOpen) return true;

            DraftUI.Defer();
            return false;
        }
    }
}
