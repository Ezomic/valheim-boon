using UnityEngine;

namespace Boon
{
    /// <summary>
    /// The boon panel: a full screen of runestones, one per card, each carved one more mark
    /// per rank taken.
    ///
    /// Three designs came before this. A draft window that dealt three cards at random; a
    /// board of tiles; then the same board dressed in the game's own wooden sprites. The last
    /// one is why this exists - borrowing a window frame is imitation, and it read as a
    /// different game no matter how close the sprites got, because IMGUI cannot draw with the
    /// game's shaders and every copied sprite had to survive a colour-space round trip that
    /// was guessed wrong three times.
    ///
    /// A runestone imitates nothing. It is Valheim's own subject matter, it is a shape rather
    /// than a material, and the marks cut into it are text in the game's own rune face - so
    /// the whole panel is one generated disc and two borrowed fonts, with nothing that can
    /// arrive the wrong colour.
    ///
    /// Full screen also deletes the scroll view: twenty-five stones fit a five by five field
    /// exactly, and the panel is sized to the screen rather than to its contents.
    /// </summary>
    internal static class BoonPanel
    {
        private const float Board = 1120f;
        private const float PadX = 30f;
        private const float PadY = 26f;

        private const float Stone = 108f;
        private const float CellGapX = 14f;
        private const float CellGapY = 18f;
        private const float StoneGap = 7f;
        private const float NameLine = 18f;
        private const float NowLine = 16f;

        private const float DetailWidth = 268f;
        private const float DetailPad = 22f;

        // Marks around the rim, spread evenly and starting at the top. The same set on every
        // stone: what differs between boons is the sigil in the middle, not the ranks cut
        // around it.
        private const float MarkRadius = 38f;
        private const float MarkSize = 22f;
        private static readonly string[] Marks = { "ᚠ", "ᚱ", "ᚲ", "ᚷ", "ᚹ", "ᚦ", "ᛁ", "ᛏ" };

        private static bool _built;
        private static bool _open;
        private static int _selected;

        private static GUIStyle _void, _title, _sub, _spend, _name, _nameDim, _now, _nowDim,
                                _sigil, _sigilDim, _mark, _dname, _dflav, _label, _dnow, _dnext,
                                _dcap, _slotOn, _slotOff, _take, _foot, _bar, _barTrack, _rule;

        private static readonly Color Void = new Color(0.031f, 0.027f, 0.024f, 0.86f);
        private static readonly Color Gold = new Color(0.83f, 0.663f, 0.29f, 1f);
        private static readonly Color Cream = new Color(0.91f, 0.863f, 0.753f, 1f);
        private static readonly Color Muted = new Color(0.659f, 0.612f, 0.518f, 1f);
        private static readonly Color Faint = new Color(0.431f, 0.404f, 0.349f, 1f);
        private static readonly Color Green = new Color(0.498f, 0.62f, 0.541f, 1f);
        private static readonly Color Silver = new Color(0.682f, 0.717f, 0.788f, 1f);
        private static readonly Color Carve = new Color(0.106f, 0.090f, 0.063f, 1f);
        private static readonly Color CarveDim = new Color(0.165f, 0.153f, 0.137f, 1f);
        private static readonly Color Track = new Color(0.165f, 0.149f, 0.125f, 1f);
        private static readonly Color Edge = new Color(0.184f, 0.169f, 0.141f, 1f);

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
        /// Never opens by itself. It used to appear the moment a level landed, which put a
        /// modal over the screen and took the mouse mid-fight - a good way to be killed by
        /// your own reward.
        /// </summary>
        internal static void Toggle()
        {
            _open = !_open;
        }

        internal static void Close()
        {
            _open = false;
        }

        internal static void Draw()
        {
            if (!IsOpen) return;

            Build();
            Stones.Ensure();

            // The whole screen, so nothing behind it competes and there is no frame that has
            // to hold its own beside the game's windows.
            GUI.Label(new Rect(0f, 0f, Screen.width, Screen.height), GUIContent.none, _void);

            var count = Mathf.Max(1, Cards.All.Count);
            var columns = Mathf.Max(1, BoonConfig.PanelColumns.Value);
            var rows = Mathf.CeilToInt(count / (float)columns);

            var cellH = Stone + StoneGap + NameLine + StoneGap + NowLine;
            var fieldW = Board - PadX * 2f - DetailWidth - DetailPad;
            var cellW = (fieldW - CellGapX * (columns - 1)) / columns;

            var headH = 34f + 5f + 18f;
            var boardH = rows * cellH + (rows - 1) * CellGapY;
            var totalH = PadY * 2f + headH + boardH + 26f;

            var origin = new Vector2((Screen.width - Board) * 0.5f, (Screen.height - totalH) * 0.5f);
            var x = origin.x + PadX;
            var y = origin.y + PadY;

            DrawHead(x, ref y, Board - PadX * 2f);

            var fieldTop = y;
            DrawField(x, fieldTop, cellW, cellH, columns);
            DrawDetail(new Rect(x + fieldW + DetailPad, fieldTop, DetailWidth, boardH));

            GUI.Label(new Rect(x, fieldTop + boardH + 8f, Board - PadX * 2f, 18f),
                      KeyName() + " or Escape to close · each mark holds the level that cut it", _foot);
        }

        private static void DrawHead(float x, ref float y, float width)
        {
            GUI.Label(new Rect(x, y, width * 0.5f, 32f), "YOUR BOONS", _title);

            var held = ClientState.Ranks.Count;
            var marks = 0;
            foreach (var kv in ClientState.Ranks) marks += kv.Value;

            GUI.Label(new Rect(x + 200f, y + 9f, width - 480f, 20f),
                      "Level " + ClientState.Level + " · " + held + " of " + Cards.All.Count +
                      " carved · " + marks + " of " + Cards.All.Count * BoonConfig.MaxRank.Value +
                      " marks", _sub);

            if (ClientState.HasPick)
            {
                var owed = ClientState.Owed;
                GUI.Label(new Rect(x, y + 7f, width, 22f),
                          owed == 1 ? "1 boon to spend" : owed + " boons to spend", _spend);
            }

            y += 34f;

            GUI.Label(new Rect(x, y, width, 5f), GUIContent.none, _barTrack);
            GUI.Label(new Rect(x, y, width * Mathf.Clamp01(Levels.Progress(ClientState.Xp)), 5f),
                      GUIContent.none, _bar);

            y += 5f + 18f;
        }

        private static void DrawField(float left, float top, float cellW, float cellH, int columns)
        {
            for (var i = 0; i < Cards.All.Count; i++)
            {
                var cell = new Rect(left + (i % columns) * (cellW + CellGapX),
                                    top + (i / columns) * (cellH + CellGapY),
                                    cellW, cellH);

                DrawStone(Cards.All[i], i, cell);
            }
        }

        private static void DrawStone(Card card, int index, Rect cell)
        {
            var rank = ClientState.RankOf(card.Id);
            var maxRank = Mathf.Max(1, BoonConfig.MaxRank.Value);
            var maxed = rank >= maxRank;
            var canTake = ClientState.HasPick && !maxed;

            var disc = new Rect(cell.x + (cell.width - Stone) * 0.5f, cell.y, Stone, Stone);
            var hovered = disc.Contains(Event.current.mousePosition);

            // Hovering selects, so the detail column follows the cursor without a click, and a
            // click only ever spends a pick. Two gestures that never overlap.
            if (hovered) _selected = index;

            if (hovered && canTake) GUI.DrawTexture(Grow(disc, 4f), Stones.Halo);

            GUI.DrawTexture(disc, rank > 0 ? Stones.Carved : Stones.Raw);
            if (maxed) GUI.DrawTexture(disc, Stones.Rim);

            var cx = disc.x + Stone * 0.5f;
            var cy = disc.y + Stone * 0.5f;

            GUI.Label(new Rect(cx - 26f, cy - 26f, 52f, 52f), Sigil(card), rank > 0 ? _sigil : _sigilDim);

            for (var m = 0; m < rank && m < Marks.Length; m++)
            {
                var angle = m / (float)maxRank * Mathf.PI * 2f;
                var px = cx + Mathf.Sin(angle) * MarkRadius;
                var py = cy - Mathf.Cos(angle) * MarkRadius;

                GUI.Label(new Rect(px - MarkSize * 0.5f, py - MarkSize * 0.5f, MarkSize, MarkSize),
                          Marks[m], _mark);
            }

            if (canTake && hovered && Event.current.type == EventType.MouseDown && Event.current.button == 0)
            {
                Take(card.Id);
                Event.current.Use();
            }

            var y = disc.yMax + StoneGap;
            GUI.Label(new Rect(cell.x, y, cell.width, NameLine), card.Name, rank > 0 ? _name : _nameDim);

            y += NameLine;
            GUI.Label(new Rect(cell.x, y, cell.width, NowLine),
                      rank > 0 ? card.Describe(rank) : card.Describe(1) + " at rank 1",
                      rank > 0 ? _now : _nowDim);
        }

        /// <summary>
        /// The detail column, the way the crafting window pairs a list with a panel. It shows
        /// whatever the cursor is over, so a full screen of stones stays readable without
        /// every stone having to state its own case.
        /// </summary>
        private static void DrawDetail(Rect rect)
        {
            if (_selected < 0 || _selected >= Cards.All.Count) return;

            var card = Cards.All[_selected];
            var rank = ClientState.RankOf(card.Id);
            var maxRank = Mathf.Max(1, BoonConfig.MaxRank.Value);
            var maxed = rank >= maxRank;

            GUI.Label(new Rect(rect.x - DetailPad * 0.5f, rect.y, 1f, rect.height), GUIContent.none, _rule);

            var y = rect.y;
            var w = rect.width;

            GUI.Label(new Rect(rect.x, y, w, 28f), card.Name, _dname);
            y += 30f;

            var flavourH = _dflav.CalcHeight(new GUIContent(card.Flavour), w);
            GUI.Label(new Rect(rect.x, y, w, flavourH), card.Flavour, _dflav);
            y += flavourH + 16f;

            y = Row(rect.x, y, w, "NOW", rank > 0 ? card.Describe(rank) : "Not yet carved", _dnow);
            y = Row(rect.x, y, w, "NEXT",
                    maxed ? "Fully carved" : card.Describe(rank + 1) + " at rank " + (rank + 1), _dnext);

            if (card.HasBonus)
            {
                var times = Card.BonusTimes(rank);
                var at = Mathf.Max(1, BoonConfig.BonusEvery.Value) * (times + 1);

                y = Row(rect.x, y, w, "CAPSTONE",
                        times > 0 ? "★ " + card.DescribeBonus(times)
                                  : "★ " + card.DescribeBonus(1) + " at rank " + at, _dcap);
            }

            GUI.Label(new Rect(rect.x, y, w, 14f), "CARVED AT", _label);
            y += 17f;

            var levels = ClientState.LevelsOf(card.Id);
            for (var i = 0; i < maxRank; i++)
            {
                var slot = new Rect(rect.x + i * 35f, y, 30f, 22f);

                if (i < rank)
                {
                    // A 0 is a rank taken before levels were recorded, and cannot be dated.
                    var level = levels != null && i < levels.Count ? levels[i] : 0;
                    GUI.Label(slot, level > 0 ? level.ToString() : "—", _slotOn);
                }
                else
                {
                    GUI.Label(slot, (i + 1).ToString(), _slotOff);
                }
            }

            GUI.Label(new Rect(rect.x, rect.yMax - 34f, w, 30f),
                      maxed ? "Fully carved"
                            : ClientState.HasPick ? "Click the stone to carve it"
                                                  : "No boon to spend", _take);
        }

        private static float Row(float x, float y, float w, string label, string value, GUIStyle style)
        {
            GUI.Label(new Rect(x, y, w, 14f), label, _label);
            GUI.Label(new Rect(x, y + 15f, w, 20f), value, style);
            return y + 41f;
        }

        /// <summary>
        /// The rune in the middle of a stone. Authored per card in the catalogue's last field,
        /// because a card's identity should be legible before its name is read - and falling
        /// back to the first letter of the id keeps a card written before sigils existed from
        /// coming out blank.
        /// </summary>
        private static string Sigil(Card card)
        {
            if (!string.IsNullOrEmpty(card.Sigil)) return card.Sigil;
            return card.Id.Length > 0 ? card.Id.Substring(0, 1).ToUpperInvariant() : "·";
        }

        private static Rect Grow(Rect r, float by)
        {
            return new Rect(r.x - by, r.y - by, r.width + by * 2f, r.height + by * 2f);
        }

        internal static string KeyName()
        {
            var key = BoonConfig.KeyBoon.Value.MainKey;
            return key == KeyCode.None ? "the Boon key" : key.ToString();
        }

        private static void Take(string id)
        {
            // Predicted before the send. ZRoutedRpc handles a message addressed to yourself
            // inline, so on a host the server has already answered by the time SendPick
            // returns - predicting afterwards stacked a second rank on top of the answer.
            ClientState.PredictTake(id);
            Net.SendPick(id);
        }

        private static void Build()
        {
            if (_built) return;
            _built = true;

            Skin.Ensure();

            _void = new GUIStyle { normal = { background = Solid(Void) } };
            _barTrack = new GUIStyle { normal = { background = Solid(Track) } };
            _bar = new GUIStyle { normal = { background = Solid(Gold) } };
            _rule = new GUIStyle { normal = { background = Solid(Edge) } };

            _title = Head(26, Gold);

            _sub = Body(14, Muted);

            _spend = Head(15, Gold);
            _spend.alignment = TextAnchor.UpperRight;

            _name = Body(14, Cream);
            _name.alignment = TextAnchor.UpperCenter;
            _nameDim = Body(14, Faint);
            _nameDim.alignment = TextAnchor.UpperCenter;

            _now = Body(12, Green);
            _now.alignment = TextAnchor.UpperCenter;
            _nowDim = Body(12, new Color(0.333f, 0.376f, 0.353f, 1f));
            _nowDim.alignment = TextAnchor.UpperCenter;

            // The inscription is cut into the stone rather than written on it, so it is dark
            // against the granite - which is all a carve is at this size.
            _sigil = Rune(34, Carve);
            _sigilDim = Rune(34, CarveDim);
            _mark = Rune(17, Carve);

            _dname = Head(21, Gold);

            _dflav = Body(13, Muted);
            _dflav.wordWrap = true;
            _dflav.fontStyle = FontStyle.Italic;

            _label = Body(11, Faint);
            _dnow = Body(14, Green);
            _dnext = Body(14, Gold);
            _dcap = Body(14, Silver);

            _slotOn = Body(12, Gold);
            _slotOn.alignment = TextAnchor.MiddleCenter;
            _slotOn.normal.background = Solid(new Color(0.83f, 0.663f, 0.29f, 0.12f));

            _slotOff = Body(12, Faint);
            _slotOff.alignment = TextAnchor.MiddleCenter;
            _slotOff.normal.background = Solid(new Color(0.129f, 0.118f, 0.098f, 1f));

            _take = Head(16, Gold);
            _take.alignment = TextAnchor.MiddleCenter;

            _foot = Body(12, Faint);
        }

        private static GUIStyle Body(int size, Color colour)
        {
            var style = Text(size, colour);
            if (Skin.Face != null) style.font = Skin.Face;
            return style;
        }

        private static GUIStyle Head(int size, Color colour)
        {
            var style = Text(size, colour);
            if (Skin.HeadFace != null) style.font = Skin.HeadFace;
            return style;
        }

        private static GUIStyle Rune(int size, Color colour)
        {
            var style = Text(size, colour);
            style.alignment = TextAnchor.MiddleCenter;
            if (Skin.RuneFace != null) style.font = Skin.RuneFace;
            return style;
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
