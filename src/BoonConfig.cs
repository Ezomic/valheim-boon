using BepInEx.Configuration;
using UnityEngine;

namespace Boon
{
    /// <summary>
    /// Everything tunable, per the repo rule that a rejected number should be a config
    /// edit rather than a rebuild.
    ///
    /// Note the standing BepInEx trap: every entry is written to disk on first run and the
    /// saved value beats a new default in code. Changing a default here does nothing on a
    /// machine that has already run the plugin - edit
    /// BepInEx\config\ezomic.valheim.boon.cfg as part of the same change.
    /// </summary>
    internal static class BoonConfig
    {
        internal static ConfigEntry<bool> Enabled;

        internal static ConfigEntry<float> XpPerSkillLevel;
        internal static ConfigEntry<float> LevelBaseXp;
        internal static ConfigEntry<float> LevelExponent;

        internal static ConfigEntry<int> MaxRank;
        internal static ConfigEntry<int> BonusEvery;
        internal static ConfigEntry<float> AttackSpeedMax;
        internal static ConfigEntry<float> PanelWidth;
        internal static ConfigEntry<int> PanelColumns;

        internal static ConfigEntry<bool> RemoveDeathSkillLoss;

        internal static ConfigEntry<bool> ProtectCharacter;
        internal static ConfigEntry<bool> RequireFreshCharacter;
        internal static ConfigEntry<bool> GateEnforce;
        internal static ConfigEntry<float> MaxSkillUpsPerMinute;
        internal static ConfigEntry<float> SkillDriftAllowance;


        internal static ConfigEntry<bool> Verbose;
        internal static ConfigEntry<KeyboardShortcut> KeyBoon;

        internal static ConfigEntry<bool> ShowInfoTab;
        internal static ConfigEntry<bool> ShowXpBar;
        internal static ConfigEntry<bool> VanillaBar;
        internal static ConfigEntry<bool> BarUpright;
        internal static ConfigEntry<bool> BarFollowStamina;
        internal static ConfigEntry<float> BarOffsetX;
        internal static ConfigEntry<float> BarOffsetY;
        internal static ConfigEntry<float> BarPosX;
        internal static ConfigEntry<float> BarPosY;
        internal static ConfigEntry<float> BarSize;
        internal static ConfigEntry<float> BarBuildRaise;
        internal static ConfigEntry<string> BarColour;
        internal static ConfigEntry<float> BarFlashSeconds;
        internal static ConfigEntry<float> BarX;
        internal static ConfigEntry<float> BarBottom;
        internal static ConfigEntry<float> BarThickness;
        internal static ConfigEntry<float> BarLength;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "Off leaves levels and cards recorded but stops granting or applying them.");

            Verbose = cfg.Bind("General", "Verbose", false,
                "Log every XP grant, every rejected report and every card applied.");

            // A window needs a way back to it. Closing it with Escape used to hide it
            // until the game restarted, which made "decide later" a lie.
            // Unbound. The compendium tab is the way in, which is the standing preference
            // here - a thing on screen rather than a key, the same call Stow made when it took
            // a buildable post and left both its keys in config with nothing on them.
            //
            // The entry stays so it can be given a key again in one line, and that is the way
            // back if the tab ever fails to build: it logs a warning saying so.
            KeyBoon = cfg.Bind("General", "KeyBoon", new KeyboardShortcut(KeyCode.None),
                "Opens your boons. Unbound by default - the tab on the compendium bar is the " +
                "way in. Set a key here if you would rather have one; F6 is devkit's, and " +
                "Numpad 0-7 are taken by Thralls and Tether.");

            // The character level is its own number with its own curve. It is fed by skill
            // level-ups but is deliberately not a restatement of total skill level - the
            // weighting below is what makes it a separate track rather than a second view
            // of the skill list.
            XpPerSkillLevel = cfg.Bind("Levelling", "XpPerSkillLevel", 1f,
                "XP granted per skill level-up, multiplied by the skill level reached. " +
                "Weighting by level reached is deliberate: a flat rate would stall late " +
                "progression exactly when the deep card ranks matter, and would make " +
                "grinding a fresh cheap skill from zero the fastest way to farm cards.");

            LevelBaseXp = cfg.Bind("Levelling", "LevelBaseXp", 60f,
                "Cumulative XP needed for character level 1. Later levels scale by LevelExponent.");

            LevelExponent = cfg.Bind("Levelling", "LevelExponent", 1.5f,
                "Cumulative XP for level N is LevelBaseXp * N^LevelExponent. Above 1 means " +
                "each level costs more than the last. Vanilla's own skill curve is already " +
                "front-loaded, so this counteracts a flood of early cards.");

            // A thing on screen rather than a key, which is the standing preference here.
            // KeyBoon stays bound and keeps working either way.
            ShowInfoTab = cfg.Bind("General", "ShowInfoTab", true,
                "Add a fifth tab to the compendium bar, beside the raven and the trophy, that " +
                "opens your boons. Cloned from a tab already there, so it carries the game's " +
                "own frame, hover and click sound.");

            ShowXpBar = cfg.Bind("Bar", "ShowXpBar", true,
                "Show the experience bar beside the health bar. It hides itself with the rest " +
                "of the interface, including when the HUD is hidden by hand.");

            // On by default, because a hand-drawn bar never matched: vanilla bars carry a
            // frame, a bevelled track, softened ends and a trailing second fill, and none of
            // that survives being approximated with a flat rectangle.
            VanillaBar = cfg.Bind("Bar", "VanillaBar", true,
                "Build the bar by cloning one of the game's own upright bars, so it carries " +
                "the same frame, fill sprite and trailing fill as stamina and eitr.\n" +
                "Off draws a plain rectangle instead. That is also the automatic fallback if " +
                "the clone fails, since the HUD hierarchy is scene data rather than API and " +
                "can change under a game update.");

            // The donor is a rotated bar - GuiBar only ever resizes on the horizontal axis, so
            // vanilla builds its upright bars by turning a horizontal one on its side. Laying
            // ours flat again is one line, and it is what a long bar wants: upright, length
            // runs down the screen and off the bottom.
            BarUpright = cfg.Bind("Bar", "BarUpright", false,
                "Stand the bar on end like stamina and eitr. Off lays it flat, which is what a " +
                "long bar needs - upright, extra length runs off the bottom of the screen.");

            // Anchored to the stamina bar rather than to the screen, so "below the stamina
            // bar" stays true at any resolution and HUD scale instead of being two numbers
            // that happen to be right on one machine. It also follows vanilla's own shove
            // upward when the build panel opens, which BarBuildRaise otherwise has to
            // duplicate by hand.
            BarFollowStamina = cfg.Bind("Bar", "BarFollowStamina", true,
                "Place the bar relative to the stamina bar instead of at fixed screen pixels. " +
                "Off falls back to BarPosX and BarPosY, which is also what happens if the " +
                "stamina bar cannot be found.");

            BarOffsetX = cfg.Bind("Bar", "BarOffsetX", 0f,
                "Pixels right of the stamina bar's centre, when following it.");

            BarOffsetY = cfg.Bind("Bar", "BarOffsetY", 70f,
                "Pixels below the stamina bar's centre, when following it.");

            // Screen pixels, not the donor's anchoredPosition: Hud rewrites that every frame
            // (0,130 normally, 0,285 with the build HUD up), so it is never a stable thing to
            // copy. The defaults put the bar where the hand-drawn one was tuned to sit.
            BarPosX = cfg.Bind("Bar", "BarPosX", 172f,
                "Pixels from the left edge of the screen to the centre of the cloned bar.");

            BarPosY = cfg.Bind("Bar", "BarPosY", 105f,
                "Pixels from the bottom of the screen to the centre of the cloned bar. Depends " +
                "on your resolution and HUD scale, so it will probably need nudging once.");

            // Vanilla sizes these bars from max stamina or max eitr, which means nothing for a
            // bar that is always 0..1. 64 is what a starting stamina bar measures (50/25*32).
            BarSize = cfg.Bind("Bar", "BarSize", 240f,
                "Length of the cloned bar in canvas units, the same units vanilla sizes the " +
                "stamina and eitr bars in. 64 is what a starting stamina bar measures; 240 is a " +
                "bar you can read a percentage off at a glance. Thickness comes " +
                "from the borrowed sprite and is not settable.");

            BarBuildRaise = cfg.Bind("Bar", "BarBuildRaise", 155f,
                "Pixels to lift the cloned bar while the build or ship panel is open. Vanilla " +
                "moves its own bars up by the same amount to clear that panel; ours is pinned " +
                "in screen space, so it has to make the move by hand.");

            BarColour = cfg.Bind("Bar", "BarColour", "E4DCC4",
                "Bar colour as RRGGBB hex. The trailing fill is the same hue held back, the " +
                "way vanilla tells its bar pairs apart. Unparseable values fall back to gold.\n" +
                "Bone by default. The four vanilla bars have red, yellow, blue and orange " +
                "between them, so a fifth in any of those reads as one of them, and pale " +
                "stone is what the boons themselves are cut from.");

            BarFlashSeconds = cfg.Bind("Bar", "BarFlashSeconds", 4f,
                "How often the bar flashes while a pick is waiting to be spent, in seconds. " +
                "Uses the flash the borrowed bar already has. Only applies to the cloned bar.");

            // Pixels rather than an anchor to the real health bar: converting a scaled Canvas
            // RectTransform into IMGUI screen space breaks differently at every HUD scale, and
            // two numbers anyone can nudge are easier to correct than a formula that is subtly
            // wrong. These four place the fallback bar only; the cloned one uses BarPos*.
            BarX = cfg.Bind("Bar", "BarX", 168f,
                "Pixels from the left edge of the screen to the left edge of the bar. The " +
                "default aims to sit just right of the health bar.");

            // Measured from the bottom, because that is the corner it belongs in. Anchoring to
            // the top would move it every time the resolution changed.
            BarBottom = cfg.Bind("Bar", "BarBottom", 75f,
                "Pixels from the bottom of the screen to the bottom of the bar. Depends on " +
                "your resolution and HUD scale, so it will probably need nudging once.");

            // Thickness and length rather than width and height, because the bar is upright:
            // reusing the old names would have let the horizontal values already written to
            // the cfg carry over as a very wide, very short vertical bar.
            BarThickness = cfg.Bind("Bar", "BarThickness", 10f, "Bar thickness in pixels.");
            BarLength = cfg.Bind("Bar", "BarLength", 60f, "Bar height in pixels.");

            // A capstone every fifth rank rather than only at the last one, so raising MaxRank
            // to 10 gives two of them rather than moving the single one further away.
            BonusEvery = cfg.Bind("Cards", "BonusEvery", 5,
                "Ranks between capstones. A card that names a bonus effect in its last two " +
                "cards.txt fields grants it once per this many ranks. At the default MaxRank " +
                "of 5 that means once, on the final upgrade.");

            AttackSpeedMax = cfg.Bind("Cards", "AttackSpeedMax", 1f,
                "Ceiling on the attack-speed cards, as a fraction. 1 means the animation can " +
                "at most run at double speed.\n" +
                "This multiplies an animation rather than a number in a table, and animation " +
                "events are what land the hit, so a mis-typed catalogue line could otherwise " +
                "run the whole character at twenty times speed.");

            // Twenty-four cards no longer fit three across without scrolling, and both of
            // these are the kind of number that wants nudging rather than rebuilding.
            PanelWidth = cfg.Bind("Cards", "PanelWidth", 1100f,
                "Width of the F7 panel in pixels. It never scrolls sideways, so this and " +
                "PanelColumns together set how wide a tile is.");

            PanelColumns = cfg.Bind("Cards", "PanelColumns", 4,
                "Tiles across the F7 panel. Fewer means wider tiles and a taller panel; the " +
                "panel scrolls vertically once it would pass 88% of the screen height.");

            MaxRank = cfg.Bind("Cards", "MaxRank", 5,
                "How deep a single card can be taken. A card at this rank stops being " +
                "pickable, and is also how many slots its track shows.");


            ProtectCharacter = cfg.Bind("Gate", "ProtectCharacter", true,
                "Refuse to start a local world with a character that belongs to a different " +
                "one.\n" +
                "This is protection rather than enforcement, and it is the only defence that " +
                "actually works: loading a world writes it into the character's own " +
                "m_worldData, nothing ever removes that entry, and the gate then refuses the " +
                "character on its own server forever. The bindings are in boon-home.txt beside " +
                "this file and can be edited or deleted by hand.");

            RemoveDeathSkillLoss = cfg.Bind("Death", "RemoveDeathSkillLoss", true,
                "Skip Skills.OnDeath entirely. The vanilla world modifier is not enough on " +
                "its own: the accumulator wipe and the 'skills lowered' message sit outside " +
                "the reduction factor, so zeroing it still discards partial progress toward " +
                "the next level in every skill.");

            // The gate is the real anti-cheat, not the rate limit. A character that has only
            // ever been on this world cannot have been levelled anywhere else.
            RequireFreshCharacter = cfg.Bind("Gate", "RequireFreshCharacter", true,
                "Check on every login that the character has played on no world but this one, " +
                "and has never used cheats. Checked every login rather than only the first, so " +
                "a character taken elsewhere and brought back is caught too.");

            GateEnforce = cfg.Bind("Gate", "GateEnforce", true,
                "On refuses the connection and tells the player why. Off logs what would " +
                "have been blocked without disconnecting anyone.\n" +
                "This applies to your own character too, and the rule is strict: a character " +
                "is refused by every world but its first, and m_worldData entries are never " +
                "removed, so one visit elsewhere locks that character out permanently. Back " +
                "up a character file before taking it anywhere else - restoring the backup " +
                "clears the travel record and is the only way back in.");

            SkillDriftAllowance = cfg.Bind("Gate", "SkillDriftAllowance", 1f,
                "How far a returning character's skill may sit above the highest level this " +
                "server watched it reach before it counts as gained elsewhere.\n" +
                "Not zero, because the ledger is flushed on a timer: a server that stops " +
                "unexpectedly can lose the last few seconds of snapshot updates, and a level " +
                "genuinely earned here would then look imported. One level of slack absorbs " +
                "that and is worth nothing to a cheat.");

            MaxSkillUpsPerMinute = cfg.Bind("Gate", "MaxSkillUpsPerMinute", 30f,
                "Server-side ceiling on accepted skill-up reports per player. A backstop " +
                "only: skills live on the client, so reports are self-reported and this " +
                "caps the damage rather than verifying anything.");
        }

        private static readonly Color Gold = new Color(0.83f, 0.663f, 0.29f, 1f);
        private static string _tintFrom;
        private static Color _tint = Gold;

        /// <summary>
        /// BarColour parsed, cached against the string it came from so the bar can re-read it
        /// every frame without parsing every frame. A bad value keeps the gold rather than
        /// throwing, the way an unknown card effect is skipped rather than fatal.
        /// </summary>
        internal static Color BarTint()
        {
            var text = BarColour != null ? BarColour.Value : null;
            if (_tintFrom == text) return _tint;

            _tintFrom = text;
            _tint = Gold;

            if (string.IsNullOrEmpty(text)) return _tint;
            if (text[0] != '#') text = "#" + text;

            if (ColorUtility.TryParseHtmlString(text, out var parsed)) _tint = parsed;
            return _tint;
        }
    }
}
