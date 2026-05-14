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
  .help()
  .parseSync();

async function main() {
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
    const envInfo = await authenticateAndFetchEnvironmentInfo(config);

    const metadata: ProfileMetadata = {
      schemaVersion: '1.0.0',
      capturedAt: new Date().toISOString(),
      sourceEnvironmentUrl: config.envUrl
    };

    console.log(`Connected to environment: ${envInfo.name}`);
    console.log(`Dual-write API version: ${envInfo.version}`);

    const profilePath = path.join(config.outputDir, 'dualwrite-profile.json');
    fs.writeFileSync(profilePath, JSON.stringify(metadata, null, 2));
    console.log(`Profile metadata written to ${profilePath}`);

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
