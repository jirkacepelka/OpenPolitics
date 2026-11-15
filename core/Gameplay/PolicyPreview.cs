namespace OpenPolitics.Core.Gameplay;

/// <summary>What one policy move does once it has worked through: per value, per voter group and for the budget.</summary>
/// <param name="Values">One entry per value the policy acts on directly.</param>
/// <param name="Groups">Voter groups with an opinion on the policy; positive pressure means they like the move.</param>
/// <param name="BudgetPercentOfGdp">Change of the deficit in percent of GDP (positive widens it).</param>
/// <param name="CapitalCost">Political capital the leader pays for this change (0 without politics, see <see cref="Game.PolicyChangeCost"/>).</param>
/// <param name="Affordable">Whether the player's party has that much capital now.</param>
public sealed record PolicyPreview(
    string PolicyId, double From, double To,
    IReadOnlyList<ValueEffect> Values, IReadOnlyList<GroupEffect> Groups, double BudgetPercentOfGdp,
    double CapitalCost = 0.0, bool Affordable = true);

/// <summary>Additive is value units per year, Relative is percent per year (the edge's own units).</summary>
public sealed record ValueEffect(string ValueId, double Additive, double Relative);

/// <summary>Change of the group's satisfaction pressure per year caused by the policy setting itself.</summary>
public sealed record GroupEffect(string GroupId, double Pressure);
