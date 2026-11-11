namespace CardForgeStudio.DemoRunner;

/// <summary>One action the scripted player performs, optionally sourced from a card carrying a specific ability.</summary>
public sealed record ScriptedStep(
    string ActionId,
    string? SourceAbilityId = null,
    bool TargetsOpponent = false,
    string? RecordMode = null);

/// <summary>The ordered steps one player performs during a single turn.</summary>
public sealed record ScriptedTurn(string Actor, string Opponent, IReadOnlyList<ScriptedStep> Steps);

/// <summary>The fixed five-turn Astral Rift match that fires every card ability and exercises every gameplay subsystem.</summary>
public static class DemoScript
{
    /// <summary>Rule pack the match is played under.</summary>
    public const string RulePackId = "astral-rift";

    private const string ArrivalAbility = "ar-ability-arrival";
    private const string ChannelAbility = "ar-ability-channel";
    private const string EchoAbility = "ar-ability-echo";
    private const string FlareAbility = "ar-ability-flare";
    private const string RallyAbility = "ar-ability-rally";
    private const string RecordAbility = "ar-ability-record";
    private const string SalvageAbility = "ar-ability-salvage";
    private const string VoidAbility = "ar-ability-void";

    /// <summary>The two scripted players, in turn order.</summary>
    public static readonly IReadOnlyList<string> Players = ["Nova", "Umbra"];

    /// <summary>
    /// Cards that must be in the opening hands, in draw order (the first entry is drawn first). The engine draws from the end
    /// of the deck list, so the match is the same every run no matter how the package orders its starting deck.
    /// </summary>
    public static readonly IReadOnlyList<string> OpeningDraws =
    [
        "ar-card-dawn-captain",
        "ar-card-whisper-agent",
        "ar-card-event-horizon",
        "ar-card-void-duelist",
        "ar-card-moss-memory",
        "ar-card-ember-fox",
    ];

    /// <summary>Moves the <see cref="OpeningDraws"/> cards to the draw end of the deck, leaving every other card where it was.</summary>
    public static void ArrangeOpeningDraws(IList<string> startingDeck)
    {
        foreach (var cardId in OpeningDraws.Reverse())
        {
            var index = startingDeck.IndexOf(cardId);
            if (index < 0) throw new InvalidOperationException($"The starting deck has no '{cardId}', which the scripted match needs in its opening draws.");
            startingDeck.RemoveAt(index);
            startingDeck.Add(cardId);
        }
    }

    /// <summary>Builds the complete turn list for the match.</summary>
    public static IReadOnlyList<ScriptedTurn> Build() =>
    [
        new ScriptedTurn("Nova", "Umbra",
        [
            new("ar-channel", ChannelAbility), new("ar-next-phase"),
            new("ar-draw"), new("ar-next-phase"),
            new("ar-deploy", ArrivalAbility),
            new("ar-stabilize"),
            new("ar-record", RecordAbility, RecordMode: "beacon"),
            new("ar-signal", EchoAbility), new("ar-next-phase"),
            new("ar-strike", FlareAbility, TargetsOpponent: true), new("ar-next-phase"),
            new("ar-end-turn"),
        ]),
        new ScriptedTurn("Umbra", "Nova",
        [
            new("ar-channel"), new("ar-next-phase"),
            new("ar-draw"), new("ar-next-phase"),
            new("ar-deploy", VoidAbility),
            new("ar-retire", VoidAbility),
            new("ar-banish", VoidAbility),
            new("ar-signal"), new("ar-next-phase"),
            new("ar-strike", TargetsOpponent: true),
            new("ar-rally", RallyAbility), new("ar-next-phase"),
            new("ar-end-turn"),
        ]),
        new ScriptedTurn("Nova", "Umbra",
        [
            new("ar-channel"), new("ar-next-phase"),
            new("ar-draw"), new("ar-next-phase"),
            new("ar-deploy", SalvageAbility),
            new("ar-retire", SalvageAbility),
            new("ar-salvage", SalvageAbility),
            new("ar-signal"), new("ar-next-phase"),
            new("ar-strike", FlareAbility, TargetsOpponent: true), new("ar-next-phase"),
            new("ar-end-turn"),
        ]),
        new ScriptedTurn("Umbra", "Nova",
        [
            new("ar-channel"), new("ar-next-phase"),
            new("ar-draw"), new("ar-next-phase"),
            new("ar-signal"), new("ar-next-phase"),
            new("ar-rally", RallyAbility), new("ar-next-phase"),
            new("ar-end-turn"),
        ]),
        new ScriptedTurn("Nova", "Umbra",
        [
            new("ar-channel"), new("ar-next-phase"),
            new("ar-draw"), new("ar-next-phase"),
            new("ar-signal"), new("ar-next-phase"),
            new("ar-strike", FlareAbility, TargetsOpponent: true),
        ]),
    ];
}
