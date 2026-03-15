using System.IO;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using GoatCheck.Agent.Observability;
using GoatCheck.Agent.Options;

namespace GoatCheck.Agent.Workflow;

public sealed class GoatWorkflowFactory(
    IOptions<GoatCheckOptions> options,
    IChatClient chatClient,
    GoatCheckMetrics metrics,
    ILoggerFactory loggerFactory)
{
    public Microsoft.Agents.AI.Workflows.Workflow CreateWorkflow()
    {
        var opts = options.Value;

        // Tool executors
        var eraLookup = new EraContextLookupExecutor(loggerFactory.CreateLogger<EraContextLookupExecutor>());
        var candidateDispatch = new CandidateDispatchExecutor();
        var scoreRollup = new PerCandidateScoreRollupExecutor(
            opts, metrics, loggerFactory.CreateLogger<PerCandidateScoreRollupExecutor>());
        var assembly = new PerCandidateAssemblyExecutor(metrics);

        // Agent executors (5 scorers)
        var statScorer = new StatisticalAchievementsScorerExecutor(
            chatClient, opts, metrics, loggerFactory.CreateLogger<StatisticalAchievementsScorerExecutor>());
        var peerScorer = new PeerRecognitionScorerExecutor(
            chatClient, opts, metrics, loggerFactory.CreateLogger<PeerRecognitionScorerExecutor>());
        var domScorer = new DominanceWindowScorerExecutor(
            chatClient, opts, metrics, loggerFactory.CreateLogger<DominanceWindowScorerExecutor>());
        var h2hScorer = new HeadToHeadScorerExecutor(
            chatClient, opts, metrics, loggerFactory.CreateLogger<HeadToHeadScorerExecutor>());
        var culturalScorer = new CulturalImpactScorerExecutor(
            chatClient, opts, metrics, loggerFactory.CreateLogger<CulturalImpactScorerExecutor>());

        // Agent executors (narrative + voice)
        var narrativeRollup = new PerCandidateNarrativeRollupExecutor(
            chatClient, opts, metrics, loggerFactory.CreateLogger<PerCandidateNarrativeRollupExecutor>());
        var hotTakeVoice = new HotTakeVoiceExecutor(
            chatClient, opts, metrics, loggerFactory.CreateLogger<HotTakeVoiceExecutor>());

        // Bindings
        ExecutorBinding eraLookupBinding = eraLookup;
        ExecutorBinding candidateDispatchBinding = candidateDispatch;
        ExecutorBinding statScorerBinding = statScorer;
        ExecutorBinding peerScorerBinding = peerScorer;
        ExecutorBinding domScorerBinding = domScorer;
        ExecutorBinding h2hScorerBinding = h2hScorer;
        ExecutorBinding culturalScorerBinding = culturalScorer;
        ExecutorBinding scoreRollupBinding = scoreRollup;
        ExecutorBinding narrativeRollupBinding = narrativeRollup;
        ExecutorBinding hotTakeVoiceBinding = hotTakeVoice;
        ExecutorBinding assemblyBinding = assembly;

        // Build DAG: era_lookup → dispatch → fan-out(5 scorers) → fan-in barrier → rollup → narrative → voice → assembly
        var builder = new WorkflowBuilder(eraLookupBinding);
        builder.AddEdge(eraLookupBinding, candidateDispatchBinding);
        ExecutorBinding[] scorerBindings = [
            statScorerBinding, peerScorerBinding, domScorerBinding,
            h2hScorerBinding, culturalScorerBinding];
        builder.AddFanOutEdge(candidateDispatchBinding, scorerBindings);
        builder.AddFanInBarrierEdge(scorerBindings, scoreRollupBinding);
        builder.AddEdge(scoreRollupBinding, narrativeRollupBinding);
        builder.AddEdge(narrativeRollupBinding, hotTakeVoiceBinding);
        builder.AddEdge(hotTakeVoiceBinding, assemblyBinding);
        builder.WithOutputFrom(assemblyBinding);
        builder.WithOpenTelemetry(
            cfg => cfg.EnableSensitiveData = opts.Observability.IncludeSensitivePayloads,
            GoatCheckActivitySource.Source);

        var workflow = builder.Build();

        // Emit up-to-date Mermaid diagram
        try
        {
            var mermaid = WorkflowVisualizer.ToMermaidString(workflow);
            var repoRoot = FindRepoRoot();
            if (repoRoot is not null)
                File.WriteAllText(Path.Combine(repoRoot, "mermaid.mmd"), mermaid);
        }
        catch (Exception ex)
        {
            loggerFactory.CreateLogger<GoatWorkflowFactory>()
                .LogWarning(ex, "Failed to write mermaid.mmd");
        }

        return workflow;
    }

    private static string? FindRepoRoot()
    {
        var dir = AppContext.BaseDirectory;
        while (dir is not null)
        {
            if (File.Exists(Path.Combine(dir, "GoatCheck.slnx")))
                return dir;
            dir = Directory.GetParent(dir)?.FullName;
        }
        return null;
    }
}
