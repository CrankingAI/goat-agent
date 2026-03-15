using Microsoft.Agents.AI.Workflows;
using GoatCheck.Agent.Contracts;

namespace GoatCheck.Agent.Workflow;

internal sealed class CandidateDispatchExecutor()
    : Executor<ResolvedGoatContext, CandidateEvaluationRequest>("candidate_dispatch")
{
    public override ValueTask<CandidateEvaluationRequest> HandleAsync(
        ResolvedGoatContext msg,
        IWorkflowContext ctx,
        CancellationToken ct)
        => ValueTask.FromResult(new CandidateEvaluationRequest(msg.Candidate, msg));
}
