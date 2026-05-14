# Dual-write Profiler CLI

A command-line tool for analyzing Dynamics 365 Dual-write configurations, integration keys, and risk profiles.

## Setup

```bash
npm install
npm run build
```

## Usage

### Basic Profile Capture

```bash
node dist/index.js \
  --env-url https://orgXXXX.crm.dynamics.com \
  --tenant {tenant-id-or-domain}
```

### Specify Output Directory

```bash
node dist/index.js \
  --env-url https://orgXXXX.crm.dynamics.com \
  --tenant {tenant-id-or-domain} \
  --output-dir ./my-output
```

### Authentication

The CLI uses device-code authentication by default (interactive). No credentials are persisted to disk.

For scripted scenarios, provide a token:

```bash
node dist/index.js \
  --env-url https://orgXXXX.crm.dynamics.com \
  --tenant {tenant-id} \
  --auth-method token \
  --token {access-token}
```

## Arguments

- `--env-url` (required): Dynamics 365 environment URL
- `--tenant` (required): Azure AD tenant ID or domain
- `--output-dir` (optional): Output directory (defaults to `./dualwrite-output/<timestamp>/`)
- `--auth-method` (optional): `device-code` or `token` (default: `device-code`)
- `--token` (optional): Access token (only with `--auth-method token`)

## Output

- `dualwrite-profile.json`: Machine-readable profile with schema metadata
- `dualwrite-map-inventory.md`: Human-readable map table (added in later tasks)
- `dualwrite-integration-keys.md`: Integration key analysis (added in later tasks)

## Testing

```bash
npm run test:smoke
```

This validates argument parsing, authentication flow, and output artifact creation without modifying production environments.
