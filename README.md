# Boon

A character level beside the skills, paid out in cards you draft and deepen.

Built against the installed game (0.221.12, Unity 6000.0.61, BepInEx 5.4.23.3, Harmony 2.9).
Single DLL plus a text file, no asset bundle.

## What it is

Valheim has skills but no character level. Boon adds one that runs **alongside** the skill
system — vanilla skills are read, never written, never rescaled, never reskinned.

Levelling that character up offers you **three cards at random**. Pick one and it gains a
rank, up to **five**. Refusing the other two does not remove them; they come round again.

There are no drawbacks. A card is a reward or it is nothing.

## Why cards, and why capacity is one of them

This started as an inventory problem. Late-biome kit eats the grid — armour, a weapon, a
bow, ammo, five tools, a torch, food, mead — and there is very little left for what you went
out to fetch.

The cheap fix is to add rows. `Inventory.m_width/m_height` are two ints and `InventoryGrid`
rebuilds itself when they change, so more space is a twenty-line mod. That is also exactly
the answer [Hoard](../hoard) argues against: the mid game is a logistics problem, and the
tedium and the difficulty are the same mechanic seen from two sides.

So capacity is a card. **Deep pack** is drafted like anything else, competing with armour and
stamina for the same pick. The space is earned rather than granted, which is the only version
that leaves the system it belongs to intact.

## XP

Skill level-ups, and nothing else. Nearly every activity in Valheim raises *some* skill, so
building, sneaking, sailing, cooking and fishing all pay in without this mod maintaining a
table of what counts.

XP is **weighted by the skill level reached**, not flat per level-up. Vanilla's own skill cost
curve is `pow(level+1, 1.5)`, so early levels are nearly free. A flat rate would rain cards in
the first hours and dry up exactly when the deep ranks start to matter — and would make
grinding a fresh cheap skill from zero the fastest way to farm drafts.

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

**On the server, keyed by the platform identity of the connection.**

This is the load-bearing decision. Valheim keeps characters entirely client-side — even
`ZNet.SaveOtherPlayerProfiles` only sends each client an RPC telling it to save its own
profile locally, so the server never holds a character file. Anything stored on the character
is therefore editable by its owner, which rules it out for anything that decides rewards.

The ledger is keyed on `ISocket.GetHostName()`, read server-side from the peer's own socket.
That identity is established by the platform before any of this code runs, so a client cannot
claim someone else's record. `Player.GetPlayerID()` was the obvious alternative and is wrong:
it arrives from the client.

The client's half of the protocol is deliberately thin. It reports **that a skill went up**
and **which card it wants**. It never sends a level, an XP total or a rank, because the server
does not store anything the client says — only re-derives from the events it claims.

### Singleplayer

Works, with no special casing. Valheim runs a local server in singleplayer, and `ZRoutedRpc`
handles a message addressed to yourself locally rather than putting it on a socket:

```csharp
if (targetPeerID == m_id || targetPeerID == 0L) HandleRoutedRPC(data);
```

Since `GetServerPeerID()` returns your own id when you are the server, skill reports, picks and
state pushes all loop straight back and resolve against the local ledger. Progress is stored
under the owner `localhost`.

The only gap is that the host has no peer entry for itself, so nothing greets it on spawn —
hence a one-shot seed of the opening state. Everything after arrives by the same path a
client's would.

The gate is pointless here and effectively inert: on your own machine you are the one it would
be protecting you from.

## The fresh-character gate

The real defence is not rate limiting, it is refusing characters that could have been levelled
elsewhere. `PlayerProfile.m_worldData` is a dictionary keyed by **world UID** with one entry
per world a character has spawned in, and `m_usedCheats` is a flag the game maintains itself.
A character that has only ever been on this world cannot have been levelled anywhere else, and
there is then nothing to verify.

Checked on **every login**, not only the first, so a character taken away to a creative world
and brought back is caught on return rather than waved through because it was clean once.

**Enforcement is on.** A refused player is sent `ConnectionStatus.ErrorKicked` and dropped.
Set `GateEnforce = false` to fall back to logging what would have been refused without
turning anyone away.

### What this costs

The rule is stricter than it sounds, in three ways:

- **It is symmetric.** "Has this character been on any world other than this one" refuses a
  character on *every* world but its first. A character bound to this server is refused by any
  other modded world, and vice versa. In practice that means **one character per modded world**.
- **It is permanent.** `m_worldData` entries are created as soon as a character explores or
  logs out, and nothing ever removes them. Visiting a friend's world once locks that character
  out of this server for good, with no way to clear the record.
- **It does not protect Boon levels** — the ledger already does that completely. Levels come
  only from skill-ups reported while connected here, and nothing is backfilled, so a character
  that grinds skills elsewhere returns with **zero** extra Boon levels. What the gate actually
  stops is a maxed-out *vanilla* character walking in: skills at 100, map explored. That is a
  real concern about server balance, but it is a different one.

A refused player is sent `ConnectionStatus.ErrorKicked` before being dropped, the same way
ZNet turns away a wrong-version client, so they see a message rather than an unexplained
disconnect.

### A character backup is the way back in — and is not a hole

Character files live client-side in
`%USERPROFILE%\AppData\LocalLow\IronGate\Valheim\characters`, and `m_worldData` is serialised
into them. **Restoring a backup taken before the trip clears the travel record and the gate
passes again.** That is the escape hatch for the permanent lockout above: back a character up
before taking it anywhere else.

It is not a way to smuggle progress in, because a restore rolls back *everything* — the
skills gained on the other world go with the travel record, which is exactly the thing anyone
would have gone there to get. Backing up, levelling elsewhere and restoring nets nothing.

What it does not defend against is a **hand-edited** character file, stripping the world
entries while keeping the skills. The file is an unencrypted ZPackage, so that is possible for
someone willing to write a parser. Combined with the fact that these facts are self-reported
in the first place, the gate stops the ordinary case and not a determined one.

### What this does not do

The gate reads a client-side file, so the facts are self-reported and a purpose-built modified
client can forge them. What it reliably catches is the ordinary case — an unmodified player
who levelled elsewhere or used `devcommands` — because the game records both and has no reason
to lie. The rate limit behind it bounds the damage of a forged report rather than detecting
one. Nothing here is airtight, and it is not presented as such.

## Cards

`cards.txt` sits beside the DLL. One card per line:

```
id | Name | flavour text | effect | value-per-rank
```

`effect` is the literal name of a public float field on the game's own `SE_Stats`. That is
what makes the catalogue a text file rather than a switch statement: Valheim already sums
about forty-five of these across active status effects — carry weight, armour, the six stamina
costs, stealth, fall damage, regen rates, skill gain — so naming one turns it into a card with
no code change and no rebuild.

`*inventoryrow` is the single special, because grid height is not a stat.

An unknown field name is **logged and skipped**, not thrown, matching how the game treats a
prefab name that does not resolve. A typo costs one card, not the catalogue.

## Config

`BepInEx\config\ezomic.valheim.boon.cfg`

| Key | Default | What it does |
| --- | --- | --- |
| `Enabled` | `true` | Off keeps records but stops granting and applying |
| `XpPerSkillLevel` | `1` | XP per skill-up, multiplied by the level reached |
| `LevelBaseXp` | `60` | Cumulative XP for level 1 |
| `LevelExponent` | `1.5` | Cumulative XP for level N is base × N^exponent |
| `MaxRank` | `5` | How deep one card goes |
| `OfferCount` | `3` | Cards per draft |
| `RemoveDeathSkillLoss` | `true` | Skip `Skills.OnDeath` |
| `RequireFreshCharacter` | `true` | Run the gate check |
| `GateEnforce` | `true` | Disconnect a refused player; off logs only |
| `MaxSkillUpsPerMinute` | `30` | Server-side ceiling on accepted reports |
| `Verbose` | `false` | Log every grant, rejection and card applied |

A value already written to the `.cfg` beats a new default in code — change the `.cfg`.

## Status: v0.1 — untested

Builds and deploys. **It has never been run in game.** Nothing below has been observed working.

## What to check first

1. Raise any skill and watch the log for an XP grant on the server side.
2. Reach level 1 and confirm the draft window appears, and that the **mouse works** in it —
   that is what the four input patches are for, and it is the usual thing to get wrong.
3. Take **Deep pack** and confirm the inventory gains a row, and that it does *not* gain
   another on reload. The base height is captured once for exactly that reason.
4. Take a stat card and confirm the number moves — carry weight is the easiest to read.
5. Die, and confirm no skills dropped and no "skills lowered" message appeared.
6. Quit on an offer and come back: **the same three cards** should be waiting. If they are
   not, the seeding is wrong and the pick is meaningless.
7. Read the log for `Gate (not enforcing): would have refused …` and see whether it is
   flagging characters you expected.

## Known gaps

- **No level or XP display outside the draft window.** There is nowhere to see progress
  between levels yet.
- **The card icon from the mockup is not drawn.** It would need a sprite asset, and this mod
  is deliberately a DLL and two text files.
- **`SE_Stats` has no max health, stamina or eitr field** — those come from food in Valheim —
  so no max-pool card is possible without a different vehicle.
- **A permanent status effect may show in the HUD status bar** with no icon. Not yet seen in
  game; if it looks wrong, that is where to look.
- **Nobody is backfilled.** With only server-observed gains counting, a character arriving at
  skill 50 still starts at Boon level 0. That follows from the anti-cheat choice and is worth
  confirming you want.

## Author

Boon is an original mod by **Robbin Thijssen** (Thijssen Software).
Copyright (c) 2026 Robbin Thijssen. MIT licensed — see `LICENSE`.
