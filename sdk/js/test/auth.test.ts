import { describe, expect, it } from "vitest";
import { HugoClient } from "../src/index.js";
import { ctx, expectHugoError, owner, randomPassword, unique } from "./helpers.js";

describe("health and info", () => {
  it("health is anonymous and healthy", async () => {
    const client = new HugoClient(ctx().server);
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
    const tenants = await new HugoClient(c.server).lookupTenants(c.ownerEmail);
    expect(tenants.map((t) => t.tenantId)).toContain(c.tenantId);
  });
});

describe("tokens", () => {
  it("login, validate, details, refresh, and logout", async () => {
    const c = ctx();
    const client = new HugoClient(c.server);
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
    const stale = new HugoClient(c.server);
    stale.useToken(refreshed.token, c.tenantId);
    await client.logout();
    expect(client.token).toBeUndefined();
    await expectHugoError(stale.tokenDetails(), 401);
  });

  it("invalid password is a typed 401", async () => {
    const c = ctx();
    const err = await expectHugoError(new HugoClient(c.server).login(c.ownerEmail, "definitely-wrong", c.tenantId), 401, "InvalidCredentials");
    expect(err.requestId).toBeTruthy();
  });

  it("auto refresh renews a token near expiry", async () => {
    const c = ctx();
    const client = new HugoClient(c.server, { refreshSkewSeconds: 24 * 3600 });
    const issued = await client.login(c.ownerEmail, c.ownerPassword, c.tenantId);
    await client.users.me();
    expect(client.token).not.toBe(issued.token);
    await client.logout();
  });

  it("must-change-password blocks routes until the user changes it", async () => {
    const c = ctx();
    const email = `${unique("mcp")}@sdk-js.test`;
    const initial = randomPassword();
    const user = await owner().users.create({ email, password: initial, firstName: "Must", lastName: "Change", mustChangePassword: true });
    try {
      const client = new HugoClient(c.server, { autoRefresh: false });
      const issued = await client.login(email, initial, c.tenantId);
      expect(issued.mustChangePassword).toBe(true);
      const blocked = await expectHugoError(client.secrets.list(), 403, "PasswordChangeRequired");
      expect(blocked.serverMessage).toContain("password");
      const next = randomPassword();
      const changed = await client.changePassword(initial, next);
      expect(changed.mustChangePassword).toBe(false);
      expect((await client.users.me()).email).toBe(email);
      await expectHugoError(client.changePassword("wrong-current", randomPassword()), 400);
    } finally {
      await owner().users.delete(user.id);
    }
  });
});

describe("credentials auth", () => {
  it("access key headers, session exchange, and signed requests", async () => {
    const c = ctx();
    const admin = owner();
    const hybrid = await admin.credentials.createWithSecret({ name: unique("hybrid"), authMode: "Hybrid", roleName: "Viewer" });
    const signedOnly = await admin.credentials.createWithSecret({ name: unique("signed"), authMode: "SignedRequest", roleName: "Viewer" });
    try {
      expect(hybrid.secretKey).toBeTruthy();

      const direct = new HugoClient(c.server);
      direct.useAccessKey(hybrid.accessKey, hybrid.secretKey, c.tenantId);
      const details = await direct.tokenDetails();
      expect(details.principalType).toBe("Credential");
      expect(details.authScheme).toBe("AccessKeySecret");
      expect((await direct.secrets.list()).success).toBe(true);

      const exchanged = new HugoClient(c.server);
      const token = await exchanged.loginWithAccessKey(hybrid.accessKey, hybrid.secretKey);
      expect(token.principalType).toBe("Credential");
      expect(exchanged.tenantId).toBe(c.tenantId);
      expect((await exchanged.secrets.list({ maxResults: 5 })).success).toBe(true);

      const signed = new HugoClient(c.server);
      signed.useSignedRequests(signedOnly.accessKey, signedOnly.secretKey, c.tenantId);
      expect((await signed.tokenDetails()).authScheme).toBe("AccessKeySignature");
      const page = await signed.secrets.list({ maxResults: 3, ordering: "NameAscending", search: "a b" });
      expect(page.maxResults).toBe(3);

      // A signed-only credential cannot use direct headers or session exchange.
      const wrong = new HugoClient(c.server);
      wrong.useAccessKey(signedOnly.accessKey, signedOnly.secretKey, c.tenantId);
      await expectHugoError(wrong.tokenDetails(), 401);
      await expectHugoError(new HugoClient(c.server).loginWithAccessKey(signedOnly.accessKey, signedOnly.secretKey), 401, "InvalidCredentials");

      // A bad signature is rejected.
      const forged = new HugoClient(c.server);
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
    const client = new HugoClient(c.server);
    client.useAdminApiKey(key!, c.tenantId);
    const details = await client.tokenDetails();
    expect(details.authScheme).toBe("AdminApiKey");
    expect((await client.users.list()).objects.some((u) => u.email === c.ownerEmail)).toBe(true);
  });
});
