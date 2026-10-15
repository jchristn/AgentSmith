import { defineConfig, devices } from '@playwright/test';

// Runs tests/live against a deployed dashboard. Set HUGO_DASHBOARD_URL (default http://127.0.0.1:8801, the compose
// port) and HUGO_ADMIN_PASSWORD, and optionally HUGO_ADMIN_EMAIL and HUGO_TENANT_ID.
export default defineConfig({
  testDir: './tests/live',
  outputDir: './test-results/live',
  timeout: 60000,
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.HUGO_DASHBOARD_URL || 'http://127.0.0.1:8801',
    trace: 'retain-on-failure'
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }]
});
