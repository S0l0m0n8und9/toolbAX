namespace FoToolbox.Core.Models;

public enum AuthMode
{
    ClientSecret = 0,
    Certificate = 1,
    BearerToken = 2,
    /// <summary>Delegated user sign-in via MSAL interactive browser flow; tokens renew silently from the MSAL cache.</summary>
    Interactive = 3
}
