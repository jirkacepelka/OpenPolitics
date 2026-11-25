using OpenPolitics.Core.Packs;

namespace OpenPolitics.Core.Politics;

/// <summary>
/// Decision 0019: parties may run a joint list before an election, with a joint programme. Constants: the window (quarters before the election in
/// which the player may form one; the AI forms its own <see cref="AiTurn"/> quarters ahead), at most <see cref="MaxMembers"/> parties, how far apart
/// their programmes may be, the votes a joint list loses (base + per unit of distance), the higher threshold of a joint list in proportional
/// elections (x <see cref="ThresholdFactor"/> per extra member: 5 % becomes 8 % for two and 11 % for three), the cost for the player.
/// </summary>
public static class AllianceRules
{
    public const int WindowTurns = 8, AiTurn = 4, MaxMembers = 3;
    public const double MaxDistance = 0.45, LossBase = 0.03, LossPerDistance = 0.35, ThresholdFactorPerExtra = 0.6;
    public const double Cost = 3, LeaveCredibility = 0.1;
    /// <summary>Credibility the player's party loses on joining a list whose programme contradicts its own: this times the distance.</summary>
    public const double JoinCredibilityPerDistance = 0.5;

    public static double ThresholdFactor(int members) => 1.0 + ThresholdFactorPerExtra * (members - 1);
}

/// <summary>
/// A joint list: Members (ordinal), their programme's Coherence (1 minus the mean distance between the members' programmes), the joint Platform
/// (the members' positions merged by support and priority) and what each member loses: a party whose voters are asked to back a programme that
/// contradicts its own loses some of them, in proportion to how far the joint programme is from its own (MemberLoss; Loss is the mean).
/// ByPlayer: the player formed it (or is in it).
/// </summary>
public sealed record Alliance(IReadOnlyList<string> Members, double Coherence, double Loss, IReadOnlyDictionary<string, PlatformPosition> Platform, bool ByPlayer,
    IReadOnlyDictionary<string, double>? MemberLoss = null)
{
    /// <summary>The share of a member's own voters the joint list loses: the farther the joint programme is from the member's own, the more of them stay away.</summary>
    public double LossOf(string member) => MemberLoss?.GetValueOrDefault(member, Loss) ?? Loss;

    /// <summary>A member's votes that count for the list.</summary>
    public double Counted(string member, double votes) => votes * (1.0 - LossOf(member));

    /// <summary>The id the joint list carries in the election (one list in the districts and in the proportional count).</summary>
    public string ListId => AllianceMath.ListId(Members);
}

/// <summary>One member's view of a proposed alliance: its seats alone and together at the current poll, and whether it would join.</summary>
public sealed record AllianceMemberView(string PartyId, int SeatsAlone, int SeatsTogether, bool Accepts, string? Reason, double Loss = 0.0);

/// <summary>What forming an alliance would do (Changes nothing): a hard error, or the list's coherence, vote loss, threshold factor and each member's view.</summary>
public sealed record AlliancePreview(string? ErrorKey, string? Detail, IReadOnlyList<string> Members, double Coherence, double Loss, double ThresholdFactor,
    IReadOnlyList<AllianceMemberView> Views, double Cost)
{
    public bool AllAccept => ErrorKey == null && Views.Count > 0 && Views.All(v => v.Accepts);
}

public sealed record AllianceFormed(int Turn, IReadOnlyList<string> Members, bool ByPlayer) : PoliticalEvent(Turn);
public sealed record AllianceBroken(int Turn, IReadOnlyList<string> Members) : PoliticalEvent(Turn);

public static class AllianceMath
{
    public static string ListId(IEnumerable<string> members) => "list:" + string.Join("+", members.OrderBy(m => m, StringComparer.Ordinal));

    /// <summary>The mean ideological distance between the members' programmes, 0..1.</summary>
    public static double MeanDistance(IReadOnlyList<PartyDef> members, IReadOnlyDictionary<string, double> ranges)
    {
        double sum = 0; int n = 0;
        for (int i = 0; i < members.Count; i++)
            for (int j = i + 1; j < members.Count; j++) { sum += PartyMath.Distance(members[i], members[j], ranges); n++; }
        return n == 0 ? 0.0 : sum / n;
    }

    /// <summary>
    /// The joint programme: every law any member stands for, at the support-weighted mean of the members' targets (weighted by their priorities too);
    /// the priority of a law is the support-weighted mean of the members' priorities (a member without the law counts as 0), so a law only one of
    /// the partners cares about is stressed less.
    /// </summary>
    public static IReadOnlyDictionary<string, PlatformPosition> MergePlatform(IReadOnlyList<PartyDef> members, IReadOnlyList<double> shares)
    {
        double total = shares.Sum();
        var result = new SortedDictionary<string, PlatformPosition>(StringComparer.Ordinal);
        foreach (var policy in members.SelectMany(m => m.Platform.Keys).Distinct(StringComparer.Ordinal).OrderBy(k => k, StringComparer.Ordinal))
        {
            double wsum = 0, target = 0, weight = 0;
            for (int i = 0; i < members.Count; i++)
            {
                if (!members[i].Platform.TryGetValue(policy, out var pos)) continue;
                double w = Math.Max(shares[i], 1e-9) * Math.Max(pos.Weight, 1e-9);
                wsum += w; target += w * pos.Target;
                weight += Math.Max(shares[i], 1e-9) * pos.Weight;
            }
            if (wsum > 0) result[policy] = new PlatformPosition(target / wsum, Math.Clamp(weight / Math.Max(total, 1e-9), 0.0, 1.0));
        }
        return result;
    }

    /// <summary>How far a party's own programme is from a joint one, 0..1 (the same measure as between two parties).</summary>
    public static double DistanceTo(PartyDef member, IReadOnlyDictionary<string, PlatformPosition> joint, IReadOnlyDictionary<string, double> ranges) =>
        PartyMath.Distance(member, member with { Platform = joint }, ranges);

    /// <summary>Splits a list's seats among its members in proportion to their votes (largest remainder, ties to the lower id).</summary>
    public static IReadOnlyDictionary<string, int> SplitSeats(int seats, IReadOnlyList<string> members, IReadOnlyDictionary<string, double> votes)
    {
        var result = members.ToDictionary(m => m, _ => 0, StringComparer.Ordinal);
        double total = members.Sum(m => votes.GetValueOrDefault(m));
        if (seats <= 0 || !(total > 0)) return result;
        var rest = new Dictionary<string, double>(StringComparer.Ordinal);
        foreach (var m in members)
        {
            double exact = seats * votes.GetValueOrDefault(m) / total;
            result[m] = (int)Math.Floor(exact + 1e-12);
            rest[m] = exact - result[m];
        }
        int left = seats - result.Values.Sum();
        foreach (var m in members.OrderByDescending(m => rest[m]).ThenBy(m => m, StringComparer.Ordinal))
        {
            if (left <= 0) break;
            result[m]++; left--;
        }
        return result;
    }
}
