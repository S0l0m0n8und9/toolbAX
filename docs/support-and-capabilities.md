# Support and capabilities

## Runtime prerequisites

The product targets Windows 10 or Windows 11 (x64), using an edition/build listed by both [.NET 10 support guidance](https://github.com/dotnet/core/blob/main/release-notes/10.0/supported-os.md) and [WebView2 supported Windows versions](https://learn.microsoft.com/en-us/microsoft-edge/webview2/#supported-windows-versions). This is a vendor-qualified prerequisite, not a per-build toolbAX certification claim. The published `toolbAX-win-x64.zip` is self-contained: it bundles its .NET runtime. The SDK in [`global.json`](../global.json) is for building source, not normal release use.

WebView2 is compiled for Windows but its Runtime is separate. Confirm availability through [Microsoft's WebView2 distribution guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution); follow organisation policy for installation and security prompts. A bundled .NET runtime does not imply WebView2 Runtime availability.

Dual-write portal sign-in uses a dedicated toolbAX WebView2 profile under the current user's local app data. Before each sign-in, toolbAX clears that profile's token-bearing browser storage and disk cache so the configured environment completes a fresh exchange the app can verify. Normal sign-in preserves cookies for SSO; **Switch account** also clears cookies so Entra prompts again. This does not clear a normal Edge or Chrome profile. Closing the window cancels the attempt; an incomplete sign-in is never used as a partial connection. If browser preparation fails, close any other toolbAX sign-in window and retry; persistent failures usually require checking the WebView2 Runtime and access to the toolbAX local app-data folder. If the portal appears to finish but the sign-in window remains open and the connection is not confirmed, close the window and retry for the configured environment; do not reuse the partial attempt.

The current dual-write gateway policy covers Microsoft's commercial-cloud `gateway.prod.island.powerapps.com` family and the public Entra login hosts. Sovereign-cloud or other gateway families are not claimed as supported by this implementation. Validation was deterministic and offline: no live portal sign-in, tenant policy, gateway operability or real profile clear was exercised.

Releases are unsigned. The published SHA256 checks downloaded bytes against the published checksum; it is not a publisher signature. Follow organisation policy for download approval and SmartScreen.

## Profile connection tests

Test uses the displayed draft connection fields without saving or activating the profile. F&O, Dataverse and portal gateway results identify the captured environment and endpoint; editing or changing the selected profile invalidates older results. Each test has its own Cancel control. A successful probe confirms that endpoint at that time, not every tool, permission or business operation.

Client-secret tests use the stored credential only when the saved authentication settings still match the draft. Save changed authentication settings and explicitly Store a new secret before testing; Test neither stores nor silently uses a typed secret. Interactive and portal tests retain their normal sign-in/token-cache behavior.

## App capabilities

| Surface | Current App capability | Boundary |
|---|---|---|
| Profiles | Interactive and Client secret authentication | Legacy Certificate values are preserved as unsupported until explicitly replaced. Dual-write uses portal-only sign-in; unused legacy DI ROPC settings are preserved with explicit legacy-password clearing. |
| Query Builder | OData query composition and CSV export | Live outcome depends on environment permissions. |
| POST Builder | POST/PATCH/DELETE with confirmation | `If-Match: *` checks existence; a specific ETag requests a version check where supported. |
| Metadata | Entity, field, navigation, enum and key inspection | Live metadata depends on environment access. |
| Map Browser | Map bindings, counts, and field details | Counts can be capped, snapshot-derived, or not-comparable. |
| Operations | Lifecycle actions and project debug flags | Actions are live writes and require confirmation. |
| Compare | Map presence and reported version/state | Includes Unknown/Ambiguous; it is not row-count or configuration-parity certification. |
| Virtual Tables | Inspect F&O-backed virtual tables | The App does not generate virtual tables. |
| Profiler | [Experimental CLI](../profiler/README.md) | Not shipped in the supported desktop release. |

The visible catalog is defined in [`BuiltInToolCatalog.cs`](../avalonia/toolBax.App/Services/BuiltInToolCatalog.cs). App startup uses real composition on Windows; non-Windows or unavailable profile-store startup enters explicit degraded/fake mode ([`App.axaml.cs`](../avalonia/toolBax.App/App.axaml.cs)). This does not imply every Windows startup succeeds.

## Write outcomes and inspection

Cancelling or timing out after dispatch does not roll back a write: the server may already have applied it. POST Builder and Operations retain the captured environment, target and observed outcome. An HTTP acknowledgment is separate from completed processing (particularly HTTP 202), and debug changes show per-project progress so a partial batch is not mistaken for all-or-nothing success.

Use the explicit readback control to GET current state in the captured scope. Readback does not prove which request caused that state and does not erase an uncertain original outcome. The app does not automatically resend a write to resolve an uncertain outcome; a newly confirmed write is a separate attempt. A POST without a safe server-provided record locator requires manual inspection in Query Builder before deciding whether another write is needed.

Receipts are memory-only. They disappear when the tool is recreated or the app closes and are not a durable journal. These safeguards and offline tests do not establish live tenant acceptance.

## Core-only APIs

The following Core capabilities have no current App UI: saved queries/API requests, template switching, table refresh, link reset, and integration-key application. They remain implementation APIs, not advertised product screens. Profile persistence is in [`ProfileStore.cs`](../src/FoToolbox.Core/Profiles/ProfileStore.cs); gateway client behavior is in [`DualWriteGatewayClient.cs`](../src/FoToolbox.Core/DualWrite/DualWriteGatewayClient.cs).

For Core consumers, bearer handlers require an explicit validated gateway origin. Delegated refresh additionally requires the captured tenant/client/resource binding; legacy refresh sessions without that provenance must sign in again. The static/manual Core bearer API remains caller-supplied within the trusted gateway policy. Normal F&O/Dataverse MSAL behavior is unchanged, and toolbAX does not treat access-token JWT decoding as validation.

## Data, logs, and evidence limits

User data is usually under `%LocalAppData%\FoToolbox`; portable-base handling can place `profile.db` differently ([`ProfilePaths.cs`](../src/FoToolbox.Core/Profiles/ProfilePaths.cs)). Secrets use CurrentUser DPAPI ([`SecretVaultService.cs`](../src/FoToolbox.Core/Profiles/SecretVaultService.cs)), so moving a zip/database is not cross-user credential migration.

Logs omit tokens, request/response bodies, and headers, but may contain endpoint paths or business identifiers. Review/redact before sharing.

Deterministic CI and packaging evidence does not prove live authentication, portal behavior, tenant policy, permissions, or every feature. See the [hardening tracker](production-readiness/2026-09-25-hardening.md) for current delivery evidence.

Microsoft does not support the referenced [Dual-write automations](https://github.com/microsoft/Dual-write-automations) project; it is provided as-is and warns API changes may break it. This statement applies to that project, not all Dataverse APIs.

Two live unknowns remain in [issue #168](https://github.com/S0l0m0n8und9/toolbAX/issues/168): Resume `skipInitialSync` behavior and `reversedSourceFilter` format. They are not validated claims.

For security reporting see [`SECURITY.md`](../SECURITY.md); contribution guidance is in [`CONTRIBUTING.md`](../CONTRIBUTING.md).
