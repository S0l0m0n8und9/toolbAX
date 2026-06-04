using System;
using Xunit;

namespace FoToolbox.E2eTests.Infrastructure;

/// <summary>
/// A Fact that is skipped when there is no interactive desktop session (UI Automation
/// cannot drive a real window headlessly). CI windows-latest is interactive, so it runs.
/// </summary>
public sealed class E2eFactAttribute : FactAttribute
{
    public E2eFactAttribute()
    {
        if (!Environment.UserInteractive)
        {
            Skip = "Requires an interactive desktop session (UI Automation).";
        }
    }
}
