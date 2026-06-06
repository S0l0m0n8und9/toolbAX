using System.Collections.Generic;
using ToolBax.Core.Models;

namespace ToolBax.Core.Services;

/// <summary>
/// Persistence seam for environment profiles. Profiles are loaded eagerly into memory (the real
/// implementation backs this with the on-disk profile store); the shell + Profiles screen read/write
/// through this interface so the view-models stay testable against a fake.
/// </summary>
public interface IProfileStore
{
    IReadOnlyList<EnvProfile> GetAll();

    /// <summary>Upserts a profile by <see cref="EnvProfile.Id"/>.</summary>
    void Save(EnvProfile profile);

    /// <summary>Removes the profile with the given id (no-op if absent).</summary>
    void Delete(string id);

    /// <summary>Id of the active profile, or null if none is active.</summary>
    string? ActiveId { get; set; }
}
