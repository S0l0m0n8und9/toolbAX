using DualWriteMapBrowserPlugin;
using System.IO;

namespace FoToolbox.Tests;

[Trait("Category", "Testify")]
public sealed class TestifyConfigurationViewModelTests
{
    private static async Task WaitForLoadAsync()
    {
        await Task.Delay(100);
    }

    [Theory]
    [InlineData(4, false)]
    [InlineData(5, true)]
    [InlineData(300, true)]
    [InlineData(301, false)]
    public async Task SaveCommand_TimeoutValidation_RespectsRange(int timeoutSeconds, bool shouldBeEnabled)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var errorsCaptured = new List<Exception>();
            var onError = new Action<Exception>(ex => errorsCaptured.Add(ex));

            var vm = new TestifyConfigurationViewModel(store, "env-1", "map-1", onError);
            await WaitForLoadAsync();
            vm.CePollTimeoutSeconds = timeoutSeconds;

            Assert.Equal(shouldBeEnabled, vm.SaveCommand.CanExecute(null));
        }
        finally
        {
            await WaitForLoadAsync();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task SaveCommand_BoundaryValues_ProducesCorrectStates()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var vm = new TestifyConfigurationViewModel(store, "env-1", "map-1", _ => { });
            await WaitForLoadAsync();

            // Test 4 (invalid - below minimum)
            vm.CePollTimeoutSeconds = 4;
            Assert.False(vm.SaveCommand.CanExecute(null), "Timeout 4 should be invalid (below minimum 5)");

            // Test 5 (valid - at minimum)
            vm.CePollTimeoutSeconds = 5;
            Assert.True(vm.SaveCommand.CanExecute(null), "Timeout 5 should be valid (at minimum)");

            // Test 300 (valid - at maximum)
            vm.CePollTimeoutSeconds = 300;
            Assert.True(vm.SaveCommand.CanExecute(null), "Timeout 300 should be valid (at maximum)");

            // Test 301 (invalid - above maximum)
            vm.CePollTimeoutSeconds = 301;
            Assert.False(vm.SaveCommand.CanExecute(null), "Timeout 301 should be invalid (above maximum 300)");
        }
        finally
        {
            await WaitForLoadAsync();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task SaveCommand_InvalidTimeoutValues_AreDisabled(int invalidTimeout)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var vm = new TestifyConfigurationViewModel(store, "env-1", "map-1", _ => { });
            await WaitForLoadAsync();

            vm.CePollTimeoutSeconds = invalidTimeout;
            Assert.False(vm.SaveCommand.CanExecute(null), $"Timeout {invalidTimeout} should be invalid (below minimum 5)");
        }
        finally
        {
            await WaitForLoadAsync();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(301)]
    [InlineData(500)]
    [InlineData(1000)]
    public async Task SaveCommand_TimeoutAboveMaximum_AreDisabled(int invalidTimeout)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var vm = new TestifyConfigurationViewModel(store, "env-1", "map-1", _ => { });
            await WaitForLoadAsync();

            vm.CePollTimeoutSeconds = invalidTimeout;
            Assert.False(vm.SaveCommand.CanExecute(null), $"Timeout {invalidTimeout} should be invalid (above maximum 300)");
        }
        finally
        {
            await WaitForLoadAsync();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Theory]
    [InlineData(5)]
    [InlineData(50)]
    [InlineData(150)]
    [InlineData(300)]
    public async Task SaveCommand_ValidTimeoutValues_AreEnabled(int validTimeout)
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify.json");

        try
        {
            var store = new TestifyConfigurationStore(path);
            var vm = new TestifyConfigurationViewModel(store, "env-1", "map-1", _ => { });
            await WaitForLoadAsync();

            vm.CePollTimeoutSeconds = validTimeout;
            Assert.True(vm.SaveCommand.CanExecute(null), $"Timeout {validTimeout} should be valid (within 5-300 range)");
        }
        finally
        {
            await WaitForLoadAsync();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public async Task TestifyConfiguration_RoundTrip_FirstWriteThenOverwrite()
    {
        var path = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}-testify-roundtrip.json");

        try
        {
            // First write: no prior config exists. Set all four fields and save.
            var store = new TestifyConfigurationStore(path);
            var first = new TestifyConfigurationViewModel(store, "env-1", "map-rt", _ => { });
            await WaitForLoadAsync();

            first.OmitCreateFieldsText = "FieldA\nFieldB";
            first.PreferredCreateValuesText = "CurrencyCode=USD\nNumberSequenceGroup=STD";
            first.CePollTimeoutSeconds = 42;
            first.AllowPartialEnumCoverage = true;
            await first.SaveCommand.ExecuteAsync();

            // Reload via fresh view-model and assert all four values match.
            var reloaded = new TestifyConfigurationViewModel(store, "env-1", "map-rt", _ => { });
            await WaitForLoadAsync();

            Assert.Equal(new HashSet<string>(new[] { "FieldA", "FieldB" }, StringComparer.OrdinalIgnoreCase), reloaded.OmitCreateFields);
            Assert.Equal("USD", reloaded.PreferredCreateValues["CurrencyCode"]);
            Assert.Equal("STD", reloaded.PreferredCreateValues["NumberSequenceGroup"]);
            Assert.Equal(42, reloaded.CePollTimeoutSeconds);
            Assert.True(reloaded.AllowPartialEnumCoverage);

            // Overwrite scenario: replace every field with new values and save again.
            reloaded.OmitCreateFieldsText = "FieldC";
            reloaded.PreferredCreateValuesText = "Country=NZ";
            reloaded.CePollTimeoutSeconds = 17;
            reloaded.AllowPartialEnumCoverage = false;
            await reloaded.SaveCommand.ExecuteAsync();

            var afterOverwrite = new TestifyConfigurationViewModel(store, "env-1", "map-rt", _ => { });
            await WaitForLoadAsync();

            Assert.Equal(new HashSet<string>(new[] { "FieldC" }, StringComparer.OrdinalIgnoreCase), afterOverwrite.OmitCreateFields);
            Assert.Single(afterOverwrite.PreferredCreateValues);
            Assert.Equal("NZ", afterOverwrite.PreferredCreateValues["Country"]);
            Assert.Equal(17, afterOverwrite.CePollTimeoutSeconds);
            Assert.False(afterOverwrite.AllowPartialEnumCoverage);
        }
        finally
        {
            await WaitForLoadAsync();
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }
}
