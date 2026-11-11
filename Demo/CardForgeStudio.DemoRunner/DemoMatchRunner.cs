using System.Text.Json;
using TcgULtimateServer.Api.Models;
using TcgULtimateServer.Api.Services;

namespace CardForgeStudio.DemoRunner;

/// <summary>Plays a scripted match through the authoritative rules engine and reports every action.</summary>
public sealed class DemoMatchRunner
{
    private readonly RulesEngine engine;
    private readonly GameState game;
    private readonly ConsoleReporter reporter;
    private readonly SourceCardResolver sourceCardResolver;
    private readonly HashSet<string> executedActionIds = new(StringComparer.Ordinal);

    /// <summary>Creates a runner for one game.</summary>
    public DemoMatchRunner(RulesEngine engine, GameState game, ConsoleReporter reporter)
    {
        this.engine = engine;
        this.game = game;
        this.reporter = reporter;
        sourceCardResolver = new SourceCardResolver(engine.Get(game.RulePackId), game);
    }

    /// <summary>Number of different actions the script has performed so far.</summary>
    public int DistinctActionsExecuted => executedActionIds.Count;

    /// <summary>Plays each turn in order, stopping early once the game has finished.</summary>
    public void Play(IEnumerable<ScriptedTurn> turns)
    {
        foreach (var turn in turns)
        {
            if (game.Finished) return;
            PlayTurn(turn);
        }
    }

    private void PlayTurn(ScriptedTurn turn)
    {
        reporter.PrintTurnHeader(game, turn.Actor, turn.Opponent);
        foreach (var step in turn.Steps)
        {
            if (game.Finished) break;
            ExecuteStep(turn, step);
        }

        reporter.PrintTurnFooter(game, turn.Actor);
    }

    private void ExecuteStep(ScriptedTurn turn, ScriptedStep step)
    {
        var snapshotBefore = GameSnapshot.Capture(game);
        var phaseBefore = game.CurrentPhaseId;
        var sourceCard = sourceCardResolver.Resolve(turn.Actor, step.ActionId, step.SourceAbilityId);
        var request = new ExecuteGameActionRequest(
            step.ActionId,
            sourceCard?.InstanceId,
            step.TargetsOpponent ? turn.Opponent : null,
            Parameters: BuildParameters(step));

        engine.Execute(game, turn.Actor, request);
        executedActionIds.Add(step.ActionId);

        var snapshotAfter = GameSnapshot.Capture(game);
        var newEvents = game.Events.Skip(snapshotBefore.EventCount).ToList();
        reporter.PrintStep(phaseBefore, game.CurrentPhaseId, step.ActionId, sourceCard?.CardId, newEvents, snapshotAfter.DescribeChangesSince(snapshotBefore));
    }

    private static Dictionary<string, JsonElement>? BuildParameters(ScriptedStep step) =>
        step.RecordMode is null
            ? null
            : new Dictionary<string, JsonElement> { ["mode"] = JsonSerializer.SerializeToElement(step.RecordMode) };
}
