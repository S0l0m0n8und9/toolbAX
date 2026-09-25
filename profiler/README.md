# Dual-write Profiler CLI — Experimental

This is an experimental, not-production-ready capture-metadata CLI. It is not included in the desktop release or current CI/release workflows.

The real path in [`src/auth.ts`](src/auth.ts) requests Graph `.default` and then calls Dataverse `GET /api/data/v9.2/msdyn_dualwriteentitymaps`. The CLI currently writes only `dualwrite-profile.json`:

```json
{ "schemaVersion": "1.0.0", "capturedAt": "...", "sourceEnvironmentUrl": "..." }
```

It does not produce map inventory, integration-key, or risk reports. The displayed version is a server header or fallback and is not a verified Dual-write API capability.

## Offline illustration

Build, then use an illustrative reserved URL with `--skip-auth`; this writes an offline fixture only and makes no authentication or tenant call:

```bash
npm run build
node dist/index.js --env-url https://example.invalid --tenant example.invalid --skip-auth --output-dir ./tmp-offline
```

Do not use `npm run test:smoke` as functional acceptance: it may launch device authentication or contact a tenant and accepts authentication failures/timeouts. No live verification is authorised for this status change.

## Arguments

- `--env-url` and `--tenant` are required.
- `--output-dir` defaults to `./dualwrite-output/<timestamp>/`.
- `--auth-method` is `device-code` or `token`; `--token` supplies the token value.
- `--skip-auth` writes the offline fixture described above.

The current implementation is [`src/index.ts`](src/index.ts), [`src/auth.ts`](src/auth.ts), and [`src/config.ts`](src/config.ts). Future profiler capabilities remain unimplemented.
