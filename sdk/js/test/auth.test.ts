import { describe, expect, it } from "vitest";
import { HugoClient, HugoError } from "../src/index.js";
import { ctx, expectHugoError, owner, randomPassword, unique, newClient } from "./helpers.js";

describe("health and info", () => {
  it("health is anonymous and healthy", async () => {
    const client = newClient();
    const health = await client.health();
    expect(health.status).toBe("healthy");
    expect(health.version).toBe("0.1.0-alpha");
    expect((await client.system.live()).status).toBeTruthy();
  });

  it("info describes the server", async () => {
    const info = await owner().info();
    expect(info.product).toBe("Hugo");
    expect(info.version).toBe("0.1.0-alpha");
    expect(Array.isArray(info.features)).toBe(true);
    const same = await owner().system.serverInfo();
    expect(same.version).toBe(info.version);
  });

  it("tenant lookup finds the provisioned tenant by email", async () => {
    const c = ctx();
    const tenants = await newClient().lookupTenants(c.ownerEmail);
    expect(tenants.map((t) => t.tenantId)).toContain(c.tenantId);
  });
});

describe("tokens", () => {
  it("login, validate, details, refresh, and logout", async () => {
    const c = ctx();
    const client = newClient();
    const issued = await client.login(c.ownerEmail, c.ownerPassword, c.tenantId);
    expect(issued.token).toBeTruthy();
    expect(issued.tenantId).toBe(c.tenantId);
    expect(client.tenantId).toBe(c.tenantId);
    expect((await client.validateToken()).valid).toBe(true);
    const details = await client.tokenDetails();
    expect(details.principalType).toBe("User");
    expect(details.email).toBe(c.ownerEmail);
    expect(details.isTenantAdmin).toBe(true);
    const refreshed = await client.refresh();
    expect(refreshed.token).toBeTruthy();
    expect(refreshed.token).not.toBe(issued.token);
    expect(client.token).toBe(refreshed.token);
    expect((await client.validateToken()).valid).toBe(true);
    const stale = newClient();
    stale.useToken(refreshed.token, c.tenantId);
    await client.logout();
    expect(client.token).toBeUndefined();
    await expectHugoError(stale.tokenDetails(), 401);
  });

  it("invalid password is a typed 401", async () => {
    const c = ctx();
    const err = await expectHugoError(newClient().login(c.ownerEmail, "definitely-wrong", c.tenantId), 401, "InvalidCredentials");
    expect(err.requestId).toBeTruthy();
  });

  it("auto refresh renews a token near expiry", async () => {
    const c = ctx();
    const client = newClient({ refreshSkewSeconds: 24 * 3600 });
    const issued = await client.login(c.ownerEmail, c.ownerPassword, c.tenantId);
    await client.users.me();
    expect(client.token).not.toBe(issued.token);
    await client.logout();
  });

  it("mustChangePassword is advisory: reported at login, routes still work, changePassword clears it", async () => {
    const c = ctx();
    const email = `${unique("mcp")}@sdk-js.test`;
    const initial = randomPassword();
    const user = await owner().users.create({ email, password: initial, firstName: "Must", lastName: "Change", mustChangePassword: true });
    try {
      await owner().userRoleAssignments.create({ userId: user.id, roleName: "Viewer" });
      const client = newClient({ autoRefresh: false });
      const issued = await client.login(email, initial, c.tenantId);
      expect(issued.mustChangePassword).toBe(true);
      expect((await client.tokenDetails()).mustChangePassword).toBe(true);
      // Nothing is blocked while the flag is set (D-048).
      expect((await client.users.me()).email).toBe(email);
      expect((await client.secrets.list()).success).toBe(true);
      const next = randomPassword();
      const changed = await client.changePassword(initial, next);
      expect(changed.mustChangePassword).toBe(false);
      expect((await client.users.me()).mustChangePassword).toBe(false);
      await expectHugoError(client.changePassword("wrong-current", randomPassword()), 400);
    } finally {
      await owner().users.delete(user.id);
    }
  });
});

describe("authentication rate limit", () => {
  it("answers 429 RateLimited with Retry-After, and rateLimitRetries waits it out", async () => {
    const c = ctx();
    // Spend this source IP's sign-in budget for the current minute with retries disabled.
    const raw = new HugoClient(c.server, { rateLimitRetries: 0 });
    let limited: HugoError | undefined;
    for (let i = 0; i < 30 && !limited; i++) {
      try {
        await raw.login(`${unique("nobody")}@sdk-js.test`, "wrong-password", c.tenantId);
      } catch (e) {
        if (e instanceof HugoError && e.status === 429) limited = e;
        else if (!(e instanceof HugoError && e.status === 401)) throw e;
      }
    }
    expect(limited).toBeDefined();
    expect(limited!.code).toBe("RateLimited");
    expect(Number(limited!.retryAfter)).toBeGreaterThan(0);
    // A retrying client waits for the window to pass and then signs in.
    const patient = newClient({ autoRefresh: false });
    const issued = await patient.login(c.ownerEmail, c.ownerPassword, c.tenantId);
    expect(issued.token).toBeTruthy();
    await patient.logout();
  }, 200000);
});

describe("credentials auth", () => {
  it("access key headers, session exchange, and signed requests", async () => {
    const c = ctx();
    const admin = owner();
    const hybrid = await admin.credentials.createWithSecret({ name: unique("hybrid"), authMode: "Hybrid", roleName: "Viewer" });
    const signedOnly = await admin.credentials.createWithSecret({ name: unique("signed"), authMode: "SignedRequest", roleName: "Viewer" });
    try {
      expect(hybrid.secretKey).toBeTruthy();

      const direct = newClient();
      direct.useAccessKey(hybrid.accessKey, hybrid.secretKey, c.tenantId);
      const details = await direct.tokenDetails();
      expect(details.principalType).toBe("Credential");
      expect(details.authScheme).toBe("AccessKeySecret");
      expect((await direct.secrets.list()).success).toBe(true);

      const exchanged = newClient();
      const token = await exchanged.loginWithAccessKey(hybrid.accessKey, hybrid.secretKey);
      expect(token.principalType).toBe("Credential");
      expect(exchanged.tenantId).toBe(c.tenantId);
      expect((await exchanged.secrets.list({ maxResults: 5 })).success).toBe(true);

      const signed = newClient();
      signed.useSignedRequests(signedOnly.accessKey, signedOnly.secretKey, c.tenantId);
      expect((await signed.tokenDetails()).authScheme).toBe("AccessKeySignature");
      const page = await signed.secrets.list({ maxResults: 3, ordering: "NameAscending", search: "a b" });
      expect(page.maxResults).toBe(3);

      // A signed-only credential cannot use direct headers or session exchange.
      const wrong = newClient();
      wrong.useAccessKey(signedOnly.accessKey, signedOnly.secretKey, c.tenantId);
      await expectHugoError(wrong.tokenDetails(), 401);
      await expectHugoError(newClient().loginWithAccessKey(signedOnly.accessKey, signedOnly.secretKey), 401, "InvalidCredentials");

      // A bad signature is rejected.
      const forged = newClient();
      forged.useSignedRequests(signedOnly.accessKey, "not-the-secret", c.tenantId);
      await expectHugoError(forged.tokenDetails(), 401);
    } finally {
      await admin.credentials.delete(hybrid.credential.id);
      await admin.credentials.delete(signedOnly.credential.id);
    }
  });

  it("administrator API key (when HUGO_SDK_ADMIN_API_KEY or HUGO_ADMIN_API_KEY is set)", async (t) => {
    const key = process.env.HUGO_SDK_ADMIN_API_KEY || process.env.HUGO_ADMIN_API_KEY;
    if (!key) t.skip("set HUGO_SDK_ADMIN_API_KEY to test the administrator API key scheme");
    const c = ctx();
    const client = newClient();
    client.useAdminApiKey(key!, c.tenantId);
    const details = await client.tokenDetails();
    expect(details.authScheme).toBe("AdminApiKey");
    expect((await client.users.list()).objects.some((u) => u.email === c.ownerEmail)).toBe(true);
  });
});
