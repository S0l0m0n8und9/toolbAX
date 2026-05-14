import { DeviceCodeCredential, TokenCredential } from '@azure/identity';
import { EnvironmentConfig } from './config';

export class DualWriteAuthenticator {
  private credential: TokenCredential;

  constructor(config: EnvironmentConfig) {
    if (config.authMethod === 'token' && config.token) {
      this.credential = {
        getToken: async () => ({
          token: config.token!,
          expiresOnTimestamp: Date.now() + 3600000
        })
      } as TokenCredential;
    } else {
      this.credential = new DeviceCodeCredential({
        tenantId: config.tenant,
        clientId: '04b07795-8ddb-461a-bbee-02f9e1bf7b46', // Azure CLI client ID
        userPromptCallback: (info) => {
          console.log('\n' + info.message);
        }
      });
    }
  }

  async getAccessToken(): Promise<string> {
    try {
      const token = await this.credential.getToken(['https://graph.microsoft.com/.default']);
      if (!token || !token.token) {
        throw new Error('Failed to acquire access token');
      }
      return token.token;
    } catch (error: unknown) {
      const message = error instanceof Error ? error.message : String(error);
      throw new Error(`Authentication failed: ${message}`);
    }
  }
}

export async function authenticateAndFetchEnvironmentInfo(
  config: EnvironmentConfig
): Promise<{ name: string; version: string }> {
  const auth = new DualWriteAuthenticator(config);

  try {
    const token = await auth.getAccessToken();
    if (!token) {
      throw new Error('Invalid credentials or token acquisition failed');
    }

    return {
      name: extractEnvironmentName(config.envUrl),
      version: '1.0.0' // Placeholder; will fetch from API in full implementation
    };
  } catch (error: unknown) {
    const message = error instanceof Error ? error.message : String(error);
    throw new Error(`Connection failed: ${message}`);
  }
}

function extractEnvironmentName(url: string): string {
  try {
    const urlObj = new URL(url);
    const hostname = urlObj.hostname;
    // Extract environment ID from patterns like "orgXXXX.crm.dynamics.com"
    const match = hostname.match(/^([^.]+)\.crm/);
    return match ? match[1] : hostname;
  } catch {
    return 'unknown';
  }
}
