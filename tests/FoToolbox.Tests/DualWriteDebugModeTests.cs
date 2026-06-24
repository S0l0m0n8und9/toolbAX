using FoToolbox.Core.DualWrite;
using Xunit;

namespace FoToolbox.Tests;

public class DualWriteDebugModeTests
{
    [Theory]
    [InlineData(true, "Yes")]
    [InlineData(false, "No")]
    public void BuildPatchBody_sets_IsDebugMode(bool enabled, string expected)
        => Assert.Equal($"{{\"IsDebugMode\":\"{expected}\"}}", DualWriteDebugMode.BuildPatchBody(enabled));

    [Theory]
    [InlineData("Yes", true)]
    [InlineData("yes", true)]
    [InlineData("true", true)]
    [InlineData("1", true)]
    [InlineData("No", false)]
    [InlineData("false", false)]
    [InlineData("0", false)]
    public void InterpretState_maps_known_values(string raw, bool expected)
        => Assert.True(DualWriteDebugMode.InterpretState(raw) == expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("maybe")]
    public void InterpretState_is_null_for_unknown(string? raw)
        => Assert.Null(DualWriteDebugMode.InterpretState(raw));

    [Fact]
    public void ReadFirstRecord_reads_id_and_state_from_a_value_array()
    {
        var rec = DualWriteDebugMode.ReadFirstRecord(
            "{\"value\":[{\"@odata.id\":\"https://e/data/X(1)\",\"IsDebugMode\":\"Yes\"}]}");

        Assert.NotNull(rec);
        Assert.Equal("https://e/data/X(1)", rec!.ODataId);
        Assert.True(rec.IsDebugMode);
    }

    [Fact]
    public void ReadFirstRecord_reads_a_single_object_with_a_bool_flag()
    {
        var rec = DualWriteDebugMode.ReadFirstRecord("{\"@odata.id\":\"u\",\"IsDebugMode\":false}");

        Assert.Equal("u", rec!.ODataId);
        Assert.False(rec.IsDebugMode);
    }

    [Fact]
    public void ReadFirstRecord_state_is_unknown_when_field_absent()
    {
        var rec = DualWriteDebugMode.ReadFirstRecord("{\"@odata.id\":\"u\"}");

        Assert.NotNull(rec);
        Assert.Null(rec!.IsDebugMode);
    }

    [Theory]
    [InlineData("{\"value\":[]}")]            // empty result set
    [InlineData("{\"IsDebugMode\":\"Yes\"}")] // no @odata.id to PATCH
    [InlineData("not json")]                   // malformed
    [InlineData(null)]
    public void ReadFirstRecord_is_null_when_no_usable_record(string? json)
        => Assert.Null(DualWriteDebugMode.ReadFirstRecord(json));
}
