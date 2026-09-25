using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
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

    Task SaveAsync(EnvProfile profile, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Save(profile);
        return Task.CompletedTask;
    }

    /// <summary>Removes the profile with the given id (no-op if absent).</summary>
    void Delete(string id);

    Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Delete(id);
        return Task.CompletedTask;
    }

    /// <summary>Id of the active profile, or null if none is active.</summary>
    string? ActiveId { get; set; }

    Task SetActiveAsync(string? id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        ActiveId = id;
        return Task.CompletedTask;
    }
}
