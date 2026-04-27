using System;

namespace FoToolbox.Core.Auth;

public sealed class AuthRecoveryException : InvalidOperationException
{
    public AuthRecoveryException(string serviceName, string reauthMessage, bool requiresInteractiveReauth = true, Exception? innerException = null)
        : base(reauthMessage, innerException)
    {
        ServiceName = serviceName;
        ReauthMessage = reauthMessage;
        RequiresInteractiveReauth = requiresInteractiveReauth;
    }

    public string PromptTitle => $"{ServiceName} sign-in required";

    public string ServiceName { get; }

    public string ReauthMessage { get; }

    public bool RequiresInteractiveReauth { get; }
}
