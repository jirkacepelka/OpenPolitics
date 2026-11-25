# 0020: A lean programme, free programme changes, a start that differs from game to game

Status: accepted. Date: 2026-11-25.

## Context
Playtest notes: changing the programme should not cost points; the programme should start really lean (about six laws) now that there are 88
laws; and every game started the same, with the same parties in government at the same poll shares.

## Decision
1. **Programme changes are free.** Adding or removing a law, shifting a position and changing a priority cost no political capital
   (`PartyRules.AddCost`, `RemoveCost`, `ShiftCost`, `PriorityCost` = 0). What limits them is credibility: a shift costs credibility by its size,
   a removed law costs 0.1 (voters notice a party dropping a position), a shift moves at most a tenth of the range at a time.
2. **The player's programme starts with six laws** (`PartyRules.StartProgramme`): the ones the party stresses most (platform weight, ties by id).
   The rest of its platform is for the player to add. The AI parties keep their full platforms (they feed the compass, the distances in talks and
   the AI governor). The start polls are calibrated after the trimming, so they still match the scenario.
3. **A random start, per game.** `Simulation.NewGame(..., randomStart: true)` (the client's New game; saved in the save file as `random_start`)
   scatters every party's poll share by `exp(start_noise x z)` (z roughly normal, from its own stream of the seed, rescaled to the same total;
   `start_noise: 0.25` in the three political scenarios) and forms the government from the seats those shares give: every seated party works
   out the coalition it could lead, and one is drawn with a weight of its seats times `exp(8 x (market score - best))`, so the better and larger
   parties lead more often but not always. The starting agreements and portfolios are what the AI formateur would offer. Without the flag the
   scenario's own start is used (the tests and the scripted smoke test rely on it).
4. **The party table follows the seed.** `PartyChoice.Options(packs, scenario, seed, randomStart)`: the new-game screen recomputes roles, polls and
   seats when the seed changes, so what it shows is what the game starts with.

## Consequences
In 40 seeds of the base scenario the leader and the coalition vary (at least 3 leaders, 4 coalitions) and every start has a seat majority.
Random-start games run twenty years with a sitting government most of the time in the proportional and the districts scenario.
