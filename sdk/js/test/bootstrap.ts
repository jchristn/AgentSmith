/**
 * Harness bootstrap shared by the Vitest global setup and the examples: environment, admin sign-in (clearing a
 * first-login must-change-password flag), and tenant provisioning and cleanup.
 */
import { HugoClient, HugoError } from "../src/index.js";

/** Harness environment. */
export interface HarnessEnv {
  server: string;
  adminEmail: string;
  adminPassword: string;
  adminTenant: string;
}

/** Values handed from global setup to every test file. */
export interface HarnessContext extends HarnessEnv {
  tenantId: string;
  tenantName: string;
  ownerUserId: string;
  ownerEmail: string;
  ownerPassword: string;
  ownerToken: string;
  adminToken: string;
}

export function readEnv(): HarnessEnv {
  return {
    server: (process.env.HUGO_SDK_SERVER || "http://127.0.0.1:8800").replace(/\/+$/, ""),
    adminEmail: process.env.HUGO_SDK_ADMIN_EMAIL || "admin@hugo.local",
    adminPassword: process.env.HUGO_SDK_ADMIN_PASSWORD || "password",
    adminTenant: process.env.HUGO_SDK_ADMIN_TENANT || "default",
  };
}

export function randomSuffix(length = 4): string {
  return Math.random().toString(16).slice(2, 2 + length).padEnd(length, "0");
}

export function randomPassword(): string {
  return `Sdk-${randomSuffix(8)}-${randomSuffix(8)}!`;
}

function utcStamp(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getUTCFullYear()}${p(d.getUTCMonth() + 1)}${p(d.getUTCDate())}${p(d.getUTCHours())}${p(d.getUTCMinutes())}${p(d.getUTCSeconds())}`;
}

/**
 * Sign in as the administrator. When the account still has must-change-password set (first login on a fresh
 * server), change the password to a temporary value and back to the configured one on the same session, which
 * clears the flag and keeps reruns working.
 */
export async function adminLogin(env: HarnessEnv): Promise<HugoClient> {
  const admin = new HugoClient(env.server, { autoRefresh: false });
  const issued = await admin.login(env.adminEmail, env.adminPassword, env.adminTenant);
  if (issued.mustChangePassword) {
    const temporary = `${env.adminPassword}-sdk-tmp-${randomSuffix(6)}`;
    await admin.changePassword(env.adminPassword, temporary);
    await admin.changePassword(temporary, env.adminPassword);
  }
  return admin;
}

/** Provision a tenant and a tenant administrator, sign the owner in, and return the shared context. */
export async function provision(env: HarnessEnv): Promise<{ context: HarnessContext; admin: HugoClient }> {
  const admin = await adminLogin(env);
  const tenantName = `sdk-js-${utcStamp()}-${randomSuffix(4)}`;
  const tenant = await admin.tenants.create({ name: tenantName, description: "Hugo JavaScript SDK harness tenant (deleted at the end of the run)." });
  try {
    const ownerEmail = `owner@sdk-js-${randomSuffix(6)}.test`;
    const ownerPassword = randomPassword();
    admin.tenantId = tenant.id;
    const owner = await admin.users.create({ email: ownerEmail, password: ownerPassword, firstName: "Sdk", lastName: "Owner", isTenantAdmin: true, mustChangePassword: false });
    admin.tenantId = env.adminTenant;
    const ownerClient = new HugoClient(env.server, { autoRefresh: false });
    const issued = await ownerClient.login(ownerEmail, ownerPassword, tenant.id);
    return {
      admin,
      context: {
        ...env,
        tenantId: tenant.id,
        tenantName,
        ownerUserId: owner.id,
        ownerEmail,
        ownerPassword,
        ownerToken: issued.token,
        adminToken: admin.token ?? "",
      },
    };
  } catch (e) {
    await admin.tenants.delete(tenant.id).catch(() => undefined);
    throw e;
  }
}

/** Delete the provisioned tenant (ignores a tenant that is already gone). */
export async function cleanup(admin: HugoClient, tenantId: string): Promise<void> {
  try {
    await admin.tenants.delete(tenantId);
  } catch (e) {
    if (!(e instanceof HugoError && e.status === 404)) throw e;
  }
}
