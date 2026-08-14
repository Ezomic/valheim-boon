using BepInEx.Configuration;

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

        internal static ConfigEntry<bool> RequireFreshCharacter;
        internal static ConfigEntry<bool> GateEnforce;
        internal static ConfigEntry<float> MaxSkillUpsPerMinute;

        internal static ConfigEntry<int> InventoryBaseHeight;

        internal static ConfigEntry<bool> Verbose;

        internal static void Bind(ConfigFile cfg)
        {
            Enabled = cfg.Bind("General", "Enabled", true,
                "Off leaves levels and cards recorded but stops granting or applying them.");

            Verbose = cfg.Bind("General", "Verbose", false,
                "Log every XP grant, every rejected report and every card applied.");

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

            MaxRank = cfg.Bind("Cards", "MaxRank", 5,
                "How deep a single card can be taken. Offers stop including cards at this rank.");

            OfferCount = cfg.Bind("Cards", "OfferCount", 3,
                "How many cards are offered per level. Fewer than this are shown only when " +
                "too few cards remain below MaxRank.");

            InventoryBaseHeight = cfg.Bind("Cards", "InventoryBaseHeight", 4,
                "Vanilla player inventory rows, used as the base the *inventoryrow card adds " +
                "to. Read from the game at runtime when possible; this is the fallback.");

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

            GateEnforce = cfg.Bind("Gate", "GateEnforce", false,
                "Off logs what the gate would have blocked without disconnecting anyone. " +
                "Start here and read the log before turning enforcement on - the check " +
                "applies to your own character as well.");

            MaxSkillUpsPerMinute = cfg.Bind("Gate", "MaxSkillUpsPerMinute", 30f,
                "Server-side ceiling on accepted skill-up reports per player. A backstop " +
                "only: skills live on the client, so reports are self-reported and this " +
                "caps the damage rather than verifying anything.");
        }
    }
}
