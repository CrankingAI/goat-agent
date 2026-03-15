using Xunit;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Extensions;
using GoatCheck.Agent.Workflow;
using Microsoft.Agents.AI.Workflows;

namespace GoatCheck.Evals;

public class GoatScoreAccuracyEval
{
    // Michael Jordan — expected to score very high among 1990s NBA players
    private static readonly GoatRequest MichaelJordanRequest = new(
        new GoatMetadata(new List<ScoringWeight>
        {
            new("StatisticalAchievements", 0.30),
            new("PeerRecognition", 0.20),
            new("DominanceWindow", 0.20),
            new("HeadToHead", 0.15),
            new("CulturalImpact", 0.15)
        }),
        new CandidateRef("mj-23", "Michael Jordan"),
        "NBA players of the 1990s",
        new List<CandidateRef>
        {
            new("shaq-32", "Shaquille O'Neal"),
            new("penny-1", "Anfernee Hardaway"),
            new("reggie-31", "Reggie Miller")
        });

    [Fact(Skip = "Requires live API")]
    public async Task MichaelJordan_ScoresAboveThresholdForNba90s()
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
            await using var run = await InProcessExecution.RunStreamingAsync(workflow, MichaelJordanRequest);
            await foreach (var evt in run.WatchStreamAsync())
            {
                if (evt is WorkflowOutputEvent output && output.Data is PerCandidateEvaluation evaluation)
                    result = evaluation;
            }

            Assert.NotNull(result);

            // Jordan should score >= 0.7 among 1990s NBA players
            Assert.True(result!.WeightedScore >= 0.7,
                $"Expected Jordan score >= 0.7, got {result.WeightedScore:F2}");

            Assert.NotEmpty(result.BestForSummary);
            Assert.NotEmpty(result.WatchOutForSummary);
        }
        finally
        {
            await host.StopAsync();
        }
    }
}
