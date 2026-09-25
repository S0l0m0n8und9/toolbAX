import * as yargs from 'yargs';
import * as fs from 'fs';
import * as path from 'path';
import { validateConfig, ProfileMetadata } from './config';
import { authenticateAndFetchEnvironmentInfo } from './auth';

const argv = yargs
  .option('env-url', {
    describe: 'Dynamics 365 environment URL (required)',
    type: 'string',
    demandOption: true
  })
  .option('tenant', {
    describe: 'Azure AD tenant ID or domain (required)',
    type: 'string',
    demandOption: true
  })
  .option('output-dir', {
    describe: 'Output directory for profile artifacts',
    type: 'string'
  })
  .option('auth-method', {
    describe: 'Authentication method: device-code or token',
    type: 'string',
    default: 'device-code'
  })
  .option('token', {
    describe: 'Access token (only with --auth-method token)',
    type: 'string'
  })
  .option('skip-auth', {
    describe: 'Write an offline fixture without authentication or live data verification',
    type: 'boolean',
    default: false
  })
  .epilog('EXPERIMENTAL: emits capture metadata only. It does not inventory maps, analyse integration keys, or produce risk reports. --skip-auth is an offline fixture, not a connectivity check.')
  .help()
  .parseSync();

async function main() {
  console.error('EXPERIMENTAL: this CLI captures metadata only and is not a supported production profiler.');
  const config = validateConfig({
    envUrl: argv['env-url'],
    tenant: argv['tenant'],
    outputDir: argv['output-dir'],
    authMethod: (argv['auth-method'] as 'device-code' | 'token') || 'device-code',
    token: argv['token']
  });

  if (!config) {
    process.exit(1);
  }

  try {
    let envInfo: { name: string; version: string };

    if (argv['skip-auth']) {
      // An intentionally offline fixture: no authentication or environment/API capability is verified.
      envInfo = {
        name: 'offline fixture',
        version: 'not verified'
      };
    } else {
      envInfo = await authenticateAndFetchEnvironmentInfo(config);
    }

    const metadata: ProfileMetadata = {
      schemaVersion: '1.0.0',
      capturedAt: new Date().toISOString(),
      sourceEnvironmentUrl: config.envUrl
    };

    if (argv['skip-auth']) {
      console.log('Offline fixture written: authentication and environment data were not verified.');
    } else {
      console.log(`Environment response name: ${envInfo.name}`);
      console.log(`Reported version: ${envInfo.version} (server header or fallback; not a verified Dual-write API capability).`);
    }

    const profilePath = path.join(config.outputDir, 'dualwrite-profile.json');
    fs.writeFileSync(profilePath, JSON.stringify(metadata, null, 2));
    console.log(`Capture metadata written to ${profilePath}`);

    console.log(`Output directory: ${config.outputDir}`);
  } catch (error: unknown) {
    const message = error instanceof Error ? error.message : String(error);
    console.error(`Error: ${message}`);
    process.exit(1);
  }
}

main().catch((error) => {
  console.error('Unexpected error:', error);
  process.exit(1);
});
