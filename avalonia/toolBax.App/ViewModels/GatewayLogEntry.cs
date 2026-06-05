namespace ToolBax.App.ViewModels;

public enum LogKind { Info, Ok, Warn, Err }

/// <summary>One line in the Operations "Gateway requests" log.</summary>
public sealed record GatewayLogEntry(string Text, string? Note, LogKind Kind);
