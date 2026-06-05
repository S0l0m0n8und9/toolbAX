namespace ToolBax.App.Models;

/// <summary>Connection state of an environment, drives the status dot colour.</summary>
public enum EnvStatus
{
    Connected,
    TokenExpired,
    Disconnected,
}

/// <summary>
/// An F&amp;O environment the shell can switch between. Shell-level subset of the handoff's full
/// EnvProfile (the auth/gateway detail arrives with the Profiles screen + toolBax.Core service seams).
/// </summary>
public sealed record EnvProfile(string Id, string Name, string Legal, EnvStatus Status)
{
    /// <summary>Two-letter chip initials derived from the name (e.g. "Contoso USMF" → "CU").</summary>
    public string Initials
    {
        get
        {
            var parts = Name.Split(' ', System.StringSplitOptions.RemoveEmptyEntries);
            return parts.Length switch
            {
                0 => "?",
                1 => parts[0][..System.Math.Min(2, parts[0].Length)].ToUpperInvariant(),
                _ => $"{char.ToUpperInvariant(parts[0][0])}{char.ToUpperInvariant(parts[^1][0])}",
            };
        }
    }
}
