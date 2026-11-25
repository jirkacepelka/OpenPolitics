# 0021: Laws in force, a moving population, prices, private pensions and an audit of effects

Status: accepted. Date: 2026-11-26.

## Context
The player asked for: laws that can be brought in and repealed like in Democracy 4 (only what is in force on the main page, the rest in a tab
with buttons to add it); voter groups whose size changes with a moving population (poor households that do well move up, and the other way;
migration and family support raise the population, which first raises the cost of schools); an audit of what laws *do* and not only whether the
numbers balance (a consumption tax raises the price level once, it does not raise inflation for ever; a later retirement age must lower the
number of pensioners in the long run); private pensions; irrelevant laws (the length of the working week) deleted; more than three voter groups.

## Decision
1. **Laws in force.** Every law is in force or not (`PolicyDef.StartActive`; `Game.ActivePolicies`). A law not in force is held at its neutral default,
   changes nothing and is not on the map; it cannot be set (`error.policy_inactive`). `IntroducePolicy` brings one in at its introduction setting
   (`intro_value`, else the default, else a fifth of the range) after its delay for twice its capital weight plus the move; `AbolishPolicy` sends it
   back to the default for its weight plus the move. Laws the state needs (`abolishable: false`: the basic taxes and budgets, the retirement age,
   the electoral rules, migration, environment and business rules, the energy mix) cannot be repealed. A measure of an AI government that touches a
   law not in force brings it in. The map shows only laws in force; the Laws tab lists the others with a "Bring in" button; the policy dialog has "Repeal".
   Both actions are saved and replayed. Deleted for being irrelevant: the working week, strike rules, religion in schools.
2. **Population** (`DemographyModelV1`). Children, young adults, workers and pensioners change by births (fertility), ageing, deaths (life expectancy)
   and migration; the adults are spread over five income classes (poor, lower, middle, upper-middle, high) that move up and down by `mobility_up` and
   `mobility_down`, class factors chosen so the starting distribution is the resting point. Fertility, life expectancy, net migration, mobility and
   urbanisation are ordinary values steered by edges: family allowances, childcare, housing and paid leave raise births; migration, asylum and
   citizenship raise migration; growth, jobs, education and low inequality raise the climb, unemployment, inflation and inequality the slide. A
   later retirement age lowers the inflow into retirement, so pensioners fall over decades. The labour force follows the working-age population.
3. **13 voter groups, overlapping, with sizes that follow the population** (`size_value` / `size_factor` / `size_offset`): five income classes, pensioners,
   young adults, families with children, city dwellers, rural conservatives, unemployed (follows unemployment), entrepreneurs, public-sector workers.
   `Game.Groups` has the sizes now; politics and the support model take them every quarter.
4. **Spending follows the head count** (`cost_driver`). Education follows pupils, pensions the pensioners (less what private funds cover), health care and
   elderly care the pensioners, family allowances, childcare and school meals the children, benefits and housing the poor. The bill grows with the load
   to the power 0.45 (benefits follow prices more than wages). Above 100 % of GDP in debt the state is forced to consolidate (up to 30 % of spending).
5. **Prices.** `kind: level` edges shift a target's level once, by the change of the edge's delayed influence (strength in percent). Taxes, the minimum
   wage, energy, competition and farm subsidies act on the price level this way; `base:inflation` is now the price level against a year ago, so a tax
   shows as a jump for a year and then drops out; `base:core_inflation` is the underlying rate the model steers.
6. **Private pensions** (`PensionModelV1`). Participation moves toward a target that rises with growth, with a small state pension and a late retirement
   age and with the state's help: a contribution (`pension_subsidy`), a tax relief (`pension_tax_relief`) or a compulsory share of the social contributions
   (`mandatory_pension_pillar`, a floor of coverage, with years of lost revenue). The funds earn the real rate plus 2 % and pay out half their assets over
   the years retirees have left; net saving becomes capital, payouts lower the pension burden.
7. **An audit of effects** (`PolicyAuditTests`). Every law is raised by half of the room above its default in a game without politics and compared with
   the same game left alone: the signs of the indicators the law is for must be right after four years, the price jump must show in inflation and be gone
   in two to three years, a later retirement age must lower the pensioners' share, and every law must move something that matters (6 % of an indicator's
   scale after six years). Every law needs a row saying what it is for. The audit found weak laws (several were raised) and a mistake of mine (a blanket
   increase of all edges of a law had also raised its side effects).
8. **Physical floors** (`ValueDef.Floor`): crime and inequality cannot fall below a floor however many laws push them.

## Balance
The passive game stays stable for 100 years in 100 seeds and under the random and extreme strategies. An aging country that does nothing runs a growing
deficit (that is the point of 4); the starting fertility (1.75) and migration (3.5 per 1000) are chosen so that the pressure is slow.
