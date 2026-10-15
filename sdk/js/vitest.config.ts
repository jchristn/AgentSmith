import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: ["test/**/*.test.ts"],
    globalSetup: ["test/global-setup.ts"],
    // The harness shares one provisioned tenant; run files one at a time against the live server.
    fileParallelism: false,
    // Generous timeouts: harness clients wait out the authentication rate limit (Retry-After: 60) when a run signs in
    // more than Auth.RateLimitPerMinute times in a minute.
    testTimeout: 180000,
    hookTimeout: 180000,
    reporters: ["verbose"],
  },
});
