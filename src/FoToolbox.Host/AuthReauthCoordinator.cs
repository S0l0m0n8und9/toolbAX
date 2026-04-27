using FoToolbox.Core.Auth;
using System;

namespace FoToolbox.Host;

internal sealed class AuthReauthCoordinator
{
    private string? _lastPromptKey;

    public event Action<AuthRecoveryException>? ReauthRequired;

    public void Notify(AuthRecoveryException exception)
    {
        var promptKey = $"{exception.ServiceName}|{exception.ReauthMessage}";
        if (string.Equals(_lastPromptKey, promptKey, StringComparison.Ordinal))
        {
            return;
        }

        _lastPromptKey = promptKey;
        ReauthRequired?.Invoke(exception);
    }

    public void Reset()
    {
        _lastPromptKey = null;
    }
}
