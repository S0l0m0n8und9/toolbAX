using System;
using System.IO;

namespace FoToolbox.Core.Profiles;

public static class ProfilePaths
{
    /// <summary>
    /// When set, this directory is used as the FoToolbox app-data root instead of
    /// %LOCALAPPDATA%\FoToolbox. Intended for test isolation (E2E/integration), since
    /// <see cref="Environment.SpecialFolder.LocalApplicationData"/> is resolved by the
    /// Windows shell and ignores a process-scoped LOCALAPPDATA environment override.
    /// Production leaves this unset, preserving the existing %LOCALAPPDATA% behaviour.
    /// </summary>
    public const string AppDataDirEnvVar = "FOTOOLBOX_APPDATA_DIR";

    private static string? ResolveOverrideRoot()
    {
        var overrideRoot = Environment.GetEnvironmentVariable(AppDataDirEnvVar);
        return string.IsNullOrWhiteSpace(overrideRoot) ? null : overrideRoot;
    }

    public static string ResolveAppDataPath(string fileName)
    {
        var overrideRoot = ResolveOverrideRoot();
        if (overrideRoot is not null)
        {
            Directory.CreateDirectory(overrideRoot);
            return Path.Combine(overrideRoot, fileName);
        }

        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            return Path.Combine(AppContext.BaseDirectory, fileName);
        }

        var appDataDir = Path.Combine(localRoot, "FoToolbox");
        Directory.CreateDirectory(appDataDir);
        return Path.Combine(appDataDir, fileName);
    }

    /// <summary>
    /// Resolves the profile.db path. When <see cref="AppDataDirEnvVar"/> is set (test
    /// isolation only) the override root deliberately takes precedence over
    /// <paramref name="baseDir"/>, so isolated runs cannot be pulled back to a caller-supplied
    /// directory. Production never sets the override, so <paramref name="baseDir"/> behaves
    /// exactly as before; all in-repo callers use the no-arg form.
    /// </summary>
    public static string ResolveProfileDbPath(string? baseDir = null)
    {
        var overrideRoot = ResolveOverrideRoot();
        if (overrideRoot is not null)
        {
            Directory.CreateDirectory(overrideRoot);
            return Path.Combine(overrideRoot, "profile.db");
        }

        var actualBase = baseDir ?? AppContext.BaseDirectory;
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            return Path.Combine(actualBase, "profile.db");
        }

        var baseDb = Path.Combine(actualBase, "profile.db");
        if (actualBase.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase))
        {
            return baseDb;
        }

        var appDataDb = ResolveAppDataPath("profile.db");

        if (File.Exists(baseDb) && !File.Exists(appDataDb))
        {
            try
            {
                File.Copy(baseDb, appDataDb, overwrite: true);
            }
            catch
            {
                // Best-effort migration only.
            }
        }

        return appDataDb;
    }
}
