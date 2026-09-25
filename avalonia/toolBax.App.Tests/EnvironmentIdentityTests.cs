using System;
using ToolBax.App.Services;
using ToolBax.Core.Models;
using Xunit;

namespace ToolBax.App.Tests;

public sealed class EnvironmentIdentityTests
{
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
