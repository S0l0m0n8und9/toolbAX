using DualWriteMapBrowserPlugin;
using System.IO;

namespace FoToolbox.Tests;

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
            config.CePollTimeoutMinutes = 12;
            config.AllowPartialEnumCoverage = true;

            await store.SaveAsync(config, CancellationToken.None);

            var reloadedStore = new TestifyConfigurationStore(path);
            var reloaded = await reloadedStore.GetOrCreateAsync("env-1", "map-1", CancellationToken.None);

            Assert.Equal(new[] { "FieldA", "fieldB" }, reloaded.OmitCreateFields.OrderBy(v => v, StringComparer.OrdinalIgnoreCase));
            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
            Assert.Equal(12, reloaded.CePollTimeoutMinutes);
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
