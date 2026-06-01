using DualWriteMapBrowserPlugin;
using System.IO;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class TestifyConfigurationStoreTests
{
    [Fact]
    public async Task SaveAndReload_PreservesPerMapSettings()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var config = await store.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);
            config.OmitCreateFields = new HashSet<string>(new[] { "FieldA", "fieldB" }, StringComparer.OrdinalIgnoreCase);
            config.PreferredCreateValues = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["NumberSequenceGroup"] = "STD",
                ["CurrencyCode"] = "USD"
            };
            config.CePollTimeoutSeconds = 12;
            config.AllowPartialEnumCoverage = true;

            await store.SaveAsync(config, CancellationToken.None);

            var reloadedStore = new TestifyConfigurationStore(path);
            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);

            Assert.Equal(new[] { "FieldA", "fieldB" }, reloaded.OmitCreateFields.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
            Assert.Equal(12, reloaded.CePollTimeoutSeconds);
            Assert.True(reloaded.AllowPartialEnumCoverage);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task Load_MigratesLegacyCePollTimeoutMinutes_ToClampedSeconds()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-legacy.json");

        try
        {
            // Pre-rename config: timeout persisted in minutes, with no CePollTimeoutSeconds field.
            var legacyJson = """
            {
              "Configurations": [
                { "EnvId": "env-1", "MapId": "map-3min", "CePollTimeoutMinutes": 3 },
                { "EnvId": "env-1", "MapId": "map-60min", "CePollTimeoutMinutes": 60 }
              ]
            }
            """;
            await File.WriteAllTextAsync(path, legacyJson);

            var store = new TestifyConfigurationStore(path);
            var threeMin = await store.GetOrCreateAsync("env-1", "map-3min", CancellationToken.None);
            var sixtyMin = await store.GetOrCreateAsync("env-1", "map-60min", CancellationToken.None);

            // 3 min -> 180 s (in range); 60 min -> 3600 s clamped to the 300 s maximum.
            Assert.Equal(180, threeMin.CePollTimeoutSeconds);
            Assert.Equal(300, sixtyMin.CePollTimeoutSeconds);
            // The user's setting is preserved, not silently reset to the 5 s default.
            Assert.NotEqual(5, threeMin.CePollTimeoutSeconds);

            // After save + reload the migrated seconds value is stable and the legacy key is gone.
            await store.SaveAsync(threeMin, CancellationToken.None);
            var reloaded = await new TestifyConfigurationStore(path).GetOrCreateAsync("env-1", "map-3min", CancellationToken.None);
            Assert.Equal(180, reloaded.CePollTimeoutSeconds);
            Assert.DoesNotContain("CePollTimeoutMinutes", await File.ReadAllTextAsync(path));
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void TextSerializer_FormatsAndParsesEditorValues()
    {
        var omitText = TestifySettingsTextSerializer.FormatLines(new HashSet<string>(new[] { "FieldA", "FieldB" }, StringComparer.OrdinalIgnoreCase));
        var preferredText = TestifySettingsTextSerializer.FormatKeyValueLines(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["NumberSequenceGroup"] = "STD",
            ["CurrencyCode"] = "USD"
        });

        Assert.Equal("FieldA\r\nFieldB", omitText);
        Assert.Equal("CurrencyCode=USD\r\nNumberSequenceGroup=STD", preferredText);

        var omit = TestifySettingsTextSerializer.ParseLines(" FieldA \r\n\r\nfieldB \r\n");
        var preferred = TestifySettingsTextSerializer.ParseKeyValueLines(" NumberSequenceGroup = STD \r\nCurrencyCode= USD \r\n");

        Assert.Equal(new[] { "FieldA", "fieldB" }, omit.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
        Assert.Equal("STD", preferred["NumberSequenceGroup"]);
        Assert.Equal("USD", preferred["CurrencyCode"]);
    }
}
