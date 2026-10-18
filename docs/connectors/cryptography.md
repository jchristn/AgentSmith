# Cryptography connector

A `cryptography` resource gives an agent access to a specific vault-managed AES key. It can decrypt encrypted objects retrieved through S3 or another storage connector, encrypt bytes with fresh random nonces, compute SHA-256/384/512 digests, and compare a digest with an expected value. All four tools run locally under normal resource and binding permissions.

## Configure the resource

Create a **Generic** secret in **Connections → Secrets**. Put the base64-encoded AES key in its `value` field. The decoded key must contain 16, 24, or 32 bytes. Choose 32 bytes for AES-256. Hexadecimal key material is also supported by setting `keyEncoding` to `hex`.

Create a **Cryptography** resource, bind the secret to `encryptionKey`, and select its cipher. Use separate resources and binding aliases for separate keys. The agent selects the resource alias; it never supplies the key in tool arguments. An optional second Generic secret bound to `initializationVector` holds a legacy CBC IV in its `value` field, using the same encoding as the key.

| Setting | Default | Meaning |
|---|---|---|
| `algorithm` | `AES-GCM` | `AES-GCM`, or explicit `AES-CBC` interoperability with PKCS7 padding |
| `keyEncoding` | `base64` | `base64` or `hex` encoding of the bound key and optional CBC IV |
| `maxInputBytes` | `1048576` | Maximum decoded input per call, from 1 byte through 16 MiB |

Attach the resource to the agent. ReadOnly access permits all four tools because they transform bytes without changing external systems. Decryption returns plaintext to the authorized agent, so bind only the keys that agent should use. Normal output limits still apply. The default binding output budget is 64 KiB; increase it when necessary for the complete encoded output. Oversized crypto results fail instead of returning partial ciphertext or plaintext.

**Test connection** validates settings and bound material, then performs a local round trip when a key is present. No network service is required. The key is optional for resources used only for hashing.

## Read an encrypted S3 object

Retrieve the complete object using `storage_read` with `encoding: "base64"`. Check that the result is not truncated. Pass its encoded content to the crypto tool without decoding it as text first. The storage read limit, binding output budget, and crypto input limit must accommodate the complete payload.

For raw AES-CBC ciphertext with a separately configured key and IV, bind both secrets, select `AES-CBC`, and call:

```json
{
  "resource": "blob-crypto",
  "data": "<base64 content from storage_read>",
  "inputEncoding": "base64",
  "outputEncoding": "utf8",
  "format": "separate"
}
```

The connector uses the bound IV when `nonce` is omitted in separate CBC mode. That matches a raw AES-CBC/PKCS7 payload encrypted with a direct AES key and separate IV. No passphrase derivation or salt processing is implied. An application using a different key derivation or envelope needs its actual format specified.

Use `outputEncoding: "base64"` for binary plaintext. UTF-8 output rejects invalid text rather than replacing bytes. Large encrypted objects cannot be decrypted correctly by independently decrypting arbitrary storage chunks.

## Cipher envelopes

Encryption always generates a fresh random nonce or IV. GCM uses a 12-byte nonce and a 16-byte authentication tag. CBC uses a 16-byte IV and PKCS7 padding. A bound legacy CBC IV is used only for decryption, never to reuse an IV for new encryption.

| Format | AES-GCM | AES-CBC |
|---|---|---|
| `separate` | Ciphertext in `data`; `nonce` and `tag` are separate fields | Ciphertext in `data`; IV in `nonce` or the bound IV secret |
| `combined` | `nonce || ciphertext || tag` in `data` | `IV || ciphertext` in `data` |

`inputEncoding` defaults to `base64`; `hex` is also available. Encryption and hashing additionally accept `utf8` input. `outputEncoding` defaults to `base64`; decryption additionally accepts `utf8`. Separate input nonce, tag, and optional GCM `associatedData` use `inputEncoding`. Returned nonce and tag use `outputEncoding`. GCM associated data must match the value used during encryption.

Prefer GCM for new ciphertext because it authenticates the data. CBC compatibility does not establish authenticity. A digest comparison verifies the expected bytes only when the expected digest comes from a trusted source.

## Tools

Every call includes the resource's bound alias in `resource`. Keys, passphrases, and replacement key material are not accepted as arguments.

| Tool | Purpose | Additional arguments |
|---|---|---|
| `crypto_encrypt` | Encrypt bytes with the resource's cipher and key | `data`, `inputEncoding`, `outputEncoding`, `format`, optional GCM `associatedData` |
| `crypto_decrypt` | Decrypt bytes, checking the GCM tag where applicable | `data`, encodings, `format`, separate `nonce`/`tag` where required, optional GCM `associatedData` |
| `crypto_hash` | Compute a digest | `data`, encodings, `hashAlgorithm`: `SHA256` (default), `SHA384`, or `SHA512` |
| `crypto_verify_hash` | Compare a digest with an expected value using constant-time comparison | Hash arguments plus `expectedHash`, encoded using `outputEncoding` |

For example, verify a hex SHA-256 digest of decrypted base64 bytes with:

```json
{
  "resource": "blob-crypto",
  "data": "<base64 plaintext>",
  "inputEncoding": "base64",
  "outputEncoding": "hex",
  "hashAlgorithm": "SHA256",
  "expectedHash": "<expected lowercase or uppercase hex digest>"
}
```

Verification returns `matches`; hashing returns the encoded digest in `data`. Encryption returns encoded ciphertext and, for separate mode, nonce and tag fields. Decryption returns encoded plaintext in `data`. Invalid key material, malformed encodings, nonce/tag lengths, authentication failures, padding failures, and output-limit failures return errors without key material or partial plaintext.

Resource context can record cipher, envelope, and encoding conventions. Keep keys and decrypted confidential contents out of that context.
