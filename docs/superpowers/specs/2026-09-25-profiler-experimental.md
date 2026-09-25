# Profiler CLI experimental boundary

## Scope

The `profiler/` CLI remains present as an experimental capture-metadata tool. This change adds no authentication, schema, dependency, CI, release, or workflow behavior.

Its real path requests a Graph `.default` token and calls Dataverse `GET /api/data/v9.2/msdyn_dualwriteentitymaps`. The only emitted JSON is `dualwrite-profile.json` with `schemaVersion`, `capturedAt`, and `sourceEnvironmentUrl`. Map inventory, integration-key analysis, and risk reports are not implemented. The displayed version is only a server header or fallback value, not proof of Dual-write API capability.

## Pre-mortem

1. Users mistake it for a supported complete profiler. Mitigation: experimental/not-production-ready labels in package metadata, CLI help/runtime, and both READMEs.
2. A skip-auth fixture appears live. Mitigation: fixture output says offline, authentication/data are unverified, and it never reports Connected or API-version success.
3. Documentation promises missing artifacts. Mitigation: name the exact three-property JSON and enumerate absent reports; do not treat a header/fallback version as a validated contract.
4. Validation calls a tenant. Mitigation: do not run `test:smoke` or `validate.ps1`; validate only build, `--help`, and `--skip-auth` with an illustrative reserved URL and temporary output.
5. Scope creeps into auth or a rewrite. Mitigation: no auth, JSON shape, dependency, lockfile, CI, workflow, or release edits; future capabilities remain unimplemented.
