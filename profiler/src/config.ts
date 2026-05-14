import * as fs from 'fs';
import * as path from 'path';

export interface EnvironmentConfig {
  envUrl: string;
  tenant: string;
  outputDir: string;
  authMethod: 'device-code' | 'token';
  token?: string;
}

export interface ProfileMetadata {
  schemaVersion: string;
  capturedAt: string;
  sourceEnvironmentUrl: string;
}

export function validateUrl(url: string): boolean {
  try {
    new URL(url);
    return url.toLowerCase().includes('crm') || url.toLowerCase().includes('dynamics');
  } catch {
    return false;
  }
}

export function getOutputDirectory(baseDir: string): string {
  if (!baseDir) {
    const timestamp = new Date().toISOString().replace(/[:.]/g, '-').slice(0, -5);
    baseDir = path.join(process.cwd(), 'dualwrite-output', timestamp);
  }

  if (!fs.existsSync(baseDir)) {
    fs.mkdirSync(baseDir, { recursive: true });
  }

  return baseDir;
}

export function validateConfig(config: Partial<EnvironmentConfig>): EnvironmentConfig | null {
  if (!config.envUrl) {
    console.error('Error: --env-url is required');
    return null;
  }

  if (!config.tenant) {
    console.error('Error: --tenant is required');
    return null;
  }

  if (!validateUrl(config.envUrl)) {
    console.error('Error: --env-url must be a valid Dynamics 365 URL');
    return null;
  }

  return {
    envUrl: config.envUrl,
    tenant: config.tenant,
    outputDir: getOutputDirectory(config.outputDir || ''),
    authMethod: config.authMethod || 'device-code',
    token: config.token
  };
}
