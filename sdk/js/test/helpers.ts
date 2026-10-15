import { expect, inject } from "vitest";
import { HugoClient, HugoClientOptions, HugoError } from "../src/index.js";
import { harnessClient, HarnessContext } from "./bootstrap.js";

export { randomPassword, randomSuffix } from "./bootstrap.js";

/** Shared harness context from global setup. */
export function ctx(): HarnessContext {
  return inject("hugo");
}

/** A new, unauthenticated client for the harness server that waits out the authentication rate limit. */
export function newClient(options: HugoClientOptions = {}): HugoClient {
  return harnessClient(ctx().server, options);
}

/** A client signed in as the provisioned tenant administrator. */
export function owner(): HugoClient {
  const c = ctx();
  const client = newClient({ autoRefresh: false });
  client.useToken(c.ownerToken, c.tenantId);
  return client;
}

/** A client signed in as the system administrator, targeting the provisioned tenant. */
export function admin(): HugoClient {
  const c = ctx();
  const client = newClient({ autoRefresh: false });
  client.useToken(c.adminToken, c.tenantId);
  return client;
}

/** A unique name with a prefix. */
export function unique(prefix: string): string {
  return `${prefix}-${Date.now().toString(36)}-${Math.random().toString(16).slice(2, 6)}`;
}

/** Await a promise expected to fail with HugoError and return the error. */
export async function expectHugoError(promise: Promise<unknown>, status: number, code?: string): Promise<HugoError> {
  try {
    await promise;
  } catch (e) {
    expect(e).toBeInstanceOf(HugoError);
    const err = e as HugoError;
    expect(err.status).toBe(status);
    if (code) expect(err.code).toBe(code);
    return err;
  }
  throw new Error(`expected HugoError ${status}${code ? " " + code : ""}, but the call succeeded`);
}
