import { DeviceCodeCredential, TokenCredential } from '@azure/identity';
import axios, { AxiosError } from 'axios';
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

    // Fetch environment info from Dual-write API
    const envInfo = await fetchDualWriteEnvironmentInfo(config.envUrl, token);
    return envInfo;
  } catch (error: unknown) {
    const message = error instanceof Error ? error.message : String(error);

    // Provide clear error message for common auth failures
    if (message.includes('AADSTS')) {
      throw new Error('Invalid credentials: Azure authentication failed');
    }
    if (message.includes('401') || message.includes('Unauthorized')) {
      throw new Error('Invalid credentials: Access denied');
    }
    if (message.includes('404')) {
      throw new Error('Invalid environment URL: Environment not found');
    }

    throw new Error(`Connection failed: ${message}`);
  }
}

async function fetchDualWriteEnvironmentInfo(envUrl: string, token: string): Promise<{ name: string; version: string }> {
  try {
    // Build API endpoint from environment URL
    const apiUrl = new URL('/api/data/v9.2/msdyn_dualwriteentitymaps', envUrl);

    const response = await axios.get(apiUrl.toString(), {
      headers: {
        'Authorization': `Bearer ${token}`,
        'OData-MaxVersions': '4.0',
        'OData-Version': '4.0',
        'Accept': 'application/json'
      },
      timeout: 10000
    });

    // Extract environment name and version from response
    const envName = extractEnvironmentName(envUrl);
    const version = response.headers['x-api-version'] || '1.0.0';

    return {
      name: envName,
      version: version
    };
  } catch (error: unknown) {
    const axiosError = error as AxiosError;

    if (axiosError.response?.status === 401) {
      throw new Error('Invalid credentials: Access denied');
    }
    if (axiosError.response?.status === 404) {
      throw new Error('Dual-write API not found or environment does not exist');
    }
    if (axiosError.code === 'ECONNREFUSED' || axiosError.code === 'ENOTFOUND') {
      throw new Error('Cannot reach environment: Network error');
    }

    const message = axiosError.message || String(error);
    throw new Error(`API connection failed: ${message}`);
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
