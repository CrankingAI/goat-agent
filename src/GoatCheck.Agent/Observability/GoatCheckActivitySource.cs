using System.Diagnostics;

namespace GoatCheck.Agent.Observability;

public static class GoatCheckActivitySource
{
    public const string Name = "GoatCheck.Agent";
    public static readonly ActivitySource Source = new(Name, "1.0.0");
}
