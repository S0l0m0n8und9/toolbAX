namespace FoToolbox.Core.Models;

public record ServicePrincipal(
    string Id,
    string EnvId,
    string ClientId,
    AuthMode AuthMode,
    string? SecretRef,
    string? CertThumbprint,
    AuthTarget Target = AuthTarget.Fo);
