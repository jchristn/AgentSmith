/**
 * Request signing compatible with the server's RequestSigner (src/Hugo.Core/Security/RequestSigner.cs).
 *
 * canonical = METHOD \n PATH \n QUERY \n TENANT \n DATE \n NONCE \n hex(SHA-256(body))
 * signature = lowercase hex HMAC-SHA256(secretKey, canonical)
 *
 * PATH is the raw path without the query, QUERY is the raw query string exactly as sent (without '?'), TENANT is
 * the credential's tenant ID, DATE is ISO 8601 UTC without fractional seconds (yyyy-MM-ddTHH:mm:ssZ), and NONCE is
 * 32 random hex characters. Uses WebCrypto (globalThis.crypto.subtle), which Node 18+, Deno, Bun, and browsers
 * provide; on Node 18 without a global crypto it falls back to node:crypto's webcrypto.
 */

/** Inputs to a canonical string. */
export interface CanonicalParts {
  method: string;
  path: string;
  query: string;
  tenantId: string;
  date: string;
  nonce: string;
  body?: Uint8Array | string;
}

/** Headers produced for one signed request. */
export interface SignedHeaders {
  "x-access-key": string;
  "x-date": string;
  "x-nonce": string;
  "x-signature": string;
  "x-tenant-id": string;
}

const encoder = new TextEncoder();

async function subtle(): Promise<SubtleCrypto> {
  const g = globalThis as { crypto?: Crypto };
  if (g.crypto?.subtle) return g.crypto.subtle;
  // The variable specifier keeps bundlers from resolving node:crypto for browser builds.
  const moduleName = "node:crypto";
  const nodeCrypto = (await import(moduleName)) as { webcrypto: Crypto };
  return nodeCrypto.webcrypto.subtle;
}

/** Lowercase hex of a byte buffer. */
export function toHex(buffer: ArrayBuffer | Uint8Array): string {
  const bytes = buffer instanceof Uint8Array ? buffer : new Uint8Array(buffer);
  let out = "";
  for (const b of bytes) out += b.toString(16).padStart(2, "0");
  return out;
}

/** Random lowercase hex string of the given byte length. */
export function randomHex(bytes: number): string {
  const array = new Uint8Array(bytes);
  const g = globalThis as { crypto?: Crypto };
  if (g.crypto?.getRandomValues) g.crypto.getRandomValues(array);
  else for (let i = 0; i < bytes; i++) array[i] = Math.floor(Math.random() * 256);
  return toHex(array);
}

/** Current UTC time formatted as the server expects in x-date. */
export function signingDate(now: Date = new Date()): string {
  return now.toISOString().replace(/\.\d{3}Z$/, "Z");
}

function bytes(body?: Uint8Array | string): Uint8Array {
  if (body === undefined) return new Uint8Array(0);
  return typeof body === "string" ? encoder.encode(body) : body;
}

/** Lowercase hex SHA-256. */
export async function sha256Hex(body?: Uint8Array | string): Promise<string> {
  const crypto = await subtle();
  return toHex(await crypto.digest("SHA-256", bytes(body) as BufferSource));
}

/** Build the canonical string. */
export async function canonicalize(parts: CanonicalParts): Promise<string> {
  const bodyHash = await sha256Hex(parts.body);
  return [parts.method.toUpperCase(), parts.path, parts.query, parts.tenantId, parts.date, parts.nonce, bodyHash].join("\n");
}

/** Lowercase hex HMAC-SHA256 of the canonical string. */
export async function sign(secretKey: string, canonical: string): Promise<string> {
  if (!secretKey) throw new Error("secretKey is required to sign a request");
  const crypto = await subtle();
  const key = await crypto.importKey("raw", encoder.encode(secretKey), { name: "HMAC", hash: "SHA-256" }, false, ["sign"]);
  return toHex(await crypto.sign("HMAC", key, encoder.encode(canonical)));
}

/** Produce the signed-request headers for one request. */
export async function signRequest(
  accessKey: string,
  secretKey: string,
  tenantId: string,
  method: string,
  path: string,
  query: string,
  body?: Uint8Array | string,
): Promise<SignedHeaders> {
  const date = signingDate();
  const nonce = randomHex(16);
  const canonical = await canonicalize({ method, path, query, tenantId, date, nonce, body });
  return {
    "x-access-key": accessKey,
    "x-date": date,
    "x-nonce": nonce,
    "x-signature": await sign(secretKey, canonical),
    "x-tenant-id": tenantId,
  };
}
