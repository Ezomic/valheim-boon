# Rist

A character level beside the skills, carved into runestones you choose and deepen.

Built against the installed game (0.221.12, Unity 6000.0.61, BepInEx 5.4.23.3, Harmony 2.9).
Single DLL plus a text file, no asset bundle.

## What it is

Valheim has skills but no character level. Rist adds one that runs **alongside** the skill
system: vanilla skills are read, never written, never rescaled, never reskinned.

Every character level earns one **pick**. Spend it on any runestone in the catalogue and that runestone
gains a rank, up to **five**. Picks bank: nothing expires, and the panel waits.

There are no drawbacks. A runestone is a reward or it is nothing.

## The panel

A fifth tab on the compendium bar, beside the raven and the trophy, opens a full screen of
runestones - one per rist. Each carries its own outline, its own rock and its own runes, all
seeded off the runestone id, so a rist looks the same in every session and on every machine: the
stone is part of how you recognise it.

A rank cuts one more mark into the rim, and the mark holds **the level that bought it**. Five
ranks, five marks, five answers to "when did I get this". The column beside the field follows
the cursor and says what a stone is worth now, what the next rank would make it, and what
waits at the bottom of its track.

Three designs came before it: a draft window dealing three at random, a board of tiles, and
that same board dressed in the game's own wooden sprites. The last is why this one exists.
Borrowing a window frame is imitation, and it read as a different game no matter how close the
sprites got - IMGUI cannot draw with the game's shaders, so every copied sprite had to survive
a colour space round trip that was guessed wrong three times. A runestone imitates nothing: it
is Valheim's own subject matter, it is a shape rather than a material, and the marks cut into
it are text in the game's own rune face.

## XP

Skill level-ups, and nothing else. Nearly every activity in Valheim raises *some* skill, so
building, sneaking, sailing, cooking and fishing all pay in without this mod maintaining a
table of what counts.

XP is **weighted by the skill level reached**, not flat per level-up. Vanilla's own skill cost
curve is `pow(level+1, 1.5)`, so early levels are nearly free. A flat rate would rain runestones in
the first hours and dry up exactly when the deep ranks start to matter, and would make
grinding a fresh cheap skill from zero the fastest way to farm picks.

## Dying no longer costs skill progress

Valheim already has a world modifier for this and it is **not enough**. `Skills.OnDeath` calls
`LowerAllSkills(m_DeathLowerFactor * Game.m_skillReductionRate)`, and inside that method only
the level loss is scaled by the factor:

```csharp
m_level -= m_level * factor;   // scaled: zero factor means no loss
m_accumulator = 0f;            // NOT scaled: always wiped
// ...and "$msg_skills_lowered" is shown regardless
```

Setting the world's skill reduction to zero therefore still throws away partial progress
toward the next level in *every* skill, and still announces that skills were lowered. On a
skill in the seventies, where one level is a long grind, the accumulator is the part that
actually hurts. Rist skips the method, which removes all three together.

## The bar

The experience bar is a clone of one of the game's own upright bars, the eitr bar, so it
carries the real frame, track and fill sprites at whatever HUD scale you have set, drains
rather than snapping to empty on a level-up, and flashes while a pick is waiting. If a game
update ever breaks the clone it falls back to a plain drawn bar rather than to an empty corner.

## Joining a server

Progress is kept **on the server**, keyed by the platform identity of the connection and the
character being played. Valheim characters are client-side files, so anything stored on the
character is editable by its owner, which rules it out for something that decides rewards.

A joining character is credited for the skills it already carries, so it arrives at the level
those skills are worth with the picks to spend. That is `CreditExistingSkills`, on by default;
turn it off and only XP earned on that server counts. It is a per-server setting, synced from
the host.

Crediting has a plain cost: a maxed vanilla character walks in at Rist level 219 and takes the
whole catalogue. The answer to that is not to withhold XP from it, it is to not let it in,
which is [Threshold](../threshold)'s job rather than this mod's.

Skill reports come from the client and cannot be verified, so three ceilings bound what a claim
can be worth: how many are accepted, how much one may say, and how much anything can be worth
over time. None of it detects a cheat, it bounds one, and it withholds rather than punishes.

Singleplayer works with no special casing and the ceilings are inert there.

## Runestones

`cards.txt` sits beside the DLL. One runestone per line:

```
id | Name | flavour text | effect | value-per-rank
```

`effect` is the literal name of a public float field on the game's own `SE_Stats`. That is
what makes the catalogue a text file rather than a switch statement: Valheim already sums
about forty-five of these across active status effects (carry weight, armour, the six stamina
costs, stealth, fall damage, regen rates, skill gain) so naming one turns it into a runestone with
no code change and no rebuild.

`*inventoryrow` is the single special, because grid height is not a stat.

An unknown field name is **logged and skipped**, not thrown, matching how the game treats a
prefab name that does not resolve. A typo costs one runestone, not the catalogue.

## Core is optional, and here is exactly what that costs

Rist installs and runs on its own. Core is a **soft** dependency: install Rist by itself and
it works, including the extra inventory rows. **Solo, you need nothing else and give up
nothing.**

On a server it is a different question, and Rist gives up more without Core than the rest of
this suite does. Three things, all of which matter only in multiplayer:

**The catalogue is no longer checked.** `cards.txt` names what every rank is worth, effects are
applied client-side from it, and the server only ever verifies the *rank*, so an edited line
is simply believed. With Core, a hash of the file travels in the version handshake and two ends
running the same build over different catalogues get reported. Without it, a client that edits
`cards.txt` gets whatever it wrote.

**The host's curve is no longer forced.** `LevelBaseXp` and the rest are synced from the host
with Core. Without it, a client with different settings reads a different level out of the same
XP, and every number on its screen disagrees with the server deciding them. The `.cfg` files
have to be matched by hand.

**Extra inventory rows are claimed without an arbiter.** This is the one worth reading twice.
`Inventory.m_height` is a single private int with no owner. Two mods that both want extra rows
each write it, the last writer wins, and a mod that only writes when its own state changes
loses silently to one that writes every frame. Core exists to add every claim up and write
once, so mods stack instead of fighting. Standalone Rist owns that write alone, correct on its
own, and **in conflict with any other mod that grants inventory rows**, with the winner decided
by frame ordering rather than by anything either mod can control. If you run another row-adding
mod, install Core.

What is *not* given up is correctness of the rows themselves. The standalone owner carries the
`Player.Load` widening in full, which is not a nicety: without it every item sitting in a
granted row is destroyed by loading the game, silently, because `AddItem` drops any saved
position outside the current grid and the row is not applied until after the load. That bug
cost a real heartwood to find, and the fallback would not have shipped without the fix.

Rist logs a warning at startup when it starts without Core, naming all three.

## Config

`BepInEx\config\ezomic.valheim.rist.cfg`

| Key | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Off keeps records but stops granting and applying |
| `XpPerSkillLevel` | `1` | XP per skill-up, multiplied by the level reached |
| `LevelBaseXp` | `40` | Cumulative XP for level 1 |
| `LevelExponent` | `1.4` | Cumulative XP for level N is base × N^exponent |
| `MaxRank` | `5` | How deep one runestone goes, and how many slots its track shows |
| `BonusEvery` | `5` | Ranks between capstones |
| `ShowInfoTab` | `true` | Add the rists tab to the compendium bar |
| `RemoveDeathSkillLoss` | `true` | Skip `Skills.OnDeath` |
| `CheckSkillBaseline` | `true` | Take a joining character's skills as the baseline it is paid from |
| `CreditExistingSkills` | `true` | Pay a joining character for the skills it already has; off counts only XP gained here |
| `MaxSkillUpsPerMinute` | `30` | Server-side ceiling on accepted reports per player |
| `MaxSkillLevelJump` | `1` | Levels above the baseline one report may claim |
| `MaxXpPerMinute` | `600` | XP paid per minute of connected time; `0` is off |
| `XpBurst` | `1800` | How much unspent allowance banks, so honest bursts still pay |
| `Verbose` | `false` | Log every grant, rejection and runestone applied |
| `ShowXpBar` | `true` | Show the experience bar at all |
| `VanillaBar` | `true` | Clone one of the game's own bars; off draws the plain fallback |
| `BarFollowStamina` | `true` | Anchor to the stamina bar; off falls back to `BarPosX`/`BarPosY` |
| `BarOffsetX` / `BarOffsetY` | `0` / `70` | Pixels right of and below the stamina bar's centre |
| `BarPosX` / `BarPosY` | `172` / `105` | Screen pixels to the **centre**, when not following |
| `BarSize` | `240` | Length in canvas units; 64 is a starting stamina bar |
| `BarBuildRaise` | `155` | Pixels to lift the bar while the build or ship panel is open |
| `BarColour` | `4FB3A5` | `RRGGBB`; the trailing fill is the same hue held back. `E4DCC4` for bone |
| `BarFlashSeconds` | `4` | How often the bar flashes while a runestone is waiting |
| `BarX` / `BarBottom` / `BarThickness` / `BarLength` | `168` / `75` / `10` / `60` | Place the **fallback** bar only |

A value already written to the `.cfg` beats a new default in code. Change the `.cfg`.

## Status: v1.0

Runs on a listen host and on a dedicated server, and both halves have been exercised: the
ledger, the gate, the pick path, the level curve, the panel, the bar and the compendium tab.
Nineteen runestones, all of them verified to name a field the game actually reads.

What is not done is the part that only play settles. The curve was 60 * N^1.5 and has now
been played once and retuned to 40 * N^1.4; beyond that it has not been played - testing ran on a cheapened one - so the pacing of the whole mod is
unknown, and no value in the catalogue has been tuned against anything but reasoning. The
version says the code is finished, not that the numbers are right.

## Known gaps

- **The shipping curve has never been played.** Everything below it works; how often a stone
  lights up at 40 * N^1.4 is now measured against one session rather than reasoned about,
  but one session is not a playthrough.
  The panel, the bar and the tab have all been seen in game and are correct; this is about
  pacing, not about whether anything draws.
- **Balance rests entirely on the runestones.** With a free choice the strongest runestone is
  always available, so the ordering of `MaxRank` and the per-rank values are doing the work
  the random offer used to do. That has not been played hard enough to find out which one
  wins.
- **Multiplayer has never had a second player.** The ledger, the version gate, the config sync
  and the catalogue hash all work host-side and against a dev server, but no remote client has
  connected, so the join path is exercised only in the shape where the server is also the
  client.
- **Ranks taken before ledger v3 have no level.** The old format recorded a rank and nothing
  else, and picks were not stored in order, so there is no way to tell which level bought
  which rank. Those slots read a dash permanently. Everything taken since is exact.
- **`SE_Stats` has no max health, stamina or eitr field** - those come from food in Valheim -
  so no max-pool runestone is possible without a different vehicle.

## Design notes

Why picks are chosen rather than dealt, why capacity is a capstone, how the bar is built out of
a vanilla one, and why the ledger lives where it does: [DESIGN.md](DESIGN.md).

## Author

Rist is an original mod by **Robbin Thijssen** (Thijssen Software).
Copyright (c) 2026 Robbin Thijssen. MIT licensed. See `LICENSE`.
