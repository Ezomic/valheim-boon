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
        internal static ConfigEntry<float> BarX;
        internal static ConfigEntry<float> BarBottom;
        internal static ConfigEntry<float> BarWidth;
        internal static ConfigEntry<float> BarHeight;

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
                "Show the experience bar under the health and stamina bars. It hides itself " +
                "with the rest of the interface, including when the HUD is hidden by hand.");

            // Pixels rather than an anchor to the real health bar: converting a scaled Canvas
            // RectTransform into IMGUI screen space breaks differently at every HUD scale, and
            // two numbers anyone can nudge are easier to correct than a formula that is subtly
            // wrong. These defaults are a starting guess for 1080p at default HUD scale.
            BarX = cfg.Bind("Bar", "BarX", 68f, "Pixels from the left edge of the screen.");

            // Measured from the bottom, because that is the corner it belongs in - health and
            // stamina live bottom-left, and this sits under them. Anchoring to the top would
            // move it every time the resolution changed.
            BarBottom = cfg.Bind("Bar", "BarBottom", 40f,
                "Pixels from the bottom of the screen to the top of the bar. Lower this to " +
                "push it further down, raise it to lift it toward the stamina bar. Depends on " +
                "your resolution and HUD scale, so it will probably need nudging once.");
            BarWidth = cfg.Bind("Bar", "BarWidth", 180f, "Bar width in pixels.");
            BarHeight = cfg.Bind("Bar", "BarHeight", 6f, "Bar height in pixels.");

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
    }
}
