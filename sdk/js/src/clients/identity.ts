import type {
  AssignmentRequest,
  AuthSession,
  Credential,
  CredentialCreateResponse,
  CredentialRequest,
  CredentialScopeAssignment,
  EffectivePermissions,
  EnumerationQuery,
  EnumerationResult,
  Permission,
  PermissionRequest,
  Role,
  RolePermissionMap,
  RoleRequest,
  SessionFilter,
  Tenant,
  TenantRequest,
  User,
  UserRequest,
  UserRoleAssignment,
} from "../models.js";
import { seg, Transport } from "../transport.js";
import { FlatCollection, TenantCollection, toQuery } from "./base.js";

/** Tenants (/v1.0/api/tenants). Listing returns the tenants visible to the caller; the rest need system administrator rights. */
export class TenantsClient extends FlatCollection<Tenant, TenantRequest> {
  constructor(t: Transport) {
    super(t, "/v1.0/api/tenants");
  }
}

/** Users in the client's tenant, plus the signed-in user's own routes. */
export class UsersClient extends TenantCollection<User, UserRequest> {
  constructor(t: Transport) {
    super(t, "users");
  }

  /** The signed-in user (GET /v1.0/api/users/me). */
  me(): Promise<User> {
    return this.t.request("GET", "/v1.0/api/users/me");
  }

  /** Update the signed-in user's name, locale, and default agent profile. */
  updateMe(body: Pick<UserRequest, "firstName" | "lastName" | "preferredLocale" | "defaultAgentProfileId">): Promise<User> {
    return this.t.request("PUT", "/v1.0/api/users/me", { body });
  }

  /** Effective permissions of a user. */
  permissions(id: string): Promise<EffectivePermissions> {
    return this.t.request("GET", `${this.item(id)}/permissions`);
  }

  /**
   * Administrator password reset. The user gets the advisory mustChangePassword flag unless mustChangePassword is
   * false; the flag blocks nothing.
   */
  resetPassword(id: string, newPassword: string, mustChangePassword?: boolean): Promise<User> {
    return this.t.request("POST", `${this.item(id)}/password`, {
      body: { newPassword },
      query: mustChangePassword === undefined ? undefined : { mustChangePassword },
    });
  }
}

/**
 * Credentials (access keys). Create and rotate return the secret key once. A credential's access comes from its role
 * assignments (pass roleName on create, or use credentialScopeAssignments); it does not inherit its owner's
 * administrator bypass unless a tenant or system administrator sets inheritOwnerBypass.
 */
export class CredentialsClient extends TenantCollection<Credential, CredentialRequest> {
  constructor(t: Transport) {
    super(t, "credentials");
  }

  /** Create a credential; the response carries the secret key once. */
  createWithSecret(body: CredentialRequest): Promise<CredentialCreateResponse> {
    return this.t.request("POST", this.base(), { body });
  }

  /** Rotate the secret key; the response carries the new secret key once. */
  rotate(id: string): Promise<CredentialCreateResponse> {
    return this.t.request("POST", `${this.item(id)}/rotate`);
  }

  /** Effective permissions of a credential. */
  permissions(id: string): Promise<EffectivePermissions> {
    return this.t.request("GET", `${this.item(id)}/permissions`);
  }
}

/** Roles, including built-in roles. */
export class RolesClient extends TenantCollection<Role, RoleRequest> {
  constructor(t: Transport) {
    super(t, "roles");
  }

  /** Clone a role (including built-ins) into a custom role. */
  clone(id: string, body: RoleRequest): Promise<Role> {
    return this.t.request("POST", `${this.item(id)}/clone`, { body });
  }
}

/** Permissions, including built-ins. */
export class PermissionsClient extends TenantCollection<Permission, PermissionRequest> {
  constructor(t: Transport) {
    super(t, "permissions");
  }
}

/** Role to permission mappings. */
export class RolePermissionsClient {
  constructor(private readonly t: Transport) {}

  private base(): string {
    return this.t.tenantPath("/role-permissions");
  }

  /** List mappings, optionally filtered by roleId. */
  list(query?: EnumerationQuery & { roleId?: string }): Promise<EnumerationResult<RolePermissionMap>> {
    return this.t.request("GET", this.base(), { query: toQuery(query) });
  }

  /** Map a permission to a custom role. */
  create(roleId: string, permissionId: string): Promise<RolePermissionMap> {
    return this.t.request("POST", this.base(), { body: { roleId, permissionId } });
  }

  /** Remove a mapping. */
  async delete(id: string): Promise<void> {
    await this.t.request("DELETE", `${this.base()}/${seg(id)}`);
  }
}

/** User role assignments (filter list with userId). */
export class UserRoleAssignmentsClient extends TenantCollection<UserRoleAssignment, AssignmentRequest> {
  constructor(t: Transport) {
    super(t, "user-role-assignments");
  }
}

/** Credential scope assignments (filter list with credentialId). */
export class CredentialScopeAssignmentsClient extends TenantCollection<CredentialScopeAssignment, AssignmentRequest> {
  constructor(t: Transport) {
    super(t, "credential-scope-assignments");
  }
}

/** Authentication sessions in the tenant. */
export class SessionsClient {
  constructor(private readonly t: Transport) {}

  private base(): string {
    return this.t.tenantPath("/sessions");
  }

  /** List sessions, optionally filtered by principalType, userId, and credentialId. Each carries principalName. */
  list(query?: SessionFilter): Promise<EnumerationResult<AuthSession>> {
    return this.t.request("GET", this.base(), { query: toQuery(query) });
  }

  get(id: string): Promise<AuthSession> {
    return this.t.request("GET", `${this.base()}/${seg(id)}`);
  }

  /** Revoke a session. */
  async revoke(id: string): Promise<void> {
    await this.t.request("DELETE", `${this.base()}/${seg(id)}`);
  }
}
