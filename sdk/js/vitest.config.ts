import { defineConfig } from "vitest/config";

export default defineConfig({
  test: {
    include: ["test/**/*.test.ts"],
    globalSetup: ["test/global-setup.ts"],
    // The harness shares one provisioned tenant; run files one at a time against the live server.
    fileParallelism: false,
    testTimeout: 30000,
    hookTimeout: 60000,
    reporters: ["verbose"],
  },
});
