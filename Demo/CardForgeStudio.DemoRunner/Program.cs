using CardForgeStudio.DemoRunner;
using TcgULtimateServer.Api.Models;

const int SuccessExitCode = 0;
const int MatchUnfinishedExitCode = 1;
const int PackageMissingExitCode = 2;
const int FailureExitCode = 3;

var options = DemoOptions.Parse(args);
if (options.ShowHelp)
{
    Console.WriteLine(DemoOptions.HelpText);
    return SuccessExitCode;
}

var reporter = new ConsoleReporter(options);
var repositoryRoot = RepositoryLocator.Find(AppContext.BaseDirectory);
var packagePath = options.PackagePathOverride
    ?? Path.Combine(repositoryRoot, "GamePackages", "astral-rift-demo-1.0.0.cardforge");
if (!File.Exists(packagePath))
{
    reporter.PrintMissingPackage(packagePath);
    return PackageMissingExitCode;
}

try
{
    using var runtime = DemoRuntime.Start(packagePath, options.KeepRuntimeStore);
    var script = DemoScript.Build();
    reporter.PrintBanner(runtime.Package, DemoScript.Players, script.Count);
    reporter.PrintStage("Package imported", Path.GetFileName(packagePath));

    var game = new GameState { RulePackId = DemoScript.RulePackId, Players = [.. DemoScript.Players] };
    var rulePack = runtime.Engine.Get(game.RulePackId);
    DemoScript.ArrangeOpeningDraws(rulePack.StartingDeck);
    runtime.Engine.Start(game);
    reporter.UseRulePack(rulePack);
    reporter.PrintStage("Rule pack activated", game.RulePackId);
    reporter.PrintStage("Opening hands set", "arranged so every scripted ability can fire");
    reporter.PrintStage("Match started", string.Join(" vs ", DemoScript.Players));

    var matchRunner = new DemoMatchRunner(runtime.Engine, game, reporter);
    matchRunner.Play(script);
    if (!game.Finished)
    {
        Console.Error.WriteLine("The scripted match did not reach a victory state.");
        return MatchUnfinishedExitCode;
    }

    reporter.PrintFinalReport(game, runtime.Package, matchRunner.DistinctActionsExecuted, options.ShowFullEventLog);
    if (runtime.RetainsStore) reporter.PrintRetainedStore(runtime.RuntimeRoot);
    return SuccessExitCode;
}
catch (Exception exception)
{
    reporter.PrintFailure(exception, options.ShowFullEventLog);
    return FailureExitCode;
}
