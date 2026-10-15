// Hugo JavaScript SDK quick start (mirrors the README).
// Usage: npm run build && node examples/quickstart.mjs
// Environment: HUGO_SDK_SERVER (default http://127.0.0.1:8800), HUGO_EMAIL, HUGO_PASSWORD, HUGO_TENANT (default "default").
import { HugoClient, HugoError } from "../dist/esm/index.js";

const server = process.env.HUGO_SDK_SERVER || "http://127.0.0.1:8800";
const email = process.env.HUGO_EMAIL || "anderson@hugo.local";
const password = process.env.HUGO_PASSWORD || "password";
const tenantId = process.env.HUGO_TENANT || "default";

const hugo = new HugoClient(server);
console.log("health:", (await hugo.health()).status);

const token = await hugo.login(email, password, tenantId);
if (token.mustChangePassword) {
  console.log("This account must change its password first: hugo.changePassword(current, next).");
  process.exit(1);
}
console.log("signed in to tenant", hugo.tenantId, "as", (await hugo.tokenDetails()).principalName);

// Store a credential in the vault (values are write-only; reveal needs re-authentication).
const secret = await hugo.secrets.create({ name: `quickstart-${Date.now()}`, kind: "Password", values: { password: "s3cret" } });
console.log("secret", secret.id, "fields", secret.fields);

// Preview: create a resource, bind it to a profile, and dry-run a tool call (Phase 3, 5, and 6 routes).
try {
  const [connector] = await hugo.connectors.list();
  const resource = await hugo.resources.create({ name: `quickstart-${Date.now()}`, connectorType: connector.type, settings: {} });
  const profile = await hugo.agentProfiles.create({ name: `quickstart-${Date.now()}` });
  await hugo.agentProfiles.bindings.create(profile.id, { resourceId: resource.id, alias: "target", accessMode: "ReadOnly" });
  const instructions = await hugo.agentProfiles.compiledInstructions(profile.id, "Mcp");
  console.log("compiled instructions:", instructions.text.slice(0, 200));
  await hugo.agentProfiles.delete(profile.id);
  await hugo.resources.delete(resource.id);
} catch (e) {
  if (e instanceof HugoError && e.status === 404) console.log("domain routes are not available on this server yet:", e.code);
  else console.log("domain preview failed:", e.message);
}

await hugo.secrets.delete(secret.id);
await hugo.logout();
