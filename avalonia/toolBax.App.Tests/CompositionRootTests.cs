using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Versioning;
using FoToolbox.Core.Profiles;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

/// <summary>
/// Composition-root coverage for <c>App.BuildServices</c> (#167). Every service seam has its own Core*
/// tests and its own Fake* for design mode, and the degraded fallbacks are covered — but nothing asserted
/// that a healthy Windows start actually reaches the Core* graph. A single `activeEnv =>` lambda swapped
/// for a fake in that method would leave the whole suite green while the shipped app quietly fabricated
/// rows for a live environment.
/// <para>
/// Runs against a throwaway app-data root (via <see cref="ProfilePaths.AppDataDirEnvVar"/>) so it never
/// touches the developer's real profile.db. No other test in this assembly resolves
/// <see cref="ProfilePaths"/>, so the process-wide override cannot cross-talk.
/// </para>
/// </summary>
public class CompositionRootTests
{
    // BuildServices is private by design (the composition root is not an API); reflect rather than widen it.
    private static object BuildServices()
    {
        var method = typeof(App).GetMethod("BuildServices", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.NotNull(method);
        var result = method!.Invoke(null, null);
        Assert.NotNull(result);
        return result!;
    }

    // BuildServices returns an 8-element tuple, which the runtime nests as ValueTuple<…7, ValueTuple<8th>>.
    private static object?[] Flatten(object tuple)
    {
        var items = new List<object?>();
        object? current = tuple;
        while (current is not null)
        {
            var type = current.GetType();
            for (var i = 1; i <= 7; i++)
            {
                var field = type.GetField($"Item{i}");
                if (field is null)
                {
                    break;
                }

                items.Add(field.GetValue(current));
            }

            current = type.GetField("Rest")?.GetValue(current);
        }

        return items.ToArray();
    }

    private static object Resolved(object? service)
    {
        Assert.NotNull(service);
        return service!;
    }

    // Each seam is handed to the shell as a factory over the active-environment accessor, so the concrete
    // type only exists once the factory runs.
    private static object Build(object? factory)
    {
        var del = Assert.IsAssignableFrom<Delegate>(factory);
        Func<EnvProfile?> activeEnv = () => null;   // nothing is resolved until a call is made
        var created = del.DynamicInvoke(activeEnv);
        Assert.NotNull(created);
        return created!;
    }

    // The healthy graph names Windows-only types (CoreAuthService wraps the DPAPI vault + MSAL). The
    // Assert.SkipUnless below is the runtime guard, but the platform analyser can't see through it, so the
    // method is annotated instead of littering the body with OperatingSystem.IsWindows() ternaries.
    [Fact]
    [SupportedOSPlatform("windows")]
    public void On_windows_the_composition_root_wires_the_real_core_services_with_no_fakes()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(),
            "The healthy graph needs the DPAPI secret vault + MSAL auth, which are Windows-only.");

        var root = Path.Combine(Path.GetTempPath(), $"toolbax-composition-{Guid.NewGuid():N}");
        var previousOverride = Environment.GetEnvironmentVariable(ProfilePaths.AppDataDirEnvVar);
        Environment.SetEnvironmentVariable(ProfilePaths.AppDataDirEnvVar, root);
        try
        {
            var items = Flatten(BuildServices());
            Assert.Equal(8, items.Length);

            var graph = new List<object>
            {
                Resolved(items[0]),                 // profiles
                Resolved(items[1]),                 // secrets
                Resolved(items[2]),                 // auth
                Build(items[3]),                    // OData client
                Build(items[4]),                    // metadata service
                Build(items[5]),                    // dual-write map reader
                Build(items[6]),                    // virtual-table reader
            };

            Assert.IsType<CoreProfileStore>(graph[0]);
            Assert.IsType<CoreSecretStore>(graph[1]);
            Assert.IsType<CoreAuthService>(graph[2]);
            Assert.IsType<CoreODataClient>(graph[3]);
            Assert.IsType<CoreMetadataService>(graph[4]);
            Assert.IsType<CoreDualWriteMapReader>(graph[5]);
            Assert.IsType<CoreVirtualTableReader>(graph[6]);

            // The blanket check: a fake anywhere in the graph is the bug this test exists for, including in
            // a seam added after it was written.
            foreach (var name in graph.Select(service => service.GetType().Name))
            {
                Assert.False(name.StartsWith("Fake", StringComparison.Ordinal),
                    $"The composition root wired a design-mode fake into a healthy start: {name}.");
            }

            // A healthy start reports no degradation, so the shell shows no "fabricated data" banner.
            Assert.Null(items[7]);
        }
        finally
        {
            Environment.SetEnvironmentVariable(ProfilePaths.AppDataDirEnvVar, previousOverride);
            TryDeleteDirectory(root);
        }
    }

    // The SQLite/vault handles are still open when the test ends; a locked file must not fail the test.
    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException)
        {
        }
        catch (UnauthorizedAccessException)
        {
        }
    }
}
