# Data pack format

A **pack** is a folder of YAML files. The base game is the pack `data/base`; mods are more packs loaded after it.
Packs contain **data only** - no code is executed from a pack. All numbers in the shipped packs are illustrative.

```text
my-pack/
  manifest.yaml
  values/*.yaml  policies/*.yaml  edges/*.yaml  voter_groups/*.yaml
  events/*.yaml  countries/*.yaml  scenarios/*.yaml
  localization/cs.yaml  localization/en.yaml
  electoral_systems/    (reserved for a later milestone, not read yet)
```

Each folder holds YAML files that are lists of entries. Ids are namespaced: `pack:name` (`base:gdp`, `mymod:wealth_tax`).
Check a pack at any time with:

```bash
dotnet run --project tools/OpenPolitics.Cli -- validate data/base data/my-pack
```

Errors have a code, file and line (see "Validator codes" below).

## manifest.yaml

```yaml
id: mymod            # unique pack id
version: 0.1.0
license: CC-BY-SA-4.0
illustrative: true   # numbers are approximate, not statistics
dependencies: [base] # packs that must be loaded earlier
```

## Load order and overrides

Packs are applied in the order given on the command line; a later pack wins. Redefining an id that already exists
needs `override: true` on the entry, otherwise the validator reports `E003`. Localization texts of later packs replace
earlier ones. A value and a policy must not share an id.

## Values (`values/*.yaml`)

```yaml
- id: base:unemployment
  unit: percent          # free text shown to players; "percent" values are range-checked by the batch runner
  initial: 5.0           # real units, no fixed range or ceiling
  reference: 5.0         # "normal" level used by edges (default: initial)
  scale: 1.0             # size of a typical deviation used to normalise edges (default: 10% of |reference|)
  owner: macro_v1        # optional: the economy model that computes this value
```

Values with `owner: macro_v1` are computed by the built-in macro model: `base:gdp, capital, productivity, labour_force,
unemployment, inflation, debt, interest_rate, revenue, spending`. All other values are integrated from edges only.

## Policies (`policies/*.yaml`)

```yaml
- id: base:vat
  unit: percent_of_gdp
  min: 4
  max: 24
  default: 12
  delay_turns: 1            # turns until a change takes effect
  revenue_base: base:gdp    # tax policy: revenue = parameter / 100 x this value
  cost_per_unit: 1          # spending policy: spending = parameter x cost_per_unit x GDP / 100
```

`min` and `max` bound the lever the player can move, not any simulated value.

## Edges (`edges/*.yaml`)

An edge makes a source (value or policy) change the **rate of change** of a target value.

```yaml
- id: base:research_to_productivity
  from: base:research          # value or policy
  to: base:productivity        # value
  kind: relative               # additive: units per year; relative: percent growth per year
  strength: 1.5
  fn: {type: linear}           # linear | power(p) | tanh(scale) | threshold(at, width); optional shaping
  delay: {type: lag, turns: 4} # none | lag(turns) | smooth(turns)
```

The source is first normalised: `(x - reference) / scale` (for a policy: reference = default, scale = max - min). The
function turns that into an influence; `strength` converts it to a rate. A cycle of edges with no delay is an error
(`E005`) - give at least one edge a `lag` or `smooth` delay. Persistent pushes on indicators should be balanced by a
self-damping edge (`from` and `to` the same value, negative strength, `lag: 1`) so they settle instead of drifting.
Edges on the derived macro values (gdp, revenue, spending, interest_rate) act for one turn only; route lasting effects
through productivity, capital, labour force, unemployment, inflation or debt.

## Voter groups (`voter_groups/*.yaml`)

```yaml
- id: base:pensioners
  size: 0.20
  weight: 1.0
  value_weights: {base:health: 2.0, base:inflation: -1.5}   # satisfaction reacts to relative yearly change
  policy_prefs: {base:pensions: 11}                          # preferred setting
  policy_weights: {base:pensions: 1.0}                       # how much distance from it hurts
```

Satisfaction is an attitude and the only bounded quantity: it stays in [-1, 1].

## Events (`events/*.yaml`)

```yaml
- id: base:recession
  condition: "base:gdp > 0 and base:unemployment < 12"   # see grammar below
  probability: 0.008           # per turn, when the condition holds
  text_key: event.recession
  effects:
    - {value: base:unemployment, delta: 2.0}
```

Condition grammar: numbers, value ids, `+ - * /`, `< <= > >= == !=`, `and or not`, parentheses. True is 1, false is 0;
dividing by zero gives 0.

## Countries and scenarios

```yaml
# countries/czechia.yaml
- id: base:czechia
  name_key: country.czechia
  parameters: {alpha: 0.35, base_productivity_growth: 0.012}   # overrides of macro_v1 defaults
# scenarios/cz_2015_sandbox.yaml
- id: base:cz_2015_sandbox
  country: base:czechia
  start_year: 2015
  turns_per_year: 4
  start_values: {base:health: 65}     # optional overrides of initial values
  start_policies: {base:vat: 15}      # optional overrides of policy defaults
```

## Localization

`localization/cs.yaml` and `en.yaml` are flat `key: text` maps. Required keys: `name.<id>` for every value, policy and
voter group, plus each country `name_key` and event `text_key`.

## Validator codes

| Code | Meaning |
|---|---|
| E001 | YAML syntax error |
| E002 | unknown field, missing required field, or wrong type |
| E003 | duplicate id (use `override: true` to replace one from an earlier pack) |
| E004 | reference to an unknown value, policy, country or id |
| E005 | cycle of edges without delay |
| E006 | missing localization (language file or key) |
| E007 | value out of range (default outside min/max, scale <= 0, probability outside 0..1, unknown function or delay type) |
| E008 | invalid event condition |
| E009 | dependency missing or loaded after the pack that needs it |
| W001 | file outside a known folder is ignored |

## Headless tools

```bash
# per-turn values of one game, tab separated
dotnet run --project tools/OpenPolitics.Cli -- run --packs data/base --scenario base:cz_2015_sandbox --seed 42 --turns 80
# many games with a scripted strategy; exit code 1 if unstable
dotnet run --project tools/OpenPolitics.Cli -- batch --packs data/base --games 1000 --strategy random --turns 80
```
