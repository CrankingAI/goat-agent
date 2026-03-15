using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Extensions;
using GoatCheck.Agent.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace GoatCheck.Evals;

public class GoatRankingStabilityEval
{
    private static readonly GoatRequest BaseRequest = new(
        new GoatMetadata(new List<ScoringWeight>
        {
            new("StatisticalAchievements", 0.30),
            new("PeerRecognition", 0.20),
            new("DominanceWindow", 0.20),
            new("HeadToHead", 0.15),
            new("CulturalImpact", 0.15)
        }),
        new CandidateRef("magic-32", "Magic Johnson"),
        "NBA players of the 1980s",
        new List<CandidateRef>
        {
            new("bird-33", "Larry Bird"),
            new("kareem-33", "Kareem Abdul-Jabbar"),
            new("isiah-11", "Isiah Thomas")
        });

    [Fact(Skip = "Requires live API")]
    public async Task Score_IsStable_AcrossRuns()
    {
        var result1 = await RunWorkflowAsync(BaseRequest);
        var result2 = await RunWorkflowAsync(BaseRequest);

        Assert.False(result1.IsDegraded || result2.IsDegraded,
            "One or both runs were degraded.");

        Assert.True(
            Math.Abs(result1.WeightedScore - result2.WeightedScore) <= 0.15,
            $"Score variance too high: {result1.WeightedScore:F2} vs {result2.WeightedScore:F2}");
    }

    private static async Task<PerCandidateEvaluation> RunWorkflowAsync(GoatRequest request)
    {
        var hostBuilder = Host.CreateApplicationBuilder();
        EvalHostExtensions.ConfigureEvalServices(hostBuilder);
        var host = hostBuilder.Build();

        await host.StartAsync();
        try
        {
            var factory = host.Services.GetRequiredService<GoatWorkflowFactory>();
            var workflow = factory.CreateWorkflow();

            PerCandidateEvaluation? result = null;
            await using var run = await InProcessExecution.RunStreamingAsync(workflow, request);
            await foreach (var evt in run.WatchStreamAsync())
            {
                if (evt is WorkflowOutputEvent output && output.Data is PerCandidateEvaluation evaluation)
                    result = evaluation;
            }

            return result ?? throw new InvalidOperationException("No output from workflow.");
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
