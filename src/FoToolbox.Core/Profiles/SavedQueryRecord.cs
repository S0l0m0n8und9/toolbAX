namespace FoToolbox.Core.Profiles;

public sealed record SavedQueryRecord(
    string Id,
    string EnvId,
    string Name,
    string SpecJson,
    bool CrossCompany,
    string CreatedUtc,
    string UpdatedUtc);
