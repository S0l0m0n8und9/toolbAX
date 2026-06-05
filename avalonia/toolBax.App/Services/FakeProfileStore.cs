using System.Collections.Generic;
using System.Linq;
using ToolBax.Core.Models;
using ToolBax.Core.Services;

namespace ToolBax.App.Services;

/// <summary>
/// In-memory <see cref="IProfileStore"/> seeded from the design prototype (data.js <c>ENVS</c>). Used
/// for design-mode + tests until the on-disk profile store is wired in.
/// </summary>
public sealed class FakeProfileStore : IProfileStore
{
    private readonly List<EnvProfile> _profiles;

    public FakeProfileStore(IEnumerable<EnvProfile>? profiles = null) =>
        _profiles = (profiles ?? Seed()).ToList();

    public string? ActiveId { get; set; } = "dev-usmf";

    public IReadOnlyList<EnvProfile> GetAll() => _profiles;

    public void Save(EnvProfile profile)
    {
        var index = _profiles.FindIndex(p => p.Id == profile.Id);
        if (index >= 0)
        {
            _profiles[index] = profile;
        }
        else
        {
            _profiles.Add(profile);
        }
    }

    public static IReadOnlyList<EnvProfile> Seed() => new[]
    {
        new EnvProfile("dev-usmf", "USMF Dev", "contoso-dev.operations.dynamics.com", "contoso.onmicrosoft.com", "USMF", "Tier 1", EnvStatus.Connected, 118),
        new EnvProfile("uat-eur", "EMEA UAT", "contoso-uat.operations.dynamics.com", "contoso.onmicrosoft.com", "DEMF", "Tier 2", EnvStatus.Connected, 184),
        new EnvProfile("prd-apac", "APAC Prod", "contoso.operations.dynamics.com", "contoso.onmicrosoft.com", "AUMF", "Prod", EnvStatus.TokenExpired),
        new EnvProfile("sbx-fin", "Finance Sbx", "contoso-fin.operations.dynamics.com", "contoso.onmicrosoft.com", "USMF", "Sandbox", EnvStatus.Disconnected),
    };
}
