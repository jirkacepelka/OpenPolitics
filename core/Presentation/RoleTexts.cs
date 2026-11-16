namespace OpenPolitics.Core.Presentation;

/// <summary>
/// Decision 0015 (5): which texts may address the player as the government. The convention: a text key is written for the leader or neutrally;
/// a variant with the suffix <c>_opposition</c> (or <c>_partner</c>) replaces it for that role (<see cref="Texts.RoleKey"/>). A key whose text talks
/// to the player as the government ("your partners", "your coalition", "gives you an ultimatum") must have an <c>_opposition</c> variant or be
/// listed here as shown only to a leader. A test checks every localization key against <see cref="Forbidden"/>.
/// </summary>
public static class RoleTexts
{
    /// <summary>Keys (or key prefixes ending in '.') that are only ever shown to the leader of the government.</summary>
    public static readonly IReadOnlyList<string> LeaderOnly = new[]
    {
        "ui.reshuffle.", "ui.client.reshuffle", "ui.err.reshuffle_cooldown", "ui.err.already_queued", "ui.ultimatum.", "ui.rail.ultimatums",
        "ui.exit.dismiss_ok", "ui.event.proposal_put_request", "ui.event.proposal_passed_request", "ui.event.proposal_failed_request",
        "ui.proposal.request", "ui.proposal.request_hint", "ui.client.propose_hint_leader", "ui.role.leader", "ui.role_changed.leader",
        "ui.start.role_hint.leader", "ui.brief.welcome_title", "ui.client.welcome_politics", "ui.client.early_election", "ui.formation.early_called",
        "ui.client.policies_hint",
        "ui.event.coalition_reshuffled", "ui.event.reshuffle_refused", "ui.event.partner_dismissed", "ui.explain.reshuffle",
        "ui.client.welcome", // the sandbox: no politics, the player always governs
    };

    /// <summary>Phrases that address the player as (the leader of) the government, per language.</summary>
    public static IReadOnlyList<string> Forbidden(string language) => language == "cs"
        ? new[] { "vaši partneři", "vašich partnerů", "vašim partnerům", "vaše koalice", "vaší koalice", "vaši koalici", "vaše vláda", "vaší vlády", "vaši vládu",
                  "vám dává ultimátum", "ultimátum vám", "vedete vládu", "vládnete zemi", "vaše požadavky splňte", "máte na to" }
        : new[] { "your partner", "your coalition", "your government", "gives you an ultimatum", "ultimatum to you", "deliver your", "you lead the government",
                  "you run the country", "keep your coalition", "your ministers", "you have {2} quarters" };

    /// <summary>Whether a key is only shown to the leader (listed exactly or under a listed prefix).</summary>
    public static bool IsLeaderOnly(string key) =>
        LeaderOnly.Any(p => p.EndsWith('.') ? key.StartsWith(p, StringComparison.Ordinal) : key == p || key.StartsWith(p + "_", StringComparison.Ordinal) && !key.EndsWith("_opposition", StringComparison.Ordinal) && !key.EndsWith("_partner", StringComparison.Ordinal));
}
