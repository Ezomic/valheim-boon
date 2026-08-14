using System.Collections.Generic;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// Two windows sharing one skin: the draft, and the list of what you already hold.
    ///
    /// IMGUI so the mod stays a single DLL with no asset bundle. Styles are built on first
    /// paint because GUI.skin only exists inside OnGUI.
    ///
    /// The draft matches the agreed mockup - header, subtitle, then cards with name, effect,
    /// flavour, a rule and a rank line. The one element of that mockup not reproduced is the
    /// per-card icon: it would need a sprite asset, and this mod is deliberately a DLL and two
    /// text files.
    /// </summary>
    internal static class DraftUI
    {
        private static bool _built;
        private static bool _deferred;
        private static string _deferredFor = "";
        private static bool _status;

        private static GUIStyle _panel, _card, _title, _sub, _name, _effect, _flavour, _rank, _rule, _footer, _bar;

        private static readonly Color Ink = new Color(0.09f, 0.07f, 0.055f, 0.98f);
        private static readonly Color CardBg = new Color(0.133f, 0.106f, 0.082f, 1f);
        private static readonly Color Edge = new Color(0.29f, 0.24f, 0.173f, 1f);
        private static readonly Color Gold = new Color(0.83f, 0.663f, 0.29f, 1f);
        private static readonly Color Cream = new Color(0.91f, 0.863f, 0.753f, 1f);
        private static readonly Color Muted = new Color(0.659f, 0.612f, 0.518f, 1f);
        private static readonly Color Green = new Color(0.498f, 0.62f, 0.541f, 1f);

        /// <summary>True while either window is up, which is what the input patches key on.</summary>
        internal static bool IsOpen => DraftVisible || StatusVisible;

        private static bool Usable
        {
            get
            {
                if (!BoonConfig.Enabled.Value) return false;
                if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead()) return false;
                return !InventoryGui.IsVisible() && !Menu.IsVisible();
            }
        }

        private static bool DraftVisible
        {
            get
            {
                if (!Usable || !ClientState.HasOffer) return false;
                return !(_deferred && _deferredFor == OfferKey());
            }
        }

        private static bool StatusVisible => _status && Usable && !DraftVisible;

        /// <summary>
        /// The key press. Brings a deferred offer back if one is waiting, otherwise shows what
        /// you hold - a deferred draft is the more urgent of the two, and needing two different
        /// keys to reach two views of the same thing would be worse than either.
        /// </summary>
        internal static void Toggle()
        {
            if (ClientState.HasOffer && _deferred && _deferredFor == OfferKey())
            {
                _deferred = false;
                _status = false;
                return;
            }

            if (DraftVisible) { Defer(); return; }

            _status = !_status;
        }

        /// <summary>Escape puts the offer off. It is still owed, so the key brings it back.</summary>
        internal static void Defer()
        {
            if (StatusVisible) { _status = false; return; }

            _deferred = true;
            _deferredFor = OfferKey();
        }

        private static string OfferKey() => string.Join(",", ClientState.Offer.ToArray());

        internal static void Draw()
        {
            if (DraftVisible) { Build(); DrawDraft(); return; }
            if (StatusVisible) { Build(); DrawStatus(); }
        }

        private static void DrawDraft()
        {
            const float width = 760f;
            const float pad = 18f;
            const float gap = 10f;

            var cards = ClientState.Offer.Count;
            var cardWidth = (width - pad * 2f - gap * (cards - 1)) / Mathf.Max(1, cards);
            const float height = 340f;

            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            var y = rect.y + pad;

            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 26f), "A boon is offered", _title);
            y += 28f;

            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 20f),
                      "Level " + ClientState.Level + " · " + Ordinal(ClientState.DraftsTaken + 1) +
                      " boon · one of " + cards, _sub);
            y += 30f;

            var cardTop = y;
            var cardHeight = rect.yMax - pad - 24f - cardTop;

            for (var i = 0; i < cards; i++)
            {
                var id = ClientState.Offer[i];
                var cr = new Rect(rect.x + pad + i * (cardWidth + gap), cardTop, cardWidth, cardHeight);

                // The button goes down first so it takes the click; the labels drawn over it
                // do not consume events, so the whole card stays clickable.
                if (GUI.Button(cr, GUIContent.none, _card)) Pick(id);

                var card = Cards.Get(id);
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

                GUI.Label(new Rect(cr.x + 12f, cy, inner, _flavour.CalcHeight(new GUIContent(card.Flavour), inner)),
                          card.Flavour, _flavour);

                // Rule and rank pinned to the bottom, so cards line up regardless of how many
                // lines the flavour wrapped to.
                GUI.Label(new Rect(cr.x + 12f, cr.yMax - 34f, inner, 1f), GUIContent.none, _rule);
                GUI.Label(new Rect(cr.x + 12f, cr.yMax - 28f, inner, 20f),
                          rank == 0 ? "Not yet taken" : "Rank " + rank + " → " + (rank + 1),
                          rank == 0 ? _rank : _effect);
            }

            GUI.Label(new Rect(rect.x + pad, rect.yMax - pad - 16f, width - pad * 2f, 20f),
                      "Escape to decide later · " + KeyName() + " to bring it back", _footer);
        }

        private static void DrawStatus()
        {
            const float width = 460f;
            const float pad = 18f;
            const float row = 24f;

            var held = new List<KeyValuePair<string, int>>(ClientState.Ranks);
            held.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));

            var height = pad * 2f + 76f + Mathf.Max(1, held.Count) * row + 22f;
            var rect = new Rect((Screen.width - width) * 0.5f, (Screen.height - height) * 0.5f, width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            var y = rect.y + pad;

            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 26f), "Your boons", _title);
            y += 28f;

            var xpInto = ClientState.Xp - Levels.XpForLevel(ClientState.Level);
            var xpNeed = Levels.XpForLevel(ClientState.Level + 1) - Levels.XpForLevel(ClientState.Level);

            GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, 20f),
                      "Level " + ClientState.Level + " · " + Mathf.FloorToInt(xpInto) + " / " +
                      Mathf.CeilToInt(xpNeed) + " xp", _sub);
            y += 24f;

            // A bar rather than only the numbers, because "how close am I" is the question this
            // window exists to answer.
            var barRect = new Rect(rect.x + pad, y, width - pad * 2f, 6f);
            GUI.Label(barRect, GUIContent.none, _rule);
            GUI.Label(new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(Levels.Progress(ClientState.Xp)), 6f),
                      GUIContent.none, _bar);
            y += 24f;

            if (held.Count == 0)
            {
                GUI.Label(new Rect(rect.x + pad, y, width - pad * 2f, row), "None yet.", _flavour);
            }
            else
            {
                foreach (var kv in held)
                {
                    var card = Cards.Get(kv.Key);
                    var label = card != null ? card.Name : kv.Key;

                    GUI.Label(new Rect(rect.x + pad, y, 190f, row), label + "  ·  " + kv.Value, _name);
                    GUI.Label(new Rect(rect.x + pad + 196f, y, width - pad * 2f - 196f, row),
                              card != null ? card.Describe(kv.Value) : "", _effect);
                    y += row;
                }
            }

            GUI.Label(new Rect(rect.x + pad, rect.yMax - pad - 16f, width - pad * 2f, 20f),
                      KeyName() + " or Escape to close", _footer);
        }

        private static string KeyName()
        {
            var key = BoonConfig.KeyBoon.Value.MainKey;
            return key == KeyCode.None ? "the Boon key" : key.ToString();
        }

        private static void Pick(string id)
        {
            Net.SendPick(id);

            // Clear locally so the window closes at once rather than waiting for the round
            // trip. The server's reply is authoritative and corrects this if it refuses.
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
            _rank = Text(13, Muted);
            _footer = Text(12, Muted);

            _flavour = Text(13, Muted);
            _flavour.wordWrap = true;
            _flavour.fontStyle = FontStyle.Italic;

            _rule = new GUIStyle { normal = { background = Solid(Edge) } };
            _bar = new GUIStyle { normal = { background = Solid(Gold) } };
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
            return tex;
        }
    }
}
