# Cryptography connector

A `cryptography` resource gives an agent access to a specific vault-managed AES key. It can decrypt encrypted objects retrieved through S3 or another storage connector, encrypt bytes with fresh random nonces, compute SHA-256/384/512 digests, and compare a digest with an expected value. All four tools run locally under normal resource and binding permissions.

## Configure the resource

Create an **AES key material** secret in **Connections → Secrets**. Its API kind is `AesKey`, and its masked fields are `key`, optional `salt`, and optional `iv`. Bind that one secret to the resource's **AES key material** slot (`encryptionKey`). Use separate bundled secrets and resource aliases for separate encryption configurations. The agent supplies the resource alias, never the secret fields. The connector reads the encrypted vault material internally; the agent does not need permission to reveal it. Missing resource context notes do not imply missing secrets.

Choose **Base64** or **Hex** independently for key, salt, and IV when creating the secret. The secret stores these choices as `keyEncoding`, `saltEncoding`, and `ivEncoding` alongside the encrypted material. Hex-looking strings can also be valid base64, so the selected format is explicit rather than guessed. Each saved format overrides the resource's `keyEncoding`; existing secrets without saved formats keep using that resource setting. With the default `keyDerivation: "None"`, `key` is a direct AES key of 16, 24, or 32 decoded bytes. A raw AES-256 key contains 32 bytes. CBC decryption of raw ciphertext can take its 16-byte IV from the bundle's `iv` field. Encryption generates fresh nonce/IV bytes; it does not reuse that saved IV.

A salt belongs to key derivation rather than the AES cipher itself. For formats that derive a key from password bytes and salt, explicitly select `PBKDF2-SHA256` and set the matching iteration count and derived key length. The bundle's decoded `key` bytes become the password input and its decoded `salt` must contain at least 16 bytes. With `None`, the saved salt is not applied. See the [.NET PBKDF2 contract](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.rfc2898derivebytes.pbkdf2). An existing ciphertext must be decrypted with the same derivation parameters used to encrypt it.

Existing Generic key secrets and separately bound legacy IV secrets continue to work. Create resource shows only the AES key material slot. An existing separate IV binding remains visible when editing that resource. New configurations need only the one AES bundle.

| Setting | Default | Meaning |
|---|---|---|
| `algorithm` | `AES-GCM` | `AES-GCM`, or explicit `AES-CBC` interoperability with PKCS7 padding |
| `keyEncoding` | `base64` | Fallback encoding for fields without a format recorded in the secret |
| `keyDerivation` | `None` | Direct AES key, or explicit `PBKDF2-SHA256` using the bundle salt |
| `iterations` | `100000` | PBKDF2 iteration count from 10000 through 1000000 |
| `derivedKeyBytes` | `32` | PBKDF2 output length: 16, 24 or 32 bytes |
| `maxInputBytes` | `1048576` | Maximum decoded input per call, from 1 byte through 16 MiB |

Attach the resource to the agent. ReadOnly access permits all four tools because they transform bytes without changing external systems. Decryption returns plaintext to the authorized agent, so bind only the keys that agent should use. Normal output limits still apply. The default binding output budget is 64 KiB; increase it when necessary for the complete encoded output. Oversized crypto results fail instead of returning partial ciphertext or plaintext.

**Test connection** validates settings and bound material, then performs a local round trip when a key is present. No network service is required. The key is optional for resources used only for hashing.

## Read an encrypted S3 object

Retrieve the complete object using `storage_read` with `encoding: "base64"`. Check that the result is not truncated. Pass its encoded content to the crypto tool without decoding it as text first. The storage read limit, binding output budget, and crypto input limit must accommodate the complete payload.

For raw AES-CBC ciphertext with a separately configured key and IV, put the key and IV in one AES secret, select `AES-CBC`, and call:

```json
{
  "resource": "blob-crypto",
  "data": "<base64 content from storage_read>",
  "inputEncoding": "base64",
  "outputEncoding": "utf8",
  "format": "separate"
}
```

A short text value may produce only one 16-byte ciphertext block. That is valid separate CBC ciphertext; an IV prefix is not required when the IV is bound in the secret. The agent should attempt the tool and report its actual error before concluding that material is missing.

When `nonce` is omitted in separate CBC mode, the connector uses the bundle's `iv`, then a separately bound legacy IV if no bundle IV exists. That matches a raw AES-CBC/PKCS7 payload encrypted with a direct AES key and separate IV. Leave `keyDerivation` at `None` for this direct-key format; select a derivation method only when the original format used it. An application using a different key derivation or envelope needs its actual format specified.

Use `outputEncoding: "base64"` for binary plaintext. UTF-8 output rejects invalid text rather than replacing bytes. Large encrypted objects cannot be decrypted correctly by independently decrypting arbitrary storage chunks.

## Cipher envelopes

Encryption always generates a fresh random nonce or IV. GCM uses a 12-byte nonce and a 16-byte authentication tag. CBC uses a 16-byte IV and PKCS7 padding. A bound legacy CBC IV is used only for decryption, never to reuse an IV for new encryption.

| Format | AES-GCM | AES-CBC |
|---|---|---|
| `separate` | Ciphertext in `data`; `nonce` and `tag` are separate fields | Ciphertext in `data`; IV in `nonce` or the bound IV secret |
| `combined` | `nonce || ciphertext || tag` in `data` | `IV || ciphertext` in `data` |

`inputEncoding` defaults to `base64`; `hex` is also available. Encryption and hashing additionally accept `utf8` input. `outputEncoding` defaults to `base64`; decryption additionally accepts `utf8`. Separate input nonce, tag, and optional GCM `associatedData` use `inputEncoding`. Returned nonce and tag use `outputEncoding`. GCM associated data must match the value used during encryption. For CBC, omit `associatedData`; an empty string is accepted as absent, while any non-empty value and authentication tags are rejected.

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
