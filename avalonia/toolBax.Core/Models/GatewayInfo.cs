using System;

namespace ToolBax.Core.Models;

/// <summary>Snapshot of the gateway's delegated auth (mode + account + time to expiry).</summary>
public sealed record AuthSnapshot(string Mode, string Account, TimeSpan Expires);

/// <summary>Resolved Dual-Write Management gateway connection for the active environment.</summary>
public sealed record GatewayInfo(
    string Identifier,
    string Region,
    string Host,
    string Cid,
    string CName,
    string ClientId,
    AuthSnapshot Auth);
