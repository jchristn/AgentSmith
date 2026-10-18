// Run from sdk/js: npm run build && node examples/documents.mjs [local-file.md]
// Requires HUGO_EMAIL and HUGO_PASSWORD; optional HUGO_SDK_SERVER and HUGO_TENANT.
import { readFile } from 'node:fs/promises';
import { basename } from 'node:path';
import { fileURLToPath } from 'node:url';
import { HugoClient } from '../dist/esm/index.js';

const email = process.env.HUGO_EMAIL;
const password = process.env.HUGO_PASSWORD;
if (!email || !password) throw new Error('Set HUGO_EMAIL and HUGO_PASSWORD for an isolated test tenant.');
const source = process.argv[2] || fileURLToPath(new URL('../../fixtures/LOG_MESSAGE_STRUCTURE.md', import.meta.url));
const markdown = new TextDecoder('utf-8', { fatal: true }).decode(await readFile(source));
const suffix = Date.now().toString(36);
const name = basename(source).replace(/\.(md|txt)$/i, `-${suffix}$&`);
const client = new HugoClient(process.env.HUGO_SDK_SERVER || 'http://127.0.0.1:8800');
let profile;
let document;
try {
  await client.login(email, password, process.env.HUGO_TENANT || 'default');
  profile = await client.agentProfiles.create({ name: `document-example-${suffix}` });
  document = await client.promptDocuments.create({ name, description: 'Read before analyzing synthetic application logs.', markdown, visibility: 'Private', active: true });
  const initial = await client.agentPromptDocuments.list(profile.id);
  let attachment = await client.agentPromptDocuments.create(profile.id, { documentId: document.id, deliveryMode: 'Reference', revisionPolicy: 'Latest', enabled: true, allowPublicAccess: false, expectedDocumentSetVersion: initial.documentSetVersion });
  console.log('Attached documents:', (await client.agentPromptDocuments.list(profile.id)).items.map((item) => item.name));
  const reference = await client.agentPromptDocuments.preview(profile.id, { audience: 'Authenticated' });
  console.log('Reference preview:', reference.manifest, reference.issues);
  const current = await client.agentPromptDocuments.list(profile.id);
  attachment = await client.agentPromptDocuments.update(profile.id, attachment.id, { expectedVersion: attachment.version, expectedDocumentSetVersion: current.documentSetVersion, deliveryMode: 'Inline' });
  const inline = await client.agentPromptDocuments.preview(profile.id, { audience: 'Authenticated' });
  if (!inline.compiledText.includes('Synthetic application log schema') && source.endsWith('LOG_MESSAGE_STRUCTURE.md')) throw new Error('Inline preview omitted the example content.');
  console.log('Inline context bytes:', inline.inlineBytes, 'estimated tokens:', inline.estimatedTokens);
  const final = await client.agentPromptDocuments.list(profile.id);
  await client.agentPromptDocuments.delete(profile.id, attachment.id, attachment.version, final.documentSetVersion);
} finally {
  // Deleting our newly created profile also removes any attachment left after a failed step.
  try { if (profile) await client.agentProfiles.delete(profile.id); }
  finally { try { if (document) { const current = await client.promptDocuments.get(document.id); await client.promptDocuments.delete(document.id, current.version); } } finally { await client.logout(); } }
}
