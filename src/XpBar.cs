using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The always-on experience bar, stacked under the vanilla health and stamina bars so it
    /// reads as one of them rather than as something bolted on.
    ///
    /// Position is config rather than computed. Anchoring to the real health bar would mean
    /// converting a scaled Canvas RectTransform into IMGUI screen space, which breaks
    /// differently at every HUD scale and resolution; a pair of numbers anyone can nudge is
    /// both simpler and easier to correct when it lands wrong.
    /// </summary>
    internal static class XpBar
    {
        private static Texture2D _track, _fill;
        private static GUIStyle _label;

        private static readonly Color TrackColour = new Color(0.227f, 0.188f, 0.145f, 0.9f);
        private static readonly Color FillColour = new Color(0.83f, 0.663f, 0.29f, 1f);

        internal static void Draw()
        {
            if (!BoonConfig.Enabled.Value || !BoonConfig.ShowXpBar.Value) return;
            if (!ClientState.Known) return;

            var player = Player.m_localPlayer;
            if (player == null || player.IsDead()) return;

            // Follow the game's own idea of whether the interface is up: hidden while a menu
            // or a container is open, and while the player has pressed the hide-HUD key.
            if (InventoryGui.IsVisible() || Menu.IsVisible()) return;
            if (Hud.instance != null && Hud.instance.m_userHidden) return;

            Build();

            var x = BoonConfig.BarX.Value;
            var w = Mathf.Max(20f, BoonConfig.BarWidth.Value);
            var h = Mathf.Max(2f, BoonConfig.BarHeight.Value);

            // Anchored to the bottom, because that is the corner it belongs in - health and
            // stamina sit bottom-left and this goes under them. IMGUI measures from the top,
            // so the offset is subtracted rather than used directly.
            var y = Screen.height - BoonConfig.BarBottom.Value;

            GUI.DrawTexture(new Rect(x, y, w, h), _track);

            var progress = Mathf.Clamp01(Levels.Progress(ClientState.Xp));
            if (progress > 0f) GUI.DrawTexture(new Rect(x, y, w * progress, h), _fill);

            // Level only. The exact numbers live on the F7 panel, which is what that panel is
            // for - a permanent readout of "24 / 60" is noise you stop reading by the second
            // hour.
            //
            // The waiting note stays until the pick is made. The centre message that announces
            // a boon fades after a few seconds, and one missed during a fight would otherwise
            // leave a card sitting unclaimed with nothing to say so.
            var text = "Boon " + ClientState.Level;
            if (ClientState.HasOffer) text += "  ·  boon waiting (" + DraftUI.KeyName() + ")";

            GUI.Label(new Rect(x, y + h + 2f, w + 220f, 18f), text, _label);
        }

        private static void Build()
        {
            if (_label != null) return;

            _track = Solid(TrackColour);
            _fill = Solid(FillColour);

            _label = new GUIStyle(GUI.skin.label)
            {
                fontSize = 12,
                normal = { textColor = FillColour },
                wordWrap = false,
                richText = false,
            };
        }

        private static Texture2D Solid(Color colour)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, colour);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            return tex;
        }
    }
}
