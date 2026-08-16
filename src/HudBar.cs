using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Boon
{
    /// <summary>
    /// The experience bar, built by cloning one of the game's own upright bars.
    ///
    /// The first version drew two flat IMGUI rectangles. It sat in the right place and still
    /// read as a mod: a vanilla bar carries a frame sprite, a bevelled inner track, softened
    /// ends, and a second bar behind the first that lags a change - none of which survives
    /// being approximated with a 1x1 texture. Cloning the real thing inherits all of it at
    /// once, the same argument as borrowing a material rather than authoring a texture.
    ///
    /// The donor is the eitr bar, with the stamina bar behind it. Both are an ordinary
    /// GuiBar pair turned on their side: GuiBar only ever resizes its fill on
    /// RectTransform.Axis.Horizontal, so an upright vanilla bar is a *rotated* horizontal
    /// one, and cloning is the only way to get that geometry without rebuilding it.
    ///
    /// XpBar stays behind this as the fallback. A HUD hierarchy is scene data rather than
    /// API, so this can be wrong in ways ilspy cannot warn about; falling back to a bar that
    /// certainly draws beats falling back to an empty corner.
    /// </summary>
    internal static class HudBar
    {
        private const float BorderBuffer = 16f;   // Hud.m_staminaBarBorderBuffer, which every
                                                  // upright bar adds to its root's length.

        private static GameObject _root;
        private static RectTransform _rect;
        private static GuiBar _fast, _slow;
        private static TMP_Text _text;
        private static Animator _animator;
        private static Canvas _canvas;

        private static bool _hasVisible, _hasFlash;
        private static bool _failed;
        private static float _nextFlash;
        private static int _shownLevel = -1;
        private static int _shownPercent = -1;
        private static bool _shownOwed;

        private static float _sizedAt;

        /// <summary>Where the trailing fill currently sits, 0..1. Negative until first drawn,
        /// so a fresh bar starts level with the real one rather than draining in from empty.</summary>
        private static float _trail = -1f;

        /// <summary>How fast the trailing fill catches up, in fractions of the bar per second.</summary>
        private const float TrailSpeed = 0.5f;
        private static bool _uprightAt;
        private static string _tintedAt;

        /// <summary>True while the cloned bar exists, which is what XpBar stands down for.</summary>
        internal static bool Live => _root != null;

        /// <summary>Whether the clone brought a text along to put the level in.</summary>
        internal static bool HasText => _text != null;

        /// <summary>
        /// Half the bar's on-screen length in pixels, so IMGUI can hang a note off a bar that
        /// lives in canvas units. The canvas scale is the whole conversion: the bar's length
        /// is set in canvas units and the scaler multiplies it to pixels.
        /// </summary>
        internal static float HalfLength
        {
            get
            {
                var scale = _canvas != null ? _canvas.scaleFactor : 1f;
                return Mathf.Max(16f, BoonConfig.BarSize.Value) * 0.5f * scale;
            }
        }

        internal static void Update()
        {
            if (!BoonConfig.Enabled.Value || !BoonConfig.ShowXpBar.Value || !BoonConfig.VanillaBar.Value)
            {
                Drop();
                return;
            }

            // The Hud is rebuilt with every world, taking our child with it. Both halves of
            // that are handled by treating a missing root as "build one".
            if (Hud.instance == null || Player.m_localPlayer == null)
            {
                Drop();
                return;
            }

            if (_root == null && !Build()) return;

            var wanted = Visible();
            if (_root.activeSelf != wanted) _root.SetActive(wanted);
            if (!wanted) return;

            Place();

            // Length and colour are re-read rather than set once at build. Both are numbers
            // that get nudged, and a bar you have to restart the game to re-measure is a bar
            // that stays slightly wrong.
            if (!Mathf.Approximately(_sizedAt, BoonConfig.BarSize.Value)) Size();
            if (_uprightAt != BoonConfig.BarUpright.Value) Lay();
            if (_tintedAt != BoonConfig.BarColour.Value) Colour();

            var progress = Mathf.Clamp01(Levels.Progress(ClientState.Xp));
            Fill(progress);

            // Level and percentage together. The level alone said nothing about how close the
            // next one was, and the bar alone is hard to read at a glance when it is a thin
            // upright sliver - a number is the only thing that answers "how far" exactly.
            var percent = Mathf.Clamp(Mathf.RoundToInt(progress * 100f), 0, 100);

            var owed = ClientState.HasPick;

            if (_text != null && (_shownLevel != ClientState.Level || _shownPercent != percent || _shownOwed != owed))
            {
                _shownLevel = ClientState.Level;
                _shownPercent = percent;
                _shownOwed = owed;
                // A star when a pick is waiting, on the bar itself rather than beside it. The
                // note that used to sit alongside was drawn at a fixed screen position and
                // ended up over the guardian power once the bar started following stamina.
                _text.text = _shownLevel + " · " + percent + "%" + (ClientState.HasPick ? " ★" : "");
            }

            Announce();
        }

        /// <summary>
        /// Always up, unlike the bars it was cloned from.
        ///
        /// Vanilla hides stamina and eitr a second after they fill, because they are things
        /// you glance at while they are moving. Experience is the opposite - it is a slow
        /// number you want to be able to check at any moment - so the only things that hide
        /// it are the two that hide everything: the player pressing the hide-HUD key, and
        /// being dead.
        ///
        /// It no longer hides behind the inventory either. That window does not cover this
        /// corner, and having it vanish while you were reading it was the reason to change.
        /// </summary>
        private static bool Visible()
        {
            if (!ClientState.Known) return false;
            if (Player.m_localPlayer.IsDead()) return false;
            return !Hud.instance.m_userHidden;
        }

        /// <summary>
        /// A waiting card flashes the bar, using the animator trigger the eitr bar already
        /// has for the same job. The centre message fades after a few seconds and one missed
        /// during a fight would otherwise leave a card unclaimed with nothing on screen to
        /// say so.
        /// </summary>
        private static void Announce()
        {
            if (!_hasFlash || !ClientState.HasPick) return;
            if (Time.time < _nextFlash) return;

            _nextFlash = Time.time + Mathf.Max(1f, BoonConfig.BarFlashSeconds.Value);
            _animator.SetTrigger("Flash");
        }

        private static bool Build()
        {
            if (_failed) return false;

            var donor = Hud.instance.m_eitrBarRoot != null
                ? Hud.instance.m_eitrBarRoot
                : Hud.instance.m_staminaBar2Root;

            if (donor == null || donor.parent == null)
            {
                Fail("no upright bar to clone from - falling back to the plain bar.");
                return false;
            }

            // Same parent, so canvas scale, sort order and the HUD's own show/hide all come
            // along. Position is overridden below; everything else is inherited on purpose.
            var go = Object.Instantiate(donor.gameObject, donor.parent);
            go.name = "BoonXpBar";
            go.SetActive(true);

            _rect = go.GetComponent<RectTransform>();
            _canvas = go.GetComponentInParent<Canvas>();

            // Which bar is which is read off the components rather than off child names: the
            // trailing one is the one told to smooth. Names are scene data and would be a
            // guess, m_smoothDrain is public API and is not.
            foreach (var bar in go.GetComponentsInChildren<GuiBar>(true))
            {
                if (bar.m_smoothDrain || bar.m_smoothFill) { if (_slow == null) _slow = bar; }
                else if (_fast == null) _fast = bar;
            }

            if (_fast == null) _fast = _slow;
            if (_slow == null) _slow = _fast;

            if (_fast == null)
            {
                Object.Destroy(go);
                Fail("the cloned bar has no GuiBar - falling back to the plain bar.");
                return false;
            }

            _text = go.GetComponentInChildren<TMP_Text>(true);

            _animator = go.GetComponent<Animator>();
            if (_animator == null) _animator = go.GetComponentInChildren<Animator>(true);
            ReadParameters();

            Size();
            Colour();
            Lay();

            // The donor's own hide animation may have left it transparent at the moment it
            // was copied - the eitr bar in particular sits faded out for anyone who has no
            // eitr. Driving the animator's Visible bool rather than hunting for alpha values
            // reuses vanilla's fade-in and cannot get the frame and the fill out of step with
            // each other.
            if (_hasVisible) _animator.SetBool("Visible", true);
            else Unfade(go);

            _root = go;
            _shownLevel = -1;
            _trail = -1f;   // A rebuilt bar starts level, not draining in from empty.

            BoonPlugin.Log.LogInfo("Experience bar cloned from " + donor.name + ".");
            return true;
        }

        /// <summary>
        /// The rescue for a donor whose animator we cannot drive: stop it animating and undo
        /// whatever fade it was frozen mid-way through.
        ///
        /// Only alphas that are *effectively invisible* are raised. Several of these pieces
        /// are meant to be semi-transparent - the darkened track especially - so forcing
        /// everything to opaque would trade an invisible bar for a wrong-looking one.
        /// </summary>
        private static void Unfade(GameObject go)
        {
            if (_animator != null) Object.Destroy(_animator);

            // Gone with it goes the flash, whether or not the controller had that trigger.
            _animator = null;
            _hasFlash = false;

            foreach (var group in go.GetComponentsInChildren<CanvasGroup>(true))
                if (group.alpha < 0.05f) group.alpha = 1f;

            foreach (var graphic in go.GetComponentsInChildren<Graphic>(true))
            {
                var c = graphic.color;
                if (c.a >= 0.05f) continue;

                c.a = 1f;
                graphic.color = c;
            }

            BoonPlugin.Log.LogInfo("Cloned bar has no Visible animator parameter; unfaded by hand.");
        }

        private static void ReadParameters()
        {
            _hasVisible = false;
            _hasFlash = false;

            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            foreach (var p in _animator.parameters)
            {
                if (p.type == AnimatorControllerParameterType.Bool && p.name == "Visible") _hasVisible = true;
                if (p.type == AnimatorControllerParameterType.Trigger && p.name == "Flash") _hasFlash = true;
            }
        }

        /// <summary>
        /// Length, in canvas units, set the way Hud.SetEitrBarSize sets it: the root carries
        /// the border buffer and the two fills do not. Vanilla sizes these from max stamina
        /// or max eitr, which is meaningless for a bar that is always 0..1, so it is config
        /// with a starting stamina bar's 64 as the default.
        /// </summary>
        private static void Size()
        {
            _sizedAt = BoonConfig.BarSize.Value;
            var length = Mathf.Max(16f, _sizedAt);

            _rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, length + BorderBuffer);

            // No SetWidth. The length is held here and applied by Fill, for the reasons below.
            BoonPlugin.Log.LogInfo("Bar sized to " + length + ".");
        }

        /// <summary>
        /// Set both fills directly, rather than through GuiBar's value machinery.
        ///
        /// Three separate bugs in this bar came from that machinery, all the same shape: it
        /// caches or defers something at a moment a freshly cloned, still-inactive object is
        /// not in.
        ///
        ///   - m_barImage is cached in Awake, so SetColor silently did nothing and the bar
        ///     wore the eitr donor's purple whatever BarColour said.
        ///   - m_width is re-read from the fill's own size on the first SetValue, discarding
        ///     what SetWidth was told, so 43% drew as 43% of the donor's 64 on a track of 240.
        ///   - and the one that made the last fix worse: SetValue after the first does not
        ///     draw anything at all. It stores the value, and SetBar is reached from
        ///     LateUpdate - which never runs, because the donor's parts are inactive for a
        ///     character with no eitr. Spending the first SetValue on zero therefore left the
        ///     bar permanently empty.
        ///
        /// SetBar is one line, and every one of those failures is a way of not reaching it:
        ///
        ///     m_bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, m_width * i);
        ///
        /// So the fill is written here instead. Nothing is cached, nothing waits for a
        /// lifecycle callback, and the trailing bar is lagged by hand - which it has to be
        /// anyway, since GuiBar's own smoothing also lives in the LateUpdate that never runs.
        /// </summary>
        private static void Fill(float progress)
        {
            var length = Mathf.Max(16f, BoonConfig.BarSize.Value);

            // The trailing fill only lags downward, which is what makes a level-up read as the
            // old bar draining away rather than the whole thing blinking back to empty.
            if (_trail < 0f || progress > _trail) _trail = progress;
            else _trail = Mathf.MoveTowards(_trail, progress, Time.deltaTime * TrailSpeed);

            Draw(_slow, length * Mathf.Max(_trail, progress));
            Draw(_fast, length * progress);
        }

        private static void Draw(GuiBar bar, float width)
        {
            if (bar == null || bar.m_bar == null) return;
            bar.m_bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, Mathf.Max(0f, width));
        }

        /// <summary>
        /// Upright or flat.
        ///
        /// Vanilla builds its upright bars by rotating a horizontal one - GuiBar only ever
        /// resizes on RectTransform.Axis.Horizontal, so there is no other way to make one
        /// stand up. Undoing that rotation is all it takes to lay ours flat, and a flat bar is
        /// what length is useful on: stood on end, every pixel added runs further down the
        /// screen and off the bottom.
        ///
        /// Set in world terms rather than local, because the rotation may live on the donor's
        /// parent rather than on the donor - this way it does not matter which.
        /// </summary>
        private static void Lay()
        {
            _uprightAt = BoonConfig.BarUpright.Value;
            if (_rect == null) return;

            _rect.rotation = _uprightAt ? Quaternion.Euler(0f, 0f, 90f) : Quaternion.identity;
        }

        private static void Colour()
        {
            _tintedAt = BoonConfig.BarColour.Value;
            var tint = BoonConfig.BarTint();

            // Only the fill is tinted, so the borrowed frame and track keep their own colours.
            // The trailing bar is the same hue held back, which is how vanilla distinguishes
            // the pair.
            Tint(_fast, tint);
            Tint(_slow, new Color(tint.r * 0.55f, tint.g * 0.55f, tint.b * 0.55f, tint.a));

            if (_text != null) _text.color = tint;
        }

        /// <summary>
        /// Colour one bar's fill, without going through GuiBar.SetColor.
        ///
        /// SetColor writes m_barImage, and m_barImage is cached in Awake:
        ///
        ///     private void Awake() { m_barImage = m_bar.GetComponent&lt;Image&gt;(); ... }
        ///     public void SetColor(Color c) { if ((bool)m_barImage) m_barImage.color = c; }
        ///
        /// The bars are collected with GetComponentsInChildren(true), which reaches inactive
        /// ones on purpose - and the eitr bar sits hidden for a character with no eitr, so
        /// Awake has never run on its parts. m_barImage is null, SetColor returns having done
        /// nothing, and it does so silently. The fill then keeps the donor's own colour, which
        /// is the whole reason an experience bar cloned from the eitr bar came out purple no
        /// matter what BarColour said.
        ///
        /// m_bar is a public field, so its Image is reachable without waiting for Awake.
        /// SetColor is still called first: once Awake has run it is the same write, and doing
        /// both keeps this correct if the caching ever moves.
        /// </summary>
        private static void Tint(GuiBar bar, Color colour)
        {
            if (bar == null) return;

            bar.SetColor(colour);

            if (bar.m_bar == null) return;

            var image = bar.m_bar.GetComponent<Image>();
            if (image == null) return;

            if (image.color != colour)
                BoonPlugin.Log.LogInfo("Bar fill '" + image.name + "' was " + image.color +
                                       " (sprite " + (image.sprite != null ? image.sprite.name : "none") +
                                       "); set to " + colour + ".");

            image.color = colour;
        }

        /// <summary>
        /// Placed by screen point rather than by copying the donor's anchoredPosition, which
        /// Hud rewrites every frame (0,130 normally, 0,285 with the build or ship HUD up) and
        /// is therefore never a stable thing to read. Going through
        /// ScreenPointToWorldPointInRectangle also sidesteps having to know whether the
        /// rotation that makes these bars upright sits on the bar or on its parent - the
        /// answer is scene data, and this works either way.
        /// </summary>
        private static void Place()
        {
            var parent = _rect.parent as RectTransform;
            if (parent == null) return;

            // Vanilla lifts its bars out of the way of the build panel. Ours is pinned in
            // screen space, so it has to make the same move by hand or it sits under the
            // piece selection.
            var raised = (Hud.instance.m_buildHud != null && Hud.instance.m_buildHud.activeSelf) ||
                         (Hud.instance.m_shipHudRoot != null && Hud.instance.m_shipHudRoot.activeSelf);

            var cam = _canvas != null && _canvas.renderMode != RenderMode.ScreenSpaceOverlay
                ? _canvas.worldCamera
                : null;

            var point = new Vector2(BoonConfig.BarPosX.Value,
                                    BoonConfig.BarPosY.Value + (raised ? BoonConfig.BarBuildRaise.Value : 0f));

            // Following the stamina bar is worth more than two pixel numbers: "below the
            // stamina bar" is then true at every resolution and HUD scale rather than on the
            // machine the numbers were measured on, and it inherits vanilla's own shove upward
            // when the build panel opens instead of having to repeat it.
            if (BoonConfig.BarFollowStamina.Value)
            {
                var anchor = Hud.instance.m_staminaBar2Root;
                if (anchor != null)
                {
                    var onScreen = RectTransformUtility.WorldToScreenPoint(cam, anchor.position);
                    point = new Vector2(onScreen.x + BoonConfig.BarOffsetX.Value,
                                        onScreen.y - BoonConfig.BarOffsetY.Value);
                }
            }

            if (RectTransformUtility.ScreenPointToWorldPointInRectangle(parent, point, cam, out var world))
                _rect.position = world;
        }

        private static void Fail(string why)
        {
            _failed = true;
            BoonPlugin.Log.LogWarning("Boon: " + why);
        }

        internal static void Drop()
        {
            if (_root != null) Object.Destroy(_root);

            _root = null;
            _rect = null;
            _fast = null;
            _slow = null;
            _text = null;
            _animator = null;
            _canvas = null;
            _shownLevel = -1;
            _trail = -1f;   // A rebuilt bar starts level, not draining in from empty.
            _shownPercent = -1;
            _sizedAt = 0f;
            _tintedAt = null;

            // Forgiven on a world change rather than for the process. A failure costs one log
            // line per world, and the alternative is that a single bad frame during a load
            // leaves the bar plain until the game is restarted.
            _failed = false;
        }
    }
}
