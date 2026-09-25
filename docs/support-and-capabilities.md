# Support and capabilities

`toolbAX-win-x64.zip` is a self-contained Windows x64 release. The SDK in `global.json` is for building source, not running it. WebView2 is a separate prerequisite; use [Microsoft's guidance](https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/distribution) and organisation policy. A bundled .NET runtime does not ensure WebView2 Runtime.

Releases are unsigned. SHA256 compares downloaded bytes with the published checksum; it is not a publisher signature. Follow organisation security policy.

The App supports Profiles, OData query/CSV, confirmed POST/PATCH/DELETE with specific ETag where supported, metadata, map browser, operations/debug lifecycle, map presence/version/state Compare including Unknown/Ambiguous, and virtual-table inspection. It does not generate virtual tables. The profiler is experimental and not shipped.

Saved queries/API requests, template switching, table refresh, link reset, and integration-key application are Core-only APIs with no App UI.

Windows uses real services; non-Windows or unavailable profile-store startup is explicit degraded/fake mode. User data is usually under `%LocalAppData%\FoToolbox`; DPAPI is CurrentUser, so moving a database is not credential migration. Review/redact logs before sharing while H14 is pending.

CI/packaging does not prove tenant permissions, portal behavior, or live authentication. H04 improvements remain pending. [Dual-write automations](https://github.com/microsoft/Dual-write-automations) are provided as-is and may break with API changes. See [issue #168](https://github.com/S0l0m0n8und9/toolbAX/issues/168), [SECURITY.md](../SECURITY.md), and [CONTRIBUTING.md](../CONTRIBUTING.md).
