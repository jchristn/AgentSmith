import { defineConfig, devices } from '@playwright/test';

// Runs tests/live against a deployed dashboard. Set GARRISON_DASHBOARD_URL (default http://127.0.0.1:8080)
// and GARRISON_ADMIN_PASSWORD, and optionally GARRISON_ADMIN_EMAIL and GARRISON_TENANT_ID.
export default defineConfig({
  testDir: './tests/live',
  outputDir: './test-results/live',
  timeout: 60000,
  fullyParallel: false,
  workers: 1,
  reporter: [['list']],
  use: {
    baseURL: process.env.GARRISON_DASHBOARD_URL || 'http://127.0.0.1:8080',
    trace: 'retain-on-failure'
  },
  projects: [{ name: 'chromium', use: { ...devices['Desktop Chrome'] } }]
});
