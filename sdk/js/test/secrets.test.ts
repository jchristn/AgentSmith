import { describe, expect, it } from "vitest";
import { HugoClient } from "../src/index.js";
import { ctx, expectHugoError, owner, unique } from "./helpers.js";

describe("secrets", () => {
  it("CRUD, values, and referrers", async () => {
    const client = owner();
    const secret = await client.secrets.create({ name: unique("db-pass"), kind: "AccessKeyPair", description: "js harness", values: { accessKeyId: "AKIAEXAMPLE", secretAccessKey: "wJalrXUtnFEMI" } });
    try {
      expect(secret.id.startsWith("sec_")).toBe(true);
      expect(secret.fields?.sort()).toEqual(["accessKeyId", "secretAccessKey"]);
      expect(JSON.stringify(secret)).not.toContain("wJalrXUtnFEMI");
      expect((await client.secrets.get(secret.id)).name).toBe(secret.name);
      expect((await client.secrets.list({ search: secret.name })).objects.map((s) => s.id)).toContain(secret.id);
      const updated = await client.secrets.update(secret.id, { name: secret.name, kind: secret.kind, description: "changed" });
      expect(updated.description).toBe("changed");
      const added = await client.secrets.setValues(secret.id, { values: { secretAccessKey: "rotated", sessionToken: "tok" } });
      expect(added.fields?.sort()).toEqual(["accessKeyId", "secretAccessKey", "sessionToken"]);
      const removed = await client.secrets.setValues(secret.id, { remove: ["sessionToken"] });
      expect(removed.fields?.sort()).toEqual(["accessKeyId", "secretAccessKey"]);
      await expectHugoError(client.secrets.setValues(secret.id, { remove: ["accessKeyId"] }), 400, "ValidationFailed");
      expect(await client.secrets.referrers(secret.id)).toEqual([]);
    } finally {
      await client.secrets.delete(secret.id);
    }
    await expectHugoError(client.secrets.get(secret.id), 404, "NotFound");
  });

  it("parse describes a kubeconfig without storing it", async () => {
    const client = owner();
    const kubeconfig = "apiVersion: v1\nkind: Config\nclusters:\n- name: c1\n  cluster:\n    server: https://127.0.0.1:6443\ncontexts:\n- name: ctx1\n  context:\n    cluster: c1\n    user: u1\n    namespace: shop\ncurrent-context: ctx1\nusers:\n- name: u1\n  user:\n    token: abc\n";
    const result = await client.secrets.parse({ content: kubeconfig, fileName: "config" });
    expect(result.kind).toBe("Kubeconfig");
    expect(result.currentContext).toBe("ctx1");
    expect((await client.secrets.list({ maxResults: 1000 })).objects.some((s) => s.kind === "Kubeconfig")).toBe(false);
  });

  it("reveal requires re-authentication, then returns values", async () => {
    const c = ctx();
    const client = new HugoClient(c.server, { autoRefresh: false });
    await client.login(c.ownerEmail, c.ownerPassword, c.tenantId);
    const secret = await client.secrets.create({ name: unique("reveal"), kind: "Password", values: { password: "hunter2-sdk" } });
    try {
      await expectHugoError(client.secrets.reveal(secret.id, "js harness check"), 403, "ReauthenticationRequired");
      const reauth = await client.reauthenticate(c.ownerPassword);
      expect(new Date(reauth.revealAllowedUntilUtc).getTime()).toBeGreaterThan(Date.now());
      const revealed = await client.secrets.reveal(secret.id, "js harness check");
      expect(revealed.values.password).toBe("hunter2-sdk");
      expect(revealed.secretId).toBe(secret.id);
      await expectHugoError(client.reauthenticate("wrong-password"), 401, "InvalidCredentials");
    } finally {
      await client.secrets.delete(secret.id);
      await client.logout();
    }
  });
});

describe("prompt templates", () => {
  it("CRUD, versions, restore, render, and clone", async () => {
    const client = owner();
    const template = await client.promptTemplates.create({ name: unique("tpl"), kind: "Fragment", content: "Hello {{input.name}}", description: "js harness" });
    let cloneId: string | undefined;
    try {
      expect(template.currentVersion).toBe(1);
      expect((await client.promptTemplates.get(template.id)).content).toBe("Hello {{input.name}}");
      const updated = await client.promptTemplates.update(template.id, { name: template.name, kind: "Fragment", content: "Hi {{input.name}}", changeNote: "v2" });
      expect(updated.currentVersion).toBe(2);
      const versions = await client.promptTemplates.versions(template.id);
      expect(versions.map((v) => v.version)).toEqual([2, 1]);
      expect((await client.promptTemplates.version(template.id, 1)).content).toBe("Hello {{input.name}}");
      const restored = await client.promptTemplates.restore(template.id, 1);
      expect(restored.content).toBe("Hello {{input.name}}");
      const rendered = await client.promptTemplates.render(template.id, { context: { input: { name: "Anderson" } } });
      expect(rendered.rendered).toContain("Anderson");
      const adHoc = await client.promptTemplates.renderAdHoc({ content: "Mr. {{input.who}}", context: { input: { who: "Anderson" } } });
      expect(adHoc.rendered).toBe("Mr. Anderson");
      const clone = await client.promptTemplates.clone(template.id, unique("tpl-clone"));
      cloneId = clone.id;
      expect(clone.content).toBe(restored.content);
      expect((await client.promptTemplates.list({ maxResults: 1000 })).objects.map((t) => t.id)).toContain(template.id);
    } finally {
      await client.promptTemplates.delete(template.id);
      if (cloneId) await client.promptTemplates.delete(cloneId);
    }
  });
});
