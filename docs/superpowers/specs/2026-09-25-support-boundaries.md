# Support boundaries (H15)

Document the self-contained win-x64 release separately from the build SDK; WebView2 Runtime is a separate prerequisite. Describe product capabilities rather than certification, distinguish App UI from Core-only APIs, preserve unsigned/checksum evidence limits, and retain external-management compatibility limits.

## Pre-mortem

1. SDK mistaken for runtime: separate release prerequisites from developer commands.
2. Cross-platform UI mistaken for live cross-platform use: state degraded/fake boundary.
3. Core API mistaken for App feature: inventory Core-only APIs.
4. Smoke/checksum mistaken for certification/signature: state evidence limits.
5. External management API mistaken for stable support: cite its as-is compatibility disclaimer.

No workflow, signing, installer, or runtime installation change is proposed. Parent-observed final local validation passed both `CI=true` Release builds with 0 warnings/errors, App 1,188/1,188 and Core 432/432; source/link review only supports this copy boundary, not live backend claims.
