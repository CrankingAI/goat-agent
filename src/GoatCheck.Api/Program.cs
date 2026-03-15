using Microsoft.Extensions.Hosting;
using GoatCheck.Agent.Contracts;
using GoatCheck.Agent.Extensions;
using GoatCheck.Agent.Workflow;
using Microsoft.Agents.AI.Workflows;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.Services.AddGoatCheckAgent(builder.Configuration);

var app = builder.Build();

app.MapDefaultEndpoints();

// POST /goat-check — accepts GoatRequest (single candidate), returns PerCandidateEvaluation
app.MapPost("/goat-check", async (
    GoatRequest request,
    GoatWorkflowFactory factory,
    CancellationToken ct) =>
{
    var workflow = factory.CreateWorkflow();

    PerCandidateEvaluation? result = null;
    Exception? error = null;

    await using var run = await InProcessExecution.RunStreamingAsync(workflow, request);
    await foreach (var evt in run.WatchStreamAsync(ct))
    {
        switch (evt)
        {
            case WorkflowOutputEvent output when output.Data is PerCandidateEvaluation evaluation:
                result = evaluation;
                break;
            case WorkflowErrorEvent err:
                error = err.Exception;
                break;
        }
    }

    if (error is not null)
        return Results.Problem(error.Message, statusCode: 500);

    if (result is null)
        return Results.Problem("Workflow completed without output.", statusCode: 500);

    return Results.Ok(result);
});

app.Run();
