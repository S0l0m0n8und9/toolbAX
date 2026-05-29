using System;
using System.Collections.Generic;

namespace FoToolbox.Core.DualWrite;

/// <summary>
/// Classifies a gateway request-status string into terminal/success flags so polling
/// loops know when to stop. The gateway returns either numeric state codes
/// (<c>"2"</c> = success, <c>"3"</c> = error per <c>DWSolutionEngine.checkSolutionApplied</c>)
/// or word states; this handles both, case-insensitively, and treats unknown states as
/// non-terminal so polling continues rather than declaring a false result.
/// </summary>
public static class DualWriteStatusInterpreter
{
    private static readonly HashSet<string> SuccessStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "2", "success", "succeeded", "completed", "complete", "done", "finished"
    };

    private static readonly HashSet<string> FailureStates = new(StringComparer.OrdinalIgnoreCase)
    {
        "3", "error", "failed", "failure", "faulted", "cancelled", "canceled", "aborted"
    };

    public static (bool IsTerminal, bool IsSuccess) Classify(string? state)
    {
        if (string.IsNullOrWhiteSpace(state))
        {
            return (false, false);
        }

        var trimmed = state.Trim();
        if (SuccessStates.Contains(trimmed))
        {
            return (true, true);
        }

        if (FailureStates.Contains(trimmed))
        {
            return (true, false);
        }

        return (false, false);
    }
}
