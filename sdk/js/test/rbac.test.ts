import { describe, expect, it } from "vitest";
import { owner, randomPassword, unique } from "./helpers.js";

describe("roles and permissions", () => {
  it("roles CRUD and clone", async () => {
    const client = owner();
    const builtIns = await client.roles.list({ maxResults: 1000 });
    const viewer = builtIns.objects.find((r) => r.name === "Viewer");
    expect(viewer?.isBuiltIn).toBe(true);
    const role = await client.roles.create({ name: unique("role"), description: "js harness role" });
    const clone = await client.roles.clone(viewer!.id, { name: unique("viewer-copy") });
    try {
      expect(role.isBuiltIn).toBe(false);
      expect((await client.roles.get(role.id)).name).toBe(role.name);
      const updated = await client.roles.update(role.id, { name: role.name, description: "changed" });
      expect(updated.description).toBe("changed");
      expect(clone.isBuiltIn).toBe(false);
    } finally {
      await client.roles.delete(role.id);
      await client.roles.delete(clone.id);
    }
  });

  it("permissions CRUD", async () => {
    const client = owner();
    const permission = await client.permissions.create({ name: unique("perm"), permissionType: "Permit", resourceTypes: ["Secret"], operationTypes: ["Read"] });
    try {
      expect(permission.resourceTypes).toEqual(["Secret"]);
      expect((await client.permissions.get(permission.id)).name).toBe(permission.name);
      const updated = await client.permissions.update(permission.id, { name: permission.name, permissionType: "Permit", resourceTypes: ["Secret", "Audit"], operationTypes: ["Read"] });
      expect(updated.resourceTypes?.sort()).toEqual(["Audit", "Secret"]);
      expect((await client.permissions.list({ maxResults: 1000 })).objects.map((p) => p.id)).toContain(permission.id);
    } finally {
      await client.permissions.delete(permission.id);
    }
  });

  it("role-permission maps", async () => {
    const client = owner();
    const role = await client.roles.create({ name: unique("maprole") });
    const permission = await client.permissions.create({ name: unique("mapperm"), permissionType: "Permit", resourceTypes: ["Audit"], operationTypes: ["Read"] });
    try {
      const map = await client.rolePermissions.create(role.id, permission.id);
      expect(map.roleId).toBe(role.id);
      const page = await client.rolePermissions.list({ roleId: role.id });
      expect(page.objects.map((m) => m.id)).toContain(map.id);
      await client.rolePermissions.delete(map.id);
      expect((await client.rolePermissions.list({ roleId: role.id })).objects.map((m) => m.id)).not.toContain(map.id);
    } finally {
      await client.roles.delete(role.id);
      await client.permissions.delete(permission.id);
    }
  });

  it("user role assignments", async () => {
    const client = owner();
    const user = await client.users.create({ email: `${unique("ura")}@sdk-js.test`, password: randomPassword() });
    try {
      const assignment = await client.userRoleAssignments.create({ userId: user.id, roleName: "Auditor" });
      expect(assignment.roleName).toBe("Auditor");
      expect((await client.userRoleAssignments.get(assignment.id)).userId).toBe(user.id);
      expect((await client.userRoleAssignments.list({ userId: user.id })).objects.map((a) => a.id)).toContain(assignment.id);
      const updated = await client.userRoleAssignments.update(assignment.id, { userId: user.id, roleName: "Viewer" });
      expect(updated.roleName).toBe("Viewer");
      await client.userRoleAssignments.delete(assignment.id);
    } finally {
      await client.users.delete(user.id);
    }
  });

  it("credential scope assignments", async () => {
    const client = owner();
    const created = await client.credentials.createWithSecret({ name: unique("csa") });
    try {
      const assignment = await client.credentialScopeAssignments.create({ credentialId: created.credential.id, roleName: "Viewer" });
      expect(assignment.credentialId).toBe(created.credential.id);
      expect((await client.credentialScopeAssignments.get(assignment.id)).roleName).toBe("Viewer");
      expect((await client.credentialScopeAssignments.list({ credentialId: created.credential.id })).objects.map((a) => a.id)).toContain(assignment.id);
      await client.credentialScopeAssignments.delete(assignment.id);
    } finally {
      await client.credentials.delete(created.credential.id);
    }
  });
});
