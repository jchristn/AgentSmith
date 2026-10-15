import { existsSync } from "node:fs";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";
import { describe, expect, it } from "vitest";
import { ctx } from "./helpers.js";

const root = fileURLToPath(new URL("..", import.meta.url));
const built = existsSync(`${root}dist/esm/index.js`) && existsSync(`${root}dist/cjs/index.js`);

describe("package build", () => {
  it("ESM build loads and talks to the server", async (t) => {
    if (!built) return t.skip("run npm run build first");
    const esm = await import(`${root}dist/esm/index.js`);
    expect(esm.SDK_VERSION).toBe("0.1.0-alpha");
    const client = new esm.HugoClient(ctx().server);
    expect((await client.health()).status).toBe("healthy");
  });

  it("CJS build loads through require", (t) => {
    if (!built) return t.skip("run npm run build first");
    const require = createRequire(import.meta.url);
    const cjs = require(`${root}dist/cjs/index.js`);
    expect(typeof cjs.HugoClient).toBe("function");
    expect(cjs.default).toBe(cjs.HugoClient);
    expect(typeof cjs.HugoError).toBe("function");
  });
});
