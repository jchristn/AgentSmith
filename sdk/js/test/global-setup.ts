import type { TestProject } from "vitest/node";
import { cleanup, HarnessContext, provision, readEnv } from "./bootstrap.js";

declare module "vitest" {
  export interface ProvidedContext {
    hugo: HarnessContext;
  }
}

/** Provision one tenant for the whole run and delete it afterwards. */
export default async function setup(project: TestProject): Promise<() => Promise<void>> {
  const env = readEnv();
  const { context, admin } = await provision(env);
  console.log(`[hugo-sdk-js] server ${env.server}, provisioned tenant ${context.tenantName} (${context.tenantId})`);
  project.provide("hugo", context);
  return async () => {
    await cleanup(admin, context.tenantId);
    console.log(`[hugo-sdk-js] deleted tenant ${context.tenantId}`);
  };
}
