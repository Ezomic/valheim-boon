using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The boon panel: every card in the catalogue, what each one is worth now, what the next
    /// rank would make it, and the level that bought every rank already held.
    ///
    /// This replaced a draft window that dealt three cards at random. The randomness was
    /// carrying a lot of machinery - a seeded roll so an offer could not be rerolled by
    /// quitting, an offer field in the ledger and on the wire, a second window with its own
    /// visibility rules - and none of that is needed once the answer is "pick any of them".
    /// What it cost was the thing the seed existed to protect: the pick is now a plain choice
    /// rather than a hand you are dealt.
    ///
    /// IMGUI so the mod stays a single DLL with no asset bundle. Styles are built on first
    /// paint because GUI.skin only exists inside OnGUI.
    ///
    /// The layout is the agreed mockup: a board of tiles, each carrying a five-slot rank
    /// track with the level that bought a rank sitting in the slot it bought. Two elements of
    /// that mockup are not reproduced - the per-card icon, which would need a sprite asset,
    /// and the theme groupings, which would need a sixth field in cards.txt and are not worth
    /// it at fourteen cards.
    /// </summary>
    internal static class BoonPanel
    {
        private const float Width = 908f;
        private const float Pad = 18f;
        private const float Gap = 8f;
        private const float TileHeight = 96f;
        private const int Columns = 3;

        private const float SlotWidth = 30f;
        private const float SlotHeight = 19f;
        private const float SlotGap = 4f;

        private static bool _built;
        private static bool _open;
        private static Vector2 _scroll;

        private static GUIStyle _panel, _card, _cardEmpty, _title, _sub, _spend, _name,
                                _effect, _upgrade, _muted, _slotOn, _slotOff, _rule, _bar, _footer;

        private static readonly Color Ink = new Color(0.09f, 0.07f, 0.055f, 0.98f);
        private static readonly Color CardBg = new Color(0.133f, 0.106f, 0.082f, 1f);
        private static readonly Color EmptyBg = new Color(0.106f, 0.086f, 0.067f, 1f);
        private static readonly Color Edge = new Color(0.29f, 0.24f, 0.173f, 1f);
        private static readonly Color Gold = new Color(0.83f, 0.663f, 0.29f, 1f);
        private static readonly Color Cream = new Color(0.91f, 0.863f, 0.753f, 1f);
        private static readonly Color Muted = new Color(0.659f, 0.612f, 0.518f, 1f);
        private static readonly Color Faded = new Color(0.42f, 0.39f, 0.34f, 1f);
        private static readonly Color Green = new Color(0.498f, 0.62f, 0.541f, 1f);

        /// <summary>True while the panel is up, which is what the input patches key on.</summary>
        internal static bool IsOpen => _open && Usable;

        private static bool Usable
        {
            get
            {
                if (!BoonConfig.Enabled.Value) return false;
                if (Player.m_localPlayer == null || Player.m_localPlayer.IsDead()) return false;
                return !InventoryGui.IsVisible() && !Menu.IsVisible();
            }
        }

        /// <summary>
        /// Never opens by itself.
        ///
        /// It used to appear the moment a level landed, which put a modal over the screen and
        /// took the mouse in the middle of a fight - a good way to get killed by your own
        /// reward. A level only announces itself, and the panel waits until it is asked for.
        /// </summary>
        internal static void Toggle()
        {
            _open = !_open;
            if (_open) _scroll = Vector2.zero;
        }

        /// <summary>Escape closes it. An unspent pick simply stays owed.</summary>
        internal static void Close()
        {
            _open = false;
        }

        internal static void Draw()
        {
            if (!IsOpen) return;

            Build();

            var rows = Mathf.CeilToInt(Mathf.Max(1, Cards.All.Count) / (float)Columns);
            var gridHeight = rows * TileHeight + (rows - 1) * Gap;

            var headHeight = 96f + (ClientState.HasPick ? 24f : 0f);
            var chrome = Pad * 2f + headHeight + 26f;

            // The catalogue is a text file and can grow, so the panel is sized to its contents
            // and only then clamped. Past the clamp the grid scrolls vertically; horizontally
            // it never does, which is why the column count is fixed rather than the tile width.
            var wanted = chrome + gridHeight;
            var height = Mathf.Min(wanted, Screen.height * 0.88f);
            var scrolls = wanted > height;

            var rect = new Rect((Screen.width - Width) * 0.5f, (Screen.height - height) * 0.5f, Width, height);
            GUI.Box(rect, GUIContent.none, _panel);

            var inner = Width - Pad * 2f;
            var y = rect.y + Pad;

            GUI.Label(new Rect(rect.x + Pad, y, inner, 26f), "Your boons", _title);
            y += 28f;

            GUI.Label(new Rect(rect.x + Pad, y, inner, 20f), Summary(), _sub);
            y += 22f;

            var barRect = new Rect(rect.x + Pad, y, inner, 6f);
            GUI.Label(barRect, GUIContent.none, _rule);
            GUI.Label(new Rect(barRect.x, barRect.y, barRect.width * Mathf.Clamp01(Levels.Progress(ClientState.Xp)), 6f),
                      GUIContent.none, _bar);
            y += 12f;

            var into = ClientState.Xp - Levels.XpForLevel(ClientState.Level);
            var need = Levels.XpForLevel(ClientState.Level + 1) - Levels.XpForLevel(ClientState.Level);
            GUI.Label(new Rect(rect.x + Pad, y, inner, 20f),
                      Mathf.FloorToInt(into) + " / " + Mathf.CeilToInt(need) +
                      " xp to level " + (ClientState.Level + 1), _sub);
            y += 24f;

            if (ClientState.HasPick)
            {
                var owed = ClientState.Owed;
                GUI.Label(new Rect(rect.x + Pad, y, inner, 22f),
                          owed == 1 ? "One boon to spend — choose any card below"
                                    : owed + " boons to spend — choose any card below", _spend);
                y += 24f;
            }

            var gridRect = new Rect(rect.x + Pad, y, inner, rect.yMax - Pad - 22f - y);

            if (scrolls)
            {
                // The view is one tile-width narrower than the grid so the scrollbar has
                // somewhere to live without the tiles reflowing under it.
                var view = new Rect(0f, 0f, inner - 18f, gridHeight);
                _scroll = GUI.BeginScrollView(gridRect, _scroll, view, false, true);
                DrawGrid(0f, 0f, inner - 18f);
                GUI.EndScrollView();
            }
            else
            {
                DrawGrid(gridRect.x, gridRect.y, inner);
            }

            GUI.Label(new Rect(rect.x + Pad, rect.yMax - Pad - 16f, inner, 20f),
                      KeyName() + " or Escape to close · a slot holds the level that bought it", _footer);
        }

        private static string Summary()
        {
            var held = ClientState.Ranks.Count;
            var total = Cards.All.Count;

            var ranks = 0;
            foreach (var kv in ClientState.Ranks) ranks += kv.Value;

            return "Level " + ClientState.Level + " · " + held + " of " + total + " held · " +
                   ranks + " of " + total * BoonConfig.MaxRank.Value + " ranks taken";
        }

        private static void DrawGrid(float left, float top, float width)
        {
            var tileWidth = (width - Gap * (Columns - 1)) / Columns;

            for (var i = 0; i < Cards.All.Count; i++)
            {
                var column = i % Columns;
                var row = i / Columns;

                DrawTile(Cards.All[i], new Rect(left + column * (tileWidth + Gap),
                                                top + row * (TileHeight + Gap),
                                                tileWidth, TileHeight));
            }
        }

        private static void DrawTile(Card card, Rect rect)
        {
            var rank = ClientState.RankOf(card.Id);
            var maxed = rank >= BoonConfig.MaxRank.Value;
            var canTake = ClientState.HasPick && !maxed;

            // The button goes down first so it takes the click; the labels drawn over it do
            // not consume events, so the whole tile stays clickable. When there is nothing to
            // spend it is a box instead, so it does not offer a hover it cannot honour.
            if (canTake)
            {
                if (GUI.Button(rect, GUIContent.none, _card)) Take(card.Id);
            }
            else
            {
                GUI.Box(rect, GUIContent.none, rank > 0 ? _card : _cardEmpty);
            }

            var x = rect.x + 12f;
            var w = rect.width - 24f;
            var y = rect.y + 10f;

            GUI.Label(new Rect(x, y, w, 20f), card.Name, rank > 0 ? _name : _muted);
            y += 21f;

            // What it does now, then what the pick would make it. The second line is the
            // whole reason the panel can replace a draft: choosing between fourteen cards is
            // only a choice if each one says what it would buy.
            GUI.Label(new Rect(x, y, w, 18f),
                      rank > 0 ? card.Describe(rank) : "Not yet taken",
                      rank > 0 ? _effect : _muted);
            y += 19f;

            GUI.Label(new Rect(x, y, w, 18f),
                      maxed ? "Rank " + rank + " · nothing further"
                            : "→ " + card.Describe(rank + 1) + " at rank " + (rank + 1),
                      maxed ? _muted : _upgrade);

            DrawTrack(card, new Rect(x, rect.yMax - 10f - SlotHeight, w, SlotHeight), rank);
        }

        /// <summary>
        /// One slot per rank. A filled slot carries the level that bought it, an empty one
        /// carries the rank number it would be - so a tile reads as both how deep it is and
        /// how much further it goes.
        /// </summary>
        private static void DrawTrack(Card card, Rect rect, int rank)
        {
            var levels = ClientState.LevelsOf(card.Id);
            var slots = Mathf.Max(1, BoonConfig.MaxRank.Value);

            for (var i = 0; i < slots; i++)
            {
                var slot = new Rect(rect.x + i * (SlotWidth + SlotGap), rect.y, SlotWidth, SlotHeight);
                if (slot.xMax > rect.xMax) break;

                if (i < rank)
                {
                    // A 0 is a rank taken before levels were recorded. There is nothing on
                    // disk that says which level bought it, so it stays a dash rather than a
                    // guess - see BoonRecord.Taken.
                    var level = levels != null && i < levels.Count ? levels[i] : 0;
                    GUI.Label(slot, level > 0 ? level.ToString() : "—", _slotOn);
                }
                else
                {
                    GUI.Label(slot, (i + 1).ToString(), _slotOff);
                }
            }
        }

        internal static string KeyName()
        {
            var key = BoonConfig.KeyBoon.Value.MainKey;
            return key == KeyCode.None ? "the Boon key" : key.ToString();
        }

        private static void Take(string id)
        {
            Net.SendPick(id);

            // Applied locally at once so the tile answers the click, rather than sitting
            // unchanged for a round trip. The server's reply is authoritative either way and
            // corrects this if it refuses - it pushes state on every rejection for exactly
            // that reason.
            ClientState.PredictTake(id);
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

            _cardEmpty = new GUIStyle(GUI.skin.box) { normal = { background = Solid(EmptyBg) } };

            _title = Text(20, Gold);
            _sub = Text(13, Muted);
            _spend = Text(14, Gold);
            _name = Text(16, Cream);
            _effect = Text(13, Green);
            _upgrade = Text(13, Gold);
            _muted = Text(13, Faded);
            _footer = Text(12, Muted);

            _slotOn = Text(12, Gold);
            _slotOn.alignment = TextAnchor.MiddleCenter;
            _slotOn.normal.background = Solid(Blend(Ink, Gold, 0.16f));

            _slotOff = Text(12, Faded);
            _slotOff.alignment = TextAnchor.MiddleCenter;
            _slotOff.normal.background = Solid(Blend(Ink, Edge, 0.35f));

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
