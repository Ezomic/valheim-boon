# Boon

A character level beside the skills, carved into runestones you choose and deepen.

Built against the installed game (0.221.12, Unity 6000.0.61, BepInEx 5.4.23.3, Harmony 2.9).
Single DLL plus a text file, no asset bundle.

## What it is

Valheim has skills but no character level. Boon adds one that runs **alongside** the skill
system — vanilla skills are read, never written, never rescaled, never reskinned.

Every character level earns one **pick**. Spend it on any runestone in the catalogue and that runestone
gains a rank, up to **five**. Picks bank: nothing expires, and the panel waits.

There are no drawbacks. A runestone is a reward or it is nothing.

### It used to deal three at random

The first version dealt three runestones per level, seeded so that quitting on a bad offer and
coming back re-offered the same three — otherwise the pick is theatre, since anyone can reroll
until they get what they wanted.

Choosing freely deletes that whole problem rather than defending against it. There is no roll
to reroll, no offer to store in the ledger or on the wire, and no second window with its own
visibility rules. What it costs is the tension of a hand you are dealt: a free choice means the
strongest runestone is always available, so balance now has to live in the runestones themselves and in
`MaxRank` rather than in whether one happened to come up.

The panel earns its keep here. Choosing between nineteen runestones is only a real choice if each
one says what it is worth now **and** what the pick would make it, which is why every tile
carries both lines.

## Why boons, and why capacity is the bottom of one

This started as an inventory problem. Late-biome kit eats the grid - armour, a weapon, a bow,
ammo, five tools, a torch, food, mead - and there is very little left for what you went out to
fetch.

The cheap fix is to add rows. `Inventory.m_height` is one int and `InventoryGrid` rebuilds
itself when it changes, so more space is a twenty-line mod. That is also exactly the answer
[Hoard](../hoard) argues against: the mid game is a logistics problem, and the tedium and the
difficulty are the same mechanic seen from two sides.

So capacity is earned, and it is a **capstone** rather than a boon of its own. It was a boon
once, called Deep pack, and it was the strongest thing in the catalogue - taken first every
time, which is not a choice. A row now sits at the bottom of Ox-backed and of Sure hand: the
reward for taking a hauling boon the whole way.

## The panel

A fifth tab on the compendium bar, beside the raven and the trophy, opens a full screen of
runestones - one per boon. Each carries its own outline, its own rock and its own runes, all
seeded off the runestone id, so a boon looks the same in every session and on every machine: the
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
the first hours and dry up exactly when the deep ranks start to matter — and would make
grinding a fresh cheap skill from zero the fastest way to farm picks.

## The bar

The experience bar is a **clone of one of the game's own upright bars** — the eitr bar, with
the stamina bar behind it as a fallback donor.

The first version drew two flat rectangles in IMGUI. It sat in the right place and still read
as a mod, because a vanilla bar is not a rectangle: it carries a frame sprite, a bevelled
inner track, softened ends, and a second fill behind the first that lags a change. None of
that survives being approximated with a 1×1 texture. Cloning the real thing inherits all of
it at once — the same argument as borrowing a material rather than authoring one.

`GuiBar` only ever resizes its fill on `RectTransform.Axis.Horizontal`, so an upright vanilla
bar is a **rotated horizontal one**. That is why the bar is cloned rather than built: the
geometry cannot be reproduced without it.

What the clone gives for free:

- the frame, track and fill sprites, at whatever HUD scale is set
- the fast/slow pair, so a level-up **drains** rather than snapping to empty
- the fade the bar already uses to show and hide itself
- the flash it already has, fired every few seconds while a runestone is waiting
- a TextMeshPro number in the game's own font, reused for the level

Two things are read off components rather than off child names, because names are scene data
and would be a guess: the trailing bar is the one with `m_smoothDrain` set, and the animator
is driven only through parameters it is confirmed to have.

Position is **screen pixels**, not the donor's `anchoredPosition` — `Hud.UpdateEitr` rewrites
that every frame `(0,130)`, or `(0,285)` with the build HUD up, so it is never a stable thing
to copy. Being pinned in screen space means the bar also has to make vanilla's own
build-panel dodge by hand, which is what `BarBuildRaise` is.

The old IMGUI bar is kept as the **automatic fallback**. A HUD hierarchy is scene data rather
than API, so this can break under a game update in a way ilspy cannot warn about, and falling
back to a bar that certainly draws beats falling back to an empty corner.

### What the clone does not give: its own lifecycle

The bar took four rounds to get right, and every fault had one cause. A cloned component has
**never run its own `Awake`, `OnEnable` or `LateUpdate`** - the eitr donor sits inactive for a
character with no eitr - and `GuiBar` puts something essential in each of them:

| Where | What it does | What went wrong |
| --- | --- | --- |
| `Awake` | caches `m_barImage` | `SetColor` returns silently, so the bar wore the donor's own colour |
| first `SetValue` | re-reads `m_width` off the fill's current size | `SetWidth(240)` was discarded, so 43% drew as 43% of the donor's 64 |
| `LateUpdate` | the only caller of `SetBar` | later `SetValue`s stored a number and drew nothing |

Each is a different way of not reaching `SetBar`, which is one line:

```csharp
m_bar.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, m_width * i);
```

So the fill is written directly and the trailing bar lagged by hand. Nothing is cached and
nothing waits on a callback that is not coming.

The lesson generalises past this bar, and it is not "do not clone": **clone a HUD object for
its geometry and its sprites, then drive its state yourself.** The frame, the bevelled track,
the softened ends and the rotation are all worth inheriting and none are worth rebuilding. It
is only the value machinery that assumes it is running on a live scene object.

The middle fault is also a small lesson in reading a symptom. The fill was authored **magenta**
(`1, 0.294, 0.939`) under a greyscale sprite, so a tint that reached nothing did not look
untinted - it looked deliberately pink, which reads as a shader or colour-space fault and sends
you looking in entirely the wrong place.

## Dying no longer costs skill progress

Valheim already has a world modifier for this and it is **not enough**. `Skills.OnDeath` calls
`LowerAllSkills(m_DeathLowerFactor * Game.m_skillReductionRate)`, and inside that method only
the level loss is scaled by the factor:

```csharp
m_level -= m_level * factor;   // scaled — zero factor means no loss
m_accumulator = 0f;            // NOT scaled — always wiped
// ...and "$msg_skills_lowered" is shown regardless
```

Setting the world's skill reduction to zero therefore still throws away partial progress
toward the next level in *every* skill, and still announces that skills were lowered. On a
skill in the seventies, where one level is a long grind, the accumulator is the part that
actually hurts. Boon skips the method, which removes all three together.

## Where progress lives, and why it is not on your character

**On the server, keyed by the platform identity of the connection and the character being played.**

This is the load-bearing decision. Valheim keeps characters entirely client-side — even
`ZNet.SaveOtherPlayerProfiles` only sends each client an RPC telling it to save its own
profile locally, so the server never holds a character file. Anything stored on the character
is therefore editable by its owner, which rules it out for anything that decides rewards.

Half the key is `ISocket.GetHostName()`, read server-side from the peer's own socket. That
identity is established by the platform before any of this code runs, so a client cannot claim
someone else's record. `Player.GetPlayerID()` was the obvious alternative and is wrong on its
own: it arrives from the client and could name anyone.

It takes both halves, and each one alone was tried. The platform identity is per *account*, so
every character on one machine shared a record and a remade character inherited the last one's
level and runestones - which is exactly what happened the first time a character was remade.
The character id alone is client-supplied. Together, the platform half fences a player into
their own account's records and the character half separates their characters within it.

The client's half of the protocol is deliberately thin. It reports **that a skill went up**
and **which runestone it wants**. It never sends a level, an XP total or a rank, because the server
does not store anything the client says — only re-derives from the events it claims.

### Singleplayer

Works, with no special casing. Valheim runs a local server in singleplayer, and `ZRoutedRpc`
handles a message addressed to yourself locally rather than putting it on a socket:

```csharp
if (targetPeerID == m_id || targetPeerID == 0L) HandleRoutedRPC(data);
```

Since `GetServerPeerID()` returns your own id when you are the server, skill reports, picks and
state pushes all loop straight back and resolve against the local ledger. Progress is stored
under the owner `localhost@<character id>`.

The only gap is that the host has no peer entry for itself, so nothing greets it on spawn —
hence a one-shot seed of the opening state. Everything after arrives by the same path a
client's would.

The gate is pointless here and effectively inert: on your own machine you are the one it would
be protecting you from.

## The gate, and how small a claim it makes

Boon used to refuse the connection. A character that had ever spawned in another world was
sent `ConnectionStatus.ErrorKicked` and dropped, on the reasoning that a character which has
only been here cannot have been levelled elsewhere.

That was the wrong power for this mod to hold. A levelling mod deciding who may play means a
bug in an XP system locks people out of the server, and it did: the player got Valheim's
generic kick screen with the reason only in the server's log, so a refusal and a crash looked
identical from their side. The check itself was also stricter than it sounded — "has this
character been anywhere else" refuses a character on every world but its first, permanently,
because `m_worldData` entries are never removed. Visiting a friend's world once cost you this
server for good.

That whole judgement now lives in [Threshold](../threshold), where turning people away is the
entire job and is done openly with its own message. What is left here is strictly about
payment, and it is a much smaller claim: **this server decides what it is willing to pay for,
not who may play.**

### What the server remembers

Every accepted skill-up raises a per-character baseline — the highest level this world has
itself watched each skill reach. That baseline is the one fact in the whole system a client
cannot touch. Character files sit on the player's own disk and are unencrypted ZPackages, so
anything read out of one is self-reported; the baseline is the server's own memory.

A character that comes back with skills above it gained them somewhere else, whatever its file
says about where it has been. It plays completely normally and simply earns nothing until the
numbers line up, and it is told so on screen — withholding is invisible by nature, and a
player who cannot tell they have stopped earning is the same complaint that killed the kick.

Nothing already earned is ever taken away. `SkillDriftAllowance` gives a level of slack,
because the ledger is flushed on a timer and a server that stops unexpectedly can lose the last
few seconds of baseline updates; one level absorbs that and is worth nothing to a cheat.

### Three ceilings, because a report cannot be verified

Skills live on the client. `Player.OnSkillLevelup` fires there, so a skill-up report is a claim
about something the server never saw, and no amount of checking makes it otherwise. What is
possible is bounding what a claim can be worth, and there are exactly three levers:

| Ceiling | Bounds |
| --- | --- |
| `MaxSkillUpsPerMinute` | how many claims are accepted |
| `MaxSkillLevelJump` | how much one claim may say |
| `MaxXpPerMinute` + `XpBurst` | how much anything can be worth over time |

The first alone was not enough, and the gap is arithmetic: XP is the skill level reached, so
thirty reports a minute each naming a level-100 skill was worth 3,000 XP — a character level
every ten seconds — and each of those forged levels was adopted into the baseline, giving the
next claim an alibi.

`MaxSkillLevelJump` closes that by refusing anything more than one level above the baseline,
which is what the game itself does: `Skills.Skill.Raise` levels one at a time and fires one
callback each. It needs a complete login baseline to mean anything, so it is enforced only for
characters this world has fully seen — an absent baseline entry is otherwise indistinguishable
from a skill that has genuinely never been used.

`MaxXpPerMinute` is the only cap that does not depend on believing the client at all. However
convincing a report is, time connected is a quantity the server measures for itself. It is a
token bucket rather than a tripwire, banking three minutes by default, so an honest burst —
clearing a crypt levels four skills at once — is still paid in full.

### What this does not do

None of it detects a cheat; it bounds one. A purpose-built client can still claim a plausible
level-up it did not earn, at the honest rate, forever. What the ceilings guarantee is that
doing so is no faster than playing, which is the only property worth having here — and it is
the reason the whole thing withholds rather than punishes. A false positive costs a player
some XP and says so on screen, not their seat on the server.

## Runestones

`cards.txt` sits beside the DLL. One runestone per line:

```
id | Name | flavour text | effect | value-per-rank
```

`effect` is the literal name of a public float field on the game's own `SE_Stats`. That is
what makes the catalogue a text file rather than a switch statement: Valheim already sums
about forty-five of these across active status effects — carry weight, armour, the six stamina
costs, stealth, fall damage, regen rates, skill gain — so naming one turns it into a runestone with
no code change and no rebuild.

`*inventoryrow` is the single special, because grid height is not a stat.

An unknown field name is **logged and skipped**, not thrown, matching how the game treats a
prefab name that does not resolve. A typo costs one runestone, not the catalogue.

## Core is optional, and here is exactly what that costs

Boon installs and runs on its own. Core is a **soft** dependency: install Boon by itself and
it works, including the extra inventory rows. **Solo, you need nothing else and give up
nothing.**

On a server it is a different question, and Boon gives up more without Core than the rest of
this suite does. Three things, all of which matter only in multiplayer:

**The catalogue is no longer checked.** `cards.txt` names what every rank is worth, effects are
applied client-side from it, and the server only ever verifies the *rank* — so an edited line
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
once, so mods stack instead of fighting. Standalone Boon owns that write alone — correct on its
own, and **in conflict with any other mod that grants inventory rows**, with the winner decided
by frame ordering rather than by anything either mod can control. If you run another row-adding
mod, install Core.

What is *not* given up is correctness of the rows themselves. The standalone owner carries the
`Player.Load` widening in full, which is not a nicety: without it every item sitting in a
granted row is destroyed by loading the game, silently, because `AddItem` drops any saved
position outside the current grid and the row is not applied until after the load. That bug
cost a real heartwood to find, and the fallback would not have shipped without the fix.

Boon logs a warning at startup when it starts without Core, naming all three.

## Config

`BepInEx\config\ezomic.valheim.boon.cfg`

| Key | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Off keeps records but stops granting and applying |
| `XpPerSkillLevel` | `1` | XP per skill-up, multiplied by the level reached |
| `LevelBaseXp` | `40` | Cumulative XP for level 1 |
| `LevelExponent` | `1.4` | Cumulative XP for level N is base × N^exponent |
| `MaxRank` | `5` | How deep one runestone goes, and how many slots its track shows |
| `BonusEvery` | `5` | Ranks between capstones |
| `ShowInfoTab` | `true` | Add the boons tab to the compendium bar |
| `RemoveDeathSkillLoss` | `true` | Skip `Skills.OnDeath` |
| `CheckSkillBaseline` | `true` | Compare a joining character against what this world watched it reach |
| `WithholdUntrustedXp` | `true` | Pay nothing while it sits above that; off logs only |
| `SkillDriftAllowance` | `1` | Levels of slack before a returning character counts as imported |
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
| `BarSize` | `240` | Length in canvas units — 64 is a starting stamina bar |
| `BarBuildRaise` | `155` | Pixels to lift the bar while the build or ship panel is open |
| `BarColour` | `4FB3A5` | `RRGGBB`; the trailing fill is the same hue held back. `E4DCC4` for bone |
| `BarFlashSeconds` | `4` | How often the bar flashes while a runestone is waiting |
| `BarX` / `BarBottom` / `BarThickness` / `BarLength` | `168` / `75` / `10` / `60` | Place the **fallback** bar only |

A value already written to the `.cfg` beats a new default in code — change the `.cfg`.

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
- **A character judged untrusted may not recover on its own.** XP is withheld and the baseline
  stops advancing with it, so "until they line up again" has no path back within a session
  short of the skills being re-seen at login. Not yet hit in practice; if a player reports
  earning nothing forever, that is where to look.
- **Ranks taken before ledger v3 have no level.** The old format recorded a rank and nothing
  else, and picks were not stored in order, so there is no way to tell which level bought
  which rank. Those slots read a dash permanently. Everything taken since is exact.
- **`SE_Stats` has no max health, stamina or eitr field** - those come from food in Valheim -
  so no max-pool runestone is possible without a different vehicle.
- **Nobody is backfilled.** With only server-observed gains counting, a character arriving at
  skill 50 still starts at Boon level 0. That follows from the anti-cheat choice rather than
  being an oversight.

## Author

Boon is an original mod by **Robbin Thijssen** (Thijssen Software).
Copyright (c) 2026 Robbin Thijssen. MIT licensed — see `LICENSE`.
