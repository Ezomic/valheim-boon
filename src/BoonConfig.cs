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
        internal static ConfigEntry<int> OfferCount;

        internal static ConfigEntry<bool> RemoveDeathSkillLoss;

        internal static ConfigEntry<bool> ProtectCharacter;
        internal static ConfigEntry<bool> RequireFreshCharacter;
        internal static ConfigEntry<bool> GateEnforce;
        internal static ConfigEntry<float> MaxSkillUpsPerMinute;
        internal static ConfigEntry<float> SkillDriftAllowance;

        internal static ConfigEntry<int> InventoryBaseHeight;

        internal static ConfigEntry<bool> Verbose;
        internal static ConfigEntry<KeyboardShortcut> KeyBoon;

        internal static ConfigEntry<bool> ShowXpBar;
        internal static ConfigEntry<bool> VanillaBar;
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

            // A window needs a way back to it. Deferring an offer with Escape used to hide it
            // until the game restarted, which made "decide later" a lie.
            KeyBoon = cfg.Bind("General", "KeyBoon", new KeyboardShortcut(KeyCode.F7),
                "Opens your boons: the cards you hold and how far you are through the current " +
                "level. If a draft is waiting, this brings it back instead. F6 is devkit's, " +
                "and Numpad 0-7 are taken by Thralls and Tether.");

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
            BarSize = cfg.Bind("Bar", "BarSize", 64f,
                "Length of the cloned bar in canvas units, the same units vanilla sizes the " +
                "stamina and eitr bars in. 64 matches a starting stamina bar. Thickness comes " +
                "from the borrowed sprite and is not settable.");

            BarBuildRaise = cfg.Bind("Bar", "BarBuildRaise", 155f,
                "Pixels to lift the cloned bar while the build or ship panel is open. Vanilla " +
                "moves its own bars up by the same amount to clear that panel; ours is pinned " +
                "in screen space, so it has to make the move by hand.");

            BarColour = cfg.Bind("Bar", "BarColour", "D4A94A",
                "Bar colour as RRGGBB hex. The trailing fill is the same hue held back, the " +
                "way vanilla tells its bar pairs apart. Unparseable values fall back to gold.");

            BarFlashSeconds = cfg.Bind("Bar", "BarFlashSeconds", 4f,
                "How often the bar flashes while a card is waiting to be drafted, in seconds. " +
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

            MaxRank = cfg.Bind("Cards", "MaxRank", 5,
                "How deep a single card can be taken. Offers stop including cards at this rank.");

            OfferCount = cfg.Bind("Cards", "OfferCount", 3,
                "How many cards are offered per level. Fewer than this are shown only when " +
                "too few cards remain below MaxRank.");

            InventoryBaseHeight = cfg.Bind("Cards", "InventoryBaseHeight", 4,
                "Vanilla player inventory rows, used as the base the *inventoryrow card adds " +
                "to. Read from the game at runtime when possible; this is the fallback.");

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
