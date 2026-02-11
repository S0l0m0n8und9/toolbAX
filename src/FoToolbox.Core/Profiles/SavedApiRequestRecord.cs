namespace FoToolbox.Core.Profiles;

public sealed record SavedApiRequestRecord(
    string Id,
    string EnvId,
    string Name,
    string Method,
    string Url,
    string OpenCollectionJson,
    string CreatedUtc,
    string UpdatedUtc);

