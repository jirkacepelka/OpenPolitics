namespace OpenPolitics.Core.Politics;

/// <summary>Where the country is in the political cycle. Extended with formation and caretaker phases in M2b.</summary>
public enum Phase { Governing }

/// <summary>Immutable political snapshot: latest poll, current seats, the government and the player's party.</summary>
public sealed record CountryPolitics(
    Phase Phase, int TurnsToElection,
    IReadOnlyDictionary<string, double> Support,
    IReadOnlyDictionary<string, int> Seats,
    IReadOnlyList<string> GovernmentParties,
    string? Leader, string PlayerParty);

public abstract record PoliticalEvent(int Turn);

/// <summary>An election took place: the votes (shares summing to 1) and the resulting seats.</summary>
public sealed record ElectionHeld(
    int Turn, IReadOnlyDictionary<string, double> Votes, IReadOnlyDictionary<string, int> Seats) : PoliticalEvent(Turn);
