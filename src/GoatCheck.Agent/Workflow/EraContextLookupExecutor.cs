using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.Logging;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Observability;

namespace GoatCheck.Agent.Workflow;

internal sealed class EraContextLookupExecutor(ILogger<EraContextLookupExecutor> logger)
    : Executor<GoatRequest, ResolvedGoatContext>("era_context_lookup")
{
    public override ValueTask<ResolvedGoatContext> HandleAsync(
        GoatRequest msg,
        IWorkflowContext ctx,
        CancellationToken ct)
    {
        using var activity = GoatCheckActivitySource.Source.StartActivity("era_context.lookup");
        activity?.SetTag("agent.name", "era_context_lookup");
        activity?.SetTag("category", msg.Category);
        activity?.SetTag("candidate.id", msg.Candidate.CandidateId);
        activity?.SetTag("candidate.name", msg.Candidate.DisplayName);
        activity?.SetTag("peer.count", msg.PeerCandidates.Count);

        logger.LogInformation(
            "EraContextLookup: resolving context for candidate={Candidate} in category={Category}",
            msg.Candidate.DisplayName, msg.Category);

        return ValueTask.FromResult(new ResolvedGoatContext(
            msg.Metadata,
            msg.Candidate,
            msg.Category,
            msg.PeerCandidates));
    }
}
