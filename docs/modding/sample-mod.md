# Sample mod

`data/examples/sample-mod` is a complete small mod. It depends on `base` and does three things:

1. Adds a policy, `sample:green_subsidy` (spending of 0 to 3 percent of GDP, default 0, takes 2 turns to act).
2. Adds an edge, `sample:green_subsidy_to_environment`, so the subsidy raises `base:environment` (with a 4-turn lag).
3. Overrides a base policy: `base:police` gets the default 2.0 (the entry carries `override: true`).

It also adds Czech and English names for the new policy (`localization/cs.yaml`, `en.yaml`).

Validate and play it on top of the base pack (order matters: the base comes first):

```bash
dotnet run --project tools/OpenPolitics.Cli -- validate data/base data/examples/sample-mod
dotnet run --project tools/OpenPolitics.Cli -- run --packs data/base data/examples/sample-mod --turns 20
```

`data/examples/broken-mod` is the opposite: a deliberately invalid pack that shows what validator errors look like.
