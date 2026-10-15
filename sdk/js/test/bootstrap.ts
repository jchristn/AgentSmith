/**
 * Harness bootstrap shared by the Vitest global setup and the examples: environment, admin sign-in, and tenant
 * provisioning and cleanup.
 */
import { HugoClient, HugoClientOptions, HugoError } from "../src/index.js";

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

/**
 * Retries the harness clients make after a 429. The server's authentication rate limit (Auth.RateLimitPerMinute,
 * default 10 per source IP and per identity in fixed one-minute windows) answers 429 with Retry-After: 60, and a full
 * run signs in more than ten times, so every harness client waits and retries instead of needing a raised limit.
 */
export const HARNESS_RATE_LIMIT_RETRIES = 3;

/** A client for the harness server that waits out the authentication rate limit. */
export function harnessClient(server: string, options: HugoClientOptions = {}): HugoClient {
  return new HugoClient(server, { rateLimitRetries: HARNESS_RATE_LIMIT_RETRIES, ...options });
}

function utcStamp(): string {
  const d = new Date();
  const p = (n: number) => String(n).padStart(2, "0");
  return `${d.getUTCFullYear()}${p(d.getUTCMonth() + 1)}${p(d.getUTCDate())}${p(d.getUTCHours())}${p(d.getUTCMinutes())}${p(d.getUTCSeconds())}`;
}

/**
 * Sign in as the administrator. On a fresh server the seeded account reports mustChangePassword, which is advisory
 * (D-048): every route works, so the harness leaves the password as configured.
 */
export async function adminLogin(env: HarnessEnv): Promise<HugoClient> {
  const admin = harnessClient(env.server, { autoRefresh: false });
  await admin.login(env.adminEmail, env.adminPassword, env.adminTenant);
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
    const ownerClient = harnessClient(env.server, { autoRefresh: false });
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
