// Hugo JavaScript SDK quick start (mirrors the README): store a secret, create an SSH resource, bind it to a
// profile, read the compiled instructions, and dry-run a tool call. Cleans up after itself.
// Usage: npm run build && node examples/quickstart.mjs
// Environment: HUGO_SDK_SERVER (default http://127.0.0.1:8800), HUGO_EMAIL, HUGO_PASSWORD, HUGO_TENANT (default "default").
import { HugoClient, HugoError } from "../dist/esm/index.js";

const server = process.env.HUGO_SDK_SERVER || "http://127.0.0.1:8800";
const email = process.env.HUGO_EMAIL || "anderson@hugo.local";
const password = process.env.HUGO_PASSWORD || "password";
const tenantId = process.env.HUGO_TENANT || "default";
const suffix = Date.now().toString(36);

const hugo = new HugoClient(server);
console.log("Health:", (await hugo.health()).status);

const token = await hugo.login(email, password, tenantId);
if (token.mustChangePassword) {
  // Advisory: every route works, but this account still has its seeded or reset password.
  console.log("Consider changing this account's password: await hugo.changePassword(current, next).");
}
console.log("Signed in to tenant", hugo.tenantId, "as", (await hugo.tokenDetails()).principalName);

// 1. Store credential material in the vault. Password secrets use the field name "password".
const secret = await hugo.secrets.create({ name: `web-01-password-${suffix}`, kind: "Password", values: { password: "s3cret" } });

let resource;
let profile;
try {
  // 2. Create an SSH resource that uses the secret through its "password" slot.
  resource = await hugo.resources.create({
    name: `web-01-${suffix}`,
    connectorType: "ssh",
    environment: "prod",
    settings: { host: "web-01.example.com", port: 22, username: "ops", authMethod: "Password" },
    secretBindings: [{ slotName: "password", secretId: secret.id }],
  });

  // 3. Create a profile and bind the resource read-only.
  profile = await hugo.agentProfiles.create({ name: `ops-${suffix}` });
  await hugo.agentProfiles.bindings.create(profile.id, { resourceId: resource.id, alias: "web-01", accessMode: "ReadOnly" });

  // 4. What an MCP caller receives.
  const instructions = await hugo.agentProfiles.compiledInstructions(profile.id, "Mcp");
  console.log("Compiled instructions:\n" + instructions.text.slice(0, 400) + "...\n");

  // 5. Would this call be allowed? (Nothing is executed.)
  const outcome = await hugo.toolInvocations.dryRun({ profileId: profile.id, resource: "web-01", toolName: "ssh_exec", arguments: { command: "uptime" } });
  console.log(`Dry run decision: ${outcome.decision} (${outcome.effect}: ${outcome.effectReason})`);
} catch (e) {
  if (e instanceof HugoError) console.log(`Hugo error ${e.status} ${e.code}: ${e.serverMessage} (request ${e.requestId})`);
  else throw e;
} finally {
  if (profile) await hugo.agentProfiles.delete(profile.id);
  if (resource) await hugo.resources.delete(resource.id);
  await hugo.secrets.delete(secret.id);
  await hugo.logout();
}
