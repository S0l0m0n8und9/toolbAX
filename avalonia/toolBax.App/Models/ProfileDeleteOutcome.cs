namespace ToolBax.App.Models;

/// <summary>Truthful result for an awaited profile delete plus optional replacement-activation warning.</summary>
public sealed record ProfileDeleteOutcome(bool Deleted, string? Warning = null);
