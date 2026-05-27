using System;

namespace FoToolbox.Host.ViewModels;

internal sealed class ConnectionTestedEventArgs : EventArgs
{
    public required string EnvironmentId { get; init; }
    public required ConnectionScope Scope { get; init; }
    public required bool Success { get; init; }
    public required DateTimeOffset TestedAt { get; init; }
    public string? Detail { get; init; }
}

internal enum ConnectionScope
{
    FinanceAndOperations,
    Dataverse,
}
