using System.Collections.Generic;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The draft window: three cards, pick one.
    ///
    /// IMGUI so the mod stays a single DLL with no asset bundle. Styles are built on first
    /// paint because GUI.skin only exists inside OnGUI.
    ///
    /// Laid out to match the agreed mockup - header, subtitle, then three cards each with
    /// name, effect, flavour, a rule, and a rank line. The one element of that mockup not
    /// reproduced is the per-card icon: it would need a sprite asset, and this mod is
    /// deliberately a DLL and two text files.
    /// </summary>
    internal static class DraftUI
    {
        private static bool _built;
        private static bool _deferred;
        private static string _deferredFor = "";

        private static GUIStyle _panel, _card, _title, _sub, _name, _effect, _flavour, _rank, _rule, _footer;
        private static readonly List<Texture2D> _textures = new List<Texture2D>();

        private static readonly Color Ink = new Color(0.09f, 0.07f, 0.055f, 0.98f);
        private static readonly Color CardBg = new Color(0.133f, 0.106f, 0.082f, 1f);
        private static readonly Color Edge = new Color(0.29f, 0.24f, 0.173f, 1f);
        private static readonly Color Gold = new Color(0.83f, 0.663f, 0.29f, 1f);
        private static readonly Color Cream = new Color(0.91f, 0.863f, 0.753f, 1f);
        private static readonly Color Muted = new Color(0.659f, 0.612f, 0.518f, 1f);
        private static readonly Color Green = new Color(0.498f, 0.62f, 0.541f, 1f);

        internal static bool IsOpen
        {
            get
            {
                if (!BoonConfig.Enabled.Value) return false;
                if (!ClientState.HasOffer) return false;
                if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead()) return false;
                if (InventoryGui.IsVisible() || Menu.IsVisible()) return false;
                return !(_deferred && _deferredFor == OfferKey());
            }
        }

        /// <summary>Escape puts the offer off. It is still owed, so it comes back.</summary>
        internal static void Defer()
        {
            _deferred = true;
            _deferredFor = OfferKey();
        }

        private static string OfferKey()
        {
            return string.Join(",", ClientState.Offer.ToArray());
        }

        internal static void Draw()
        {
            if (!IsOpen) return;
            Build();

            const float width = 760f;
            const float pad = 18f;
            const float gap = 10f;

            var cards = ClientState.Offer.Count;
            var cardWidth = (width - pad * 2f - gap * (cards - 1)) / Mathf.Max(1, cards);
            var height = 340f;

            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            var y = rect.y + pad;

            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 26f), "A boon is offered", _title);
            y += 28f;

            var ordinal = Ordinal(ClientState.DraftsTaken + 1);
            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 20f),
                      "Level " + ClientState.Level + " · " + ordinal + " boon · one of " + cards, _sub);
            y += 30f;

            var cardTop = y;
            var cardHeight = rect.yMax - pad - 24f - cardTop;

            for (var i = 0; i < cards; i++)
            {
                var id = ClientState.Offer[i];
                var card = Cards.Get(id);

                var cr = new Rect(rect.x + pad + i * (cardWidth + gap), cardTop, cardWidth, cardHeight);

                // The button goes down first so it takes the click; the labels drawn over it
                // do not consume events, so the whole card stays clickable.
                if (GUI.Button(cr, GUIContent.none, _card)) Pick(id);

                if (card == null)
                {
                    GUI.Label(new Rect(cr.x + 12f, cr.y + 12f, cr.width - 24f, 40f), id, _name);
                    continue;
                }

                var rank = ClientState.RankOf(id);
                var inner = cr.width - 24f;
                var cy = cr.y + 14f;

                GUI.Label(new Rect(cr.x + 12f, cy, inner, 22f), card.Name, _name);
                cy += 26f;

                GUI.Label(new Rect(cr.x + 12f, cy, inner, 20f), card.Describe(rank + 1), _effect);
                cy += 26f;

                var flavourHeight = _flavour.CalcHeight(new GUIContent(card.Flavour), inner);
                GUI.Label(new Rect(cr.x + 12f, cy, inner, flavourHeight), card.Flavour, _flavour);

                // Rule and rank pinned to the bottom, so cards line up regardless of how many
                // lines the flavour text wrapped to.
                GUI.Label(new Rect(cr.x + 12f, cr.yMax - 34f, inner, 1f), GUIContent.none, _rule);

                var rankText = rank == 0
                    ? "Not yet taken"
                    : "Rank " + rank + " → " + (rank + 1);

                GUI.Label(new Rect(cr.x + 12f, cr.yMax - 28f, inner, 20f), rankText,
                          rank == 0 ? _rank : _effect);
            }

            GUI.Label(new Rect(rect.x + pad, rect.yMax - pad - 16f, width - pad * 2f, 20f),
                      "Escape to decide later", _footer);
        }

        private static void Pick(string id)
        {
            Net.SendPick(id);

            // Clear locally so the window closes at once rather than waiting for the round
            // trip. The server's reply is authoritative and will correct this if it refuses.
            ClientState.Offer.Clear();
            _deferred = false;
        }

        private static string Ordinal(int n)
        {
            if (n <= 0) return n.ToString();
            if (n % 100 >= 11 && n % 100 <= 13) return n + "th";

            switch (n % 10)
            {
                case 1: return n + "st";
                case 2: return n + "nd";
                case 3: return n + "rd";
                default: return n + "th";
            }
        }

        private static void Build()
        {
            if (_built) return;
            _built = true;

            _panel = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Solid(Ink) },
                border = new RectOffset(1, 1, 1, 1),
            };

            _card = new GUIStyle(GUI.skin.box)
            {
                normal = { background = Solid(CardBg) },
                hover = { background = Solid(Blend(CardBg, Gold, 0.12f)) },
                active = { background = Solid(Blend(CardBg, Gold, 0.2f)) },
            };

            _title = Text(20, Gold);
            _sub = Text(13, Muted);
            _name = Text(16, Cream);
            _effect = Text(14, Green);
            _flavour = Text(13, Muted);
            _flavour.wordWrap = true;
            _flavour.fontStyle = FontStyle.Italic;
            _rank = Text(13, Muted);
            _footer = Text(12, Muted);

            _rule = new GUIStyle { normal = { background = Solid(Edge) } };
        }

        private static GUIStyle Text(int size, Color colour)
        {
            return new GUIStyle(GUI.skin.label)
            {
                fontSize = size,
                normal = { textColor = colour },
                wordWrap = false,
                richText = false,
            };
        }

        private static Color Blend(Color a, Color b, float t)
        {
            return new Color(Mathf.Lerp(a.r, b.r, t), Mathf.Lerp(a.g, b.g, t), Mathf.Lerp(a.b, b.b, t), a.a);
        }

        private static Texture2D Solid(Color colour)
        {
            var tex = new Texture2D(1, 1);
            tex.SetPixel(0, 0, colour);
            tex.Apply();
            tex.hideFlags = HideFlags.HideAndDontSave;
            _textures.Add(tex);
            return tex;
        }
    }
}
