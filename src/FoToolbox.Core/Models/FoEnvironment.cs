namespace FoToolbox.Core.Models;

/// <summary>
/// Basic environment profile for connecting to a D365 F&O instance.
/// </summary>
public record FoEnvironment(
    string Id,
    string Name,
    string BaseUrl,
    string TenantId,
    string? DefaultCompany);
