using System;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class EnvironmentIdentityTests
{
    [Fact]
    public void Metadata_partition_has_a_stable_versioned_encoding()
    {
        // Independently calculated from the documented UTF-8 length framing, including field names.
        Assert.Equal("envmeta-v1:2aafe61bab4f568f9000f8f74ec5429ebd24a14254c7806f92dbe8c4871fb375",
            EnvironmentIdentity.Create(Profile()).ToMetadataCachePartition());
    }

    [Theory]
    [MemberData(nameof(ConnectionChanges))]
    public void Every_connection_identity_change_isolates_metadata(Func<EnvProfile, EnvProfile> change)
    {
        var before = Profile();
        Assert.NotEqual(EnvironmentIdentity.Create(before).ToMetadataCachePartition(),
            EnvironmentIdentity.Create(change(before)).ToMetadataCachePartition());
    }

    [Theory]
    [InlineData("profile")]
    [InlineData("company")]
    [InlineData("suffix")]
    public void Exact_identifier_and_URL_suffix_case_changes_isolate_metadata(string part)
    {
        var before = Profile() with { Url = "https://host.example/Path?Query=Value#Fragment" };
        var after = part switch
        {
            "profile" => before with { Id = "profile-a" },
            "company" => before with { Legal = "usmf" },
            _ => before with { Url = "https://host.example/path?query=value#fragment" },
        };
        Assert.NotEqual(EnvironmentIdentity.Create(before).ToMetadataCachePartition(),
            EnvironmentIdentity.Create(after).ToMetadataCachePartition());
    }

    [Fact]
    public void Opaque_field_separators_cannot_alias_another_partition()
    {
        var before = EnvironmentIdentity.Create(Profile()) with { ProfileId = "a|b", FoEndpoint = "c" };
        var after = before with { ProfileId = "a", FoEndpoint = "b|c" };
        Assert.NotEqual(before.ToMetadataCachePartition(), after.ToMetadataCachePartition());
    }
    private static EnvProfile Profile() => new(
        "Profile-A", "Environment", "https://HOST.operations.dynamics.com", "Tenant.OnMicrosoft.com",
        "USMF", "Tier 1", EnvStatus.Connected, 42,
        DataverseUrl: "https://ORG.crm.dynamics.com",
        ClientId: "FO-CLIENT", AuthMode: FoAuthMode.Interactive,
        DataverseClientId: "DV-CLIENT", DataverseAuthMode: FoAuthMode.Interactive)
    {
        DataIntegratorClientId = "di-client",
        DataIntegratorMode = DiAuthMode.Ropc,
        DualWriteGatewayUrl = "https://legacy-gateway.example",
    };

    public static TheoryData<Func<EnvProfile, EnvProfile>> ConnectionChanges => new()
    {
        p => p with { Url = "https://other.operations.dynamics.com" },
        p => p with { DataverseUrl = "https://other.crm.dynamics.com" },
        p => p with { Tenant = "other.onmicrosoft.com" },
        p => p with { ClientId = "fo-client-2" },
        p => p with { AuthMode = FoAuthMode.ClientSecret },
        p => p with { DataverseClientId = "dv-client-2" },
        p => p with { DataverseAuthMode = FoAuthMode.ClientSecret },
        p => p with { Legal = "DEMF" },
    };

    [Theory]
    [MemberData(nameof(ConnectionChanges))]
    public void Same_profile_id_with_changed_connection_data_is_a_different_identity(
        Func<EnvProfile, EnvProfile> change)
    {
        var before = Profile();
        Assert.NotEqual(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(change(before)));
    }

    [Fact]
    public void Cosmetic_profile_changes_keep_the_same_identity()
    {
        var before = Profile();
        var after = before with
        {
            Name = "Renamed",
            Status = EnvStatus.TokenExpired,
            LatencyMs = 999,
            Tier = "Production",
        };

        Assert.Equal(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(after));
        Assert.Equal(EnvironmentIdentity.Create(before).ToMetadataCachePartition(), EnvironmentIdentity.Create(after).ToMetadataCachePartition());
    }

    [Fact]
    public void Equivalent_endpoint_authority_spelling_keeps_the_same_identity()
    {
        var before = Profile();
        var after = before with
        {
            Url = "host.operations.dynamics.com/",
            DataverseUrl = "org.crm.dynamics.com/",
            Tenant = " tenant.onmicrosoft.com ",
            ClientId = "fo-client",
            DataverseClientId = "dv-client",
        };

        Assert.Equal(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(after));
        Assert.Equal(EnvironmentIdentity.Create(before).ToMetadataCachePartition(), EnvironmentIdentity.Create(after).ToMetadataCachePartition());
    }

    [Fact]
    public void Profile_id_case_remains_significant()
    {
        var before = Profile();
        Assert.NotEqual(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(before with { Id = "profile-a" }));
    }

    [Fact]
    public void Default_company_case_remains_significant()
    {
        var before = Profile();
        Assert.NotEqual(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(before with { Legal = "usmf" }));
    }

    [Fact]
    public void Url_path_query_and_fragment_case_remain_significant()
    {
        var before = Profile() with { Url = "https://host.example/Path?Query=Value#Fragment" };
        var after = before with { Url = "https://HOST.example/path?query=value#fragment" };

        Assert.NotEqual(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(after));
    }

    [Fact]
    public void URL_userinfo_case_is_preserved_like_the_shared_request_normalizer()
    {
        var before = Profile() with { Url = "HTTPS://User:Pass@HOST.example/Path" };
        var after = before with { Url = "https://user:pass@host.example/Path" };
        Assert.Equal("https://User:Pass@host.example/Path", EnvironmentIdentity.Create(before).FoEndpoint);
        Assert.NotEqual(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(after));
    }

    [Fact]
    public void Unused_legacy_data_integrator_fields_do_not_change_current_live_identity()
    {
        var before = Profile();
        var after = before with
        {
            DataIntegratorClientId = "other-di-client",
            DataIntegratorMode = DiAuthMode.Interactive,
            DualWriteGatewayUrl = "https://other-gateway.example",
        };

        Assert.Equal(EnvironmentIdentity.Create(before), EnvironmentIdentity.Create(after));
    }
}
