/**
 * Server follow-ups: credential bypass opt-in, list filters (with 400 ValidationFailed on unknown enum values),
 * bindingCount, session principalName, the BearerToken secret kind, and secret updates that keep omitted fields.
 */
import { describe, expect, it } from "vitest";
import type { ResourceRequest } from "../src/index.js";
import { ctx, expectHugoError, newClient, owner, unique } from "./helpers.js";

function httpResource(name: string, environment = "test"): ResourceRequest {
  return { name, connectorType: "http", description: "Hugo JS SDK harness resource", environment, settings: { baseUrl: "http://127.0.0.1:9" } };
}

describe("credential bypass", () => {
  it("a credential without a role has no access; inheritOwnerBypass is administrator-only and audited", async () => {
    const c = ctx();
    const client = owner();
    const created = await client.credentials.createWithSecret({ name: unique("norole"), authMode: "Hybrid" });
    try {
      expect(created.credential.inheritOwnerBypass).toBe(false);
      const keyed = newClient();
      keyed.useAccessKey(created.accessKey, created.secretKey, c.tenantId);
      // No role assignments and no inherited bypass: nothing is permitted.
      await expectHugoError(keyed.secrets.list(), 403);

      // Without a role it cannot update credentials at all, so it cannot grant itself the bypass.
      await expectHugoError(keyed.credentials.update(created.credential.id, { inheritOwnerBypass: true }), 403);

      // The tenant administrator can; the owner is a tenant administrator, so the credential now has access.
      const updated = await client.credentials.update(created.credential.id, { inheritOwnerBypass: true });
      expect(updated.inheritOwnerBypass).toBe(true);
      expect((await keyed.secrets.list()).success).toBe(true);

      // Even carrying the tenant administrator bypass, a credential is not an interactive administrator and cannot
      // change the flag (on itself or any other credential).
      await expectHugoError(keyed.credentials.update(created.credential.id, { inheritOwnerBypass: false }), 403, "BypassRequiresAdministrator");

      const audit = await client.audit.list({ eventType: "CredentialBypassChanged", pageSize: 50 });
      expect(audit.objects.some((r) => r.resourceId === created.credential.id)).toBe(true);

      const cleared = await client.credentials.update(created.credential.id, { inheritOwnerBypass: false });
      expect(cleared.inheritOwnerBypass).toBe(false);
      await expectHugoError(keyed.secrets.list(), 403);
    } finally {
      await client.credentials.delete(created.credential.id);
    }
  });

  it("inheritOwnerBypass can be set at creation by a tenant administrator", async () => {
    const client = owner();
    const created = await client.credentials.createWithSecret({ name: unique("bypass"), authMode: "Hybrid", inheritOwnerBypass: true });
    try {
      expect(created.credential.inheritOwnerBypass).toBe(true);
      expect((await client.credentials.get(created.credential.id)).inheritOwnerBypass).toBe(true);
    } finally {
      await client.credentials.delete(created.credential.id);
    }
  });
});

describe("sessions", () => {
  it("principalName and the principalType, userId, and credentialId filters", async () => {
    const c = ctx();
    const client = owner();
    const created = await client.credentials.createWithSecret({ name: unique("sesscred"), authMode: "Hybrid", roleName: "Viewer" });
    try {
      const exchanged = newClient({ autoRefresh: false });
      const issued = await exchanged.loginWithAccessKey(created.accessKey, created.secretKey);
      const byCredential = await client.sessions.list({ credentialId: created.credential.id, maxResults: 100 });
      expect(byCredential.objects.map((s) => s.id)).toContain(issued.sessionId);
      expect(byCredential.objects.every((s) => s.credentialId === created.credential.id)).toBe(true);
      const credentialSession = byCredential.objects.find((s) => s.id === issued.sessionId)!;
      expect(credentialSession.principalType).toBe("Credential");
      expect(credentialSession.principalName).toBeTruthy();

      const users = await client.sessions.list({ principalType: "User", maxResults: 1000 });
      expect(users.objects.length).toBeGreaterThan(0);
      expect(users.objects.every((s) => s.principalType === "User")).toBe(true);
      expect(users.objects.some((s) => s.principalName === c.ownerEmail)).toBe(true);

      const byUser = await client.sessions.list({ userId: c.ownerUserId, maxResults: 1000 });
      expect(byUser.objects.length).toBeGreaterThan(0);
      expect(byUser.objects.every((s) => s.userId === c.ownerUserId)).toBe(true);

      const bogus = await expectHugoError(client.sessions.list({ principalType: "Robot" as any }), 400, "ValidationFailed");
      expect(bogus.serverMessage).toContain("User");
    } finally {
      await client.credentials.delete(created.credential.id);
    }
  });
});

describe("secrets", () => {
  it("BearerToken kind, kind filter, and updates that keep omitted fields", async () => {
    const client = owner();
    const bearer = await client.secrets.create({ name: unique("bearer"), kind: "BearerToken", description: "bearer", values: { token: "tok-123" } });
    const generic = await client.secrets.create({ name: unique("generic"), kind: "Generic", values: { value: "x" } });
    try {
      expect(bearer.kind).toBe("BearerToken");
      expect(bearer.fields).toEqual(["token"]);
      const page = await client.secrets.list({ kind: "BearerToken", maxResults: 1000 });
      expect(page.objects.map((s) => s.id)).toContain(bearer.id);
      expect(page.objects.every((s) => s.kind === "BearerToken")).toBe(true);
      await expectHugoError(client.secrets.list({ kind: "Bogus" as any }), 400, "ValidationFailed");

      const updated = await client.secrets.update(bearer.id, { description: "only the description" });
      expect(updated.description).toBe("only the description");
      expect(updated.name).toBe(bearer.name);
      expect(updated.kind).toBe("BearerToken");
    } finally {
      await client.secrets.delete(bearer.id);
      await client.secrets.delete(generic.id);
    }
  });
});

describe("resources and profiles", () => {
  it("environment and health filters, and bindingCount", async () => {
    const client = owner();
    const environment = unique("env");
    const tested = await client.resources.create(httpResource(unique("tested"), environment));
    const untested = await client.resources.create(httpResource(unique("untested"), environment));
    const profile = await client.agentProfiles.create({ name: unique("counted") });
    try {
      await client.resources.test(tested.id);
      const inEnv = await client.resources.list({ environment, maxResults: 1000 });
      expect(inEnv.objects.map((r) => r.id).sort()).toEqual([tested.id, untested.id].sort());
      const unhealthy = await client.resources.list({ environment, health: "Unhealthy" });
      expect(unhealthy.objects.map((r) => r.id)).toEqual([tested.id]);
      const unknown = await client.resources.list({ environment, health: "Unknown" });
      expect(unknown.objects.map((r) => r.id)).toEqual([untested.id]);
      await expectHugoError(client.resources.list({ health: "Sick" as any }), 400, "ValidationFailed");

      expect((await client.agentProfiles.get(profile.id)).bindingCount).toBe(0);
      await client.agentProfiles.bindings.create(profile.id, { resourceId: tested.id, alias: "a" });
      await client.agentProfiles.bindings.create(profile.id, { resourceId: untested.id, alias: "b" });
      expect((await client.agentProfiles.get(profile.id)).bindingCount).toBe(2);
      const listed = (await client.agentProfiles.list({ maxResults: 1000 })).objects.find((p) => p.id === profile.id);
      expect(listed?.bindingCount).toBe(2);
    } finally {
      await client.agentProfiles.delete(profile.id);
      await client.resources.delete(tested.id);
      await client.resources.delete(untested.id);
    }
  });
});

describe("approval and tool invocation filters", () => {
  it("effect, fromUtc, and toUtc", async () => {
    const client = owner();
    const resource = await client.resources.create(httpResource(unique("filters")));
    const profile = await client.agentProfiles.create({ name: unique("filters") });
    try {
      await client.agentProfiles.bindings.create(profile.id, { resourceId: resource.id, alias: "api", accessMode: "ReadWriteWithApproval" });
      const write = await client.toolInvocations.invoke({ profileId: profile.id, resource: "api", toolName: "http_request", arguments: { method: "POST", path: "/w" } });
      const destroy = await client.toolInvocations.invoke({ profileId: profile.id, resource: "api", toolName: "http_request", arguments: { method: "DELETE", path: "/d" } });
      expect(write.decision).toBe("ApprovalRequired");
      expect(destroy.decision).toBe("ApprovalRequired");

      const destructive = await client.approvals.list({ agentProfileId: profile.id, effect: "Destructive" });
      expect(destructive.objects.map((a) => a.id)).toEqual([destroy.approvalId]);
      const writes = await client.approvals.list({ agentProfileId: profile.id, effect: "Write" });
      expect(writes.objects.map((a) => a.id)).toEqual([write.approvalId]);
      const past = new Date(Date.now() - 3600_000);
      const future = new Date(Date.now() + 3600_000);
      expect((await client.approvals.list({ agentProfileId: profile.id, fromUtc: past, toUtc: future })).objects.length).toBe(2);
      expect((await client.approvals.list({ agentProfileId: profile.id, fromUtc: future })).objects.length).toBe(0);
      expect((await client.approvals.list({ agentProfileId: profile.id, toUtc: past })).objects.length).toBe(0);
      const bad = await expectHugoError(client.approvals.list({ effect: "Explosive" as any }), 400, "ValidationFailed");
      expect(bad.serverMessage).toContain("Destructive");

      const invDestructive = await client.toolInvocations.list({ agentProfileId: profile.id, effect: "Destructive" });
      expect(invDestructive.objects.map((i) => i.id)).toEqual([destroy.invocationId]);
      expect((await client.toolInvocations.list({ agentProfileId: profile.id, fromUtc: past, toUtc: future })).objects.length).toBe(2);
      expect((await client.toolInvocations.list({ agentProfileId: profile.id, fromUtc: future })).objects.length).toBe(0);
      expect((await client.toolInvocations.list({ agentProfileId: profile.id, since: future })).objects.length).toBe(0);
      expect((await client.toolInvocations.list({ agentProfileId: profile.id, toUtc: past })).objects.length).toBe(0);
      await expectHugoError(client.toolInvocations.list({ effect: "Explosive" as any }), 400, "ValidationFailed");

      await client.approvals.cancel(write.approvalId!);
      await client.approvals.cancel(destroy.approvalId!);
    } finally {
      await client.agentProfiles.delete(profile.id);
      await client.resources.delete(resource.id);
    }
  });
});
