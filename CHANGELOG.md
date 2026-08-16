# Changelog

Notable changes to Boon. Format follows [Keep a Changelog](https://keepachangelog.com),
and the mod uses [semantic versioning](https://semver.org).

## [0.1.0] — 2026-08-16

Written and building. **Never run in game.**

### The line this sits on

> **It adds a level beside the skills. It never reads, writes, rescales or reskins a vanilla skill.**

Valheim has skills but no character level. Boon adds one that runs alongside them. Vanilla
skills are read and never written.

### Levels and picks

- Every character level earns one **pick**, spent on any runestone in the catalogue to raise it a
  rank, up to five.
- **Picks bank.** Nothing expires and the panel waits.
- **No drawbacks.** A runestone is a reward or it is nothing.

### Free choice, not a dealt hand

An earlier version dealt three runestones per level, seeded so that quitting on a bad offer and
returning re-offered the same three — otherwise the pick is theatre, since anyone can reroll
until they get what they want.

Choosing freely deletes that problem rather than defending against it: no roll to reroll, no
offer to store in the ledger or on the wire, no second window with its own visibility rules.
What it costs is the tension of a hand you are dealt. A free choice means the strongest runestone
is always available, so balance now lives in the runestones themselves and in `MaxRank`.

The panel carries the weight for this: choosing between fourteen runestones is only a real choice
if each tile says both what it is worth now and what the pick would make it.

### Capacity is a runestone, not a setting

This began as an inventory problem — late-biome kit eats the grid and leaves nothing for what
you went out to fetch. The cheap fix is more rows, and that is exactly what
[Hoard](../hoard) argues against. So capacity is **Deep pack**, taken like anything else and
competing with armour and the rest.

### Known limits

- **Never played.** The free-pick path has been exercised only against a deliberately
  cheapened curve, not a real one.
- **The play profile still carries the test curve.** `LevelBaseXp = 5` and
  `LevelExponent = 1` were set for testing so the pick path could be reached without
  grinding; the server profile is untouched at `60` / `1.5`. This must be restored before
  release, and because BepInEx's saved value beats any default in code, the fix is to edit
  the `.cfg` rather than the C#.
- Runestone balance is untested by definition: with free choice, the strongest runestone is always
  available, and nothing has yet been played hard enough to find out which one that is.
