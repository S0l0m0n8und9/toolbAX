using System;
using System.IO;

namespace FoToolbox.Core.Profiles;

public static class ProfilePaths
{
    public static string ResolveAppDataPath(string fileName)
    {
        var localRoot = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        if (string.IsNullOrWhiteSpace(localRoot))
        {
            return Path.Combine(AppContext.BaseDirectory, fileName);
        }

        var appDataDir = Path.Combine(localRoot, "FoToolbox");
        Directory.CreateDirectory(appDataDir);
        return Path.Combine(appDataDir, fileName);
    }

    public static string ResolveProfileDbPath(string? baseDir = null)
    {
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
