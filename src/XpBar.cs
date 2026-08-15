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
        private static GUIStyle _label, _waiting;

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
            var thickness = Mathf.Max(3f, BoonConfig.BarThickness.Value);
            var length = Mathf.Max(10f, BoonConfig.BarLength.Value);

            // Upright, to stand beside the health bar rather than lie under it. IMGUI measures
            // from the top, so the bottom-anchored offset is subtracted and the bar is drawn
            // upward from there.
            var bottom = Screen.height - BoonConfig.BarBottom.Value;
            var top = bottom - length;

            GUI.DrawTexture(new Rect(x, top, thickness, length), _track);

            // Fills from the bottom up, the way the health bar beside it does.
            var progress = Mathf.Clamp01(Levels.Progress(ClientState.Xp));
            if (progress > 0f)
            {
                var filled = length * progress;
                GUI.DrawTexture(new Rect(x, bottom - filled, thickness, filled), _fill);
            }

            // Just the level. The exact numbers live on the F7 panel, which is what that panel
            // is for - a permanent "24 / 60" is noise you stop reading by the second hour.
            _label.alignment = TextAnchor.UpperCenter;
            GUI.Label(new Rect(x - 14f, bottom + 3f, thickness + 28f, 18f),
                      ClientState.Level.ToString(), _label);

            // Only while something is actually waiting. The centre message announcing a boon
            // fades after a few seconds, and one missed during a fight would otherwise leave a
            // card unclaimed with nothing on screen to say so.
            if (!ClientState.HasOffer) return;

            _waiting.alignment = TextAnchor.MiddleLeft;
            GUI.Label(new Rect(x + thickness + 6f, top, 200f, length),
                      "boon\nwaiting\n(" + DraftUI.KeyName() + ")", _waiting);
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

            _waiting = new GUIStyle(_label) { fontSize = 12, wordWrap = false };
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
