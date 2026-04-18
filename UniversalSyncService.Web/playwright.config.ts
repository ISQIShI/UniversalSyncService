import { defineConfig } from '@playwright/test';

const baseUrl = process.env.PLAYWRIGHT_BASE_URL || 'http://127.0.0.1:7199';
const testTimeout = Number(process.env.PLAYWRIGHT_TEST_TIMEOUT_MS || 30000);
const webServerTimeout = Number(process.env.PLAYWRIGHT_WEBSERVER_TIMEOUT_MS || 45000);

export default defineConfig({
  testDir: './tests/e2e',
  timeout: testTimeout,
  expect: {
    timeout: 5000,
  },
  fullyParallel: false,
  use: {
    baseURL: baseUrl,
    channel: 'msedge',
    headless: true,
    ignoreHTTPSErrors: true,
    actionTimeout: 10000,
    navigationTimeout: 10000,
  },
  webServer: {
    command: 'pwsh -File ./run-playwright-host.ps1',
    url: `${baseUrl}/health`,
    reuseExistingServer: false,
    timeout: webServerTimeout,
  },
});
