import { describe, expect, it } from "vitest";
import { HugoClient } from "../src/index.js";
import { admin, ctx, expectHugoError, owner, randomPassword, unique } from "./helpers.js";

describe("tenants", () => {
  it("system administrator reads and updates the provisioned tenant", async () => {
    const c = ctx();
    const client = admin();
    const tenant = await client.tenants.get(c.tenantId);
    expect(tenant.name).toBe(c.tenantName);
    const updated = await client.tenants.update(c.tenantId, { name: c.tenantName, description: "updated by the js harness" });
    expect(updated.description).toBe("updated by the js harness");
    const page = await client.tenants.list({ maxResults: 1000 });
    expect(page.objects.some((t) => t.id === c.tenantId)).toBe(true);
  });

  it("system administrator creates, updates, and deletes a tenant", async () => {
    const client = admin();
    const tenant = await client.tenants.create({ name: unique("sdk-js-extra"), region: "us-test" });
    try {
      expect(tenant.id.startsWith("ten_")).toBe(true);
      expect((await client.tenants.get(tenant.id)).region).toBe("us-test");
    } finally {
      await client.tenants.delete(tenant.id);
    }
    await expectHugoError(client.tenants.get(tenant.id), 404);
  });

  it("tenant administrator sees only its own tenant", async () => {
    const page = await owner().tenants.list();
    expect(page.objects.every((t) => t.id === ctx().tenantId)).toBe(true);
  });
});

describe("users", () => {
  it("CRUD, me, update me, permissions, and administrator password reset", async () => {
    const c = ctx();
    const client = owner();
    const email = `${unique("user")}@sdk-js.test`;
    const password = randomPassword();
    const user = await client.users.create({ email, password, firstName: "Ada", lastName: "Lovelace" });
    try {
      expect(user.id.startsWith("usr_")).toBe(true);
      expect((await client.users.get(user.id)).email).toBe(email);
      const updated = await client.users.update(user.id, { firstName: "Augusta" });
      expect(updated.firstName).toBe("Augusta");
      const page = await client.users.list({ search: email });
      expect(page.objects.map((u) => u.id)).toContain(user.id);
      const all: string[] = [];
      for await (const u of client.users.all({ maxResults: 1 })) all.push(u.id);
      expect(all).toContain(user.id);

      const perms = await client.users.permissions(c.ownerUserId);
      expect(perms.isTenantAdmin).toBe(true);

      const me = await client.users.me();
      expect(me.email).toBe(c.ownerEmail);
      const renamed = await client.users.updateMe({ firstName: "Sdk", lastName: "Owner", preferredLocale: "en" });
      expect(renamed.preferredLocale).toBe("en");

      const reset = randomPassword();
      const afterReset = await client.users.resetPassword(user.id, reset, false);
      expect(afterReset.mustChangePassword).toBe(false);
      const login = new HugoClient(c.server, { autoRefresh: false });
      await login.login(email, reset, c.tenantId);
      expect((await login.users.me()).id).toBe(user.id);
    } finally {
      await client.users.delete(user.id);
    }
    await expectHugoError(client.users.get(user.id), 404);
  });
});

describe("credentials", () => {
  it("CRUD, rotate, and permissions", async () => {
    const client = owner();
    const created = await client.credentials.createWithSecret({ name: unique("cred"), authMode: "Hybrid", roleName: "Viewer" });
    try {
      const id = created.credential.id;
      expect(created.credential.accessKey).toBe(created.accessKey);
      expect(created.secretKey.endsWith(created.credential.secretKeyLast4 ?? "")).toBe(true);
      expect((await client.credentials.get(id)).name).toBe(created.credential.name);
      expect((await client.credentials.list()).objects.map((x) => x.id)).toContain(id);
      const updated = await client.credentials.update(id, { name: created.credential.name + "-renamed" });
      expect(updated.name.endsWith("-renamed")).toBe(true);
      const rotated = await client.credentials.rotate(id);
      expect(rotated.secretKey).not.toBe(created.secretKey);
      const perms = await client.credentials.permissions(id);
      expect(perms.principalType).toBe("Credential");
    } finally {
      await client.credentials.delete(created.credential.id);
    }
  });
});

describe("sessions", () => {
  it("list, read, and revoke", async () => {
    const c = ctx();
    const extra = new HugoClient(c.server, { autoRefresh: false });
    const issued = await extra.login(c.ownerEmail, c.ownerPassword, c.tenantId);
    const client = owner();
    const page = await client.sessions.list({ maxResults: 1000 });
    expect(page.objects.map((s) => s.id)).toContain(issued.sessionId);
    const session = await client.sessions.get(issued.sessionId!);
    expect(session.principalType).toBe("User");
    await client.sessions.revoke(issued.sessionId!);
    await expectHugoError(extra.tokenDetails(), 401);
  });
});
