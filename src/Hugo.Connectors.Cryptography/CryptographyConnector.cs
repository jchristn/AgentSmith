namespace Hugo.Connectors.Cryptography
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Globalization;
    using System.Security.Cryptography;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>Local cryptography using administrator-managed vault key material. Thread-safe.</summary>
    public class CryptographyConnector : IConnector
    {
        /// <summary>Connector type and family identifier.</summary>
        public static readonly string TypeName = "cryptography";

        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>Create the cryptographic connector. No network client or dependency is required.</summary>
        public CryptographyConnector()
        {
            Tools = new List<IConnectorTool> { new CryptographyTool("encrypt"), new CryptographyTool("decrypt"), new CryptographyTool("hash"), new CryptographyTool("verify_hash") };
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema();
            schema.Add(new SettingDescriptor("algorithm", SettingValueKindEnum.Enum, "Cipher algorithm")
            {
                DefaultValue = "AES-GCM", AllowedValues = new List<string> { "AES-GCM", "AES-CBC" }, ShowInSummary = true,
                Help = "AES-GCM authenticates ciphertext and is the default. AES-CBC uses PKCS7 padding for explicit legacy interoperability and does not authenticate data. No cipher or envelope is guessed."
            });
            schema.Add(new SettingDescriptor("keyEncoding", SettingValueKindEnum.Enum, "Key material encoding")
            {
                DefaultValue = "base64", AllowedValues = new List<string> { "base64", "hex" },
                Help = "Encoding of AES secret key, salt and iv fields, or legacy Generic value fields. Direct AES keys decode to 16, 24, or 32 bytes. PBKDF2 treats decoded key bytes as the password. CBC IVs decode to 16 bytes."
            });
            schema.Add(new SettingDescriptor("keyDerivation", SettingValueKindEnum.Enum, "Key derivation")
            {
                DefaultValue = "None", AllowedValues = new List<string> { "None", "PBKDF2-SHA256" },
                Help = "None uses decoded AES key bytes directly and ignores stored salt. PBKDF2-SHA256 explicitly derives the AES key from decoded key/password and salt fields. Salt must decode to at least 16 bytes."
            });
            schema.Add(new SettingDescriptor("iterations", SettingValueKindEnum.Integer, "PBKDF2 iterations")
            {
                DefaultValue = "100000", Minimum = 10000, Maximum = 1000000,
                Help = "PBKDF2-SHA256 iteration count, default 100000, range 10000 through 1000000. Must match the producer of existing encrypted data."
            });
            schema.Add(new SettingDescriptor("derivedKeyBytes", SettingValueKindEnum.Enum, "Derived AES key bytes")
            {
                DefaultValue = "32", AllowedValues = new List<string> { "16", "24", "32" },
                Help = "PBKDF2 output length: 16, 24, or 32 bytes (default 32). Ignored when keyDerivation is None."
            });
            schema.Add(new SettingDescriptor("maxInputBytes", SettingValueKindEnum.Integer, "Maximum input bytes")
            {
                DefaultValue = "1048576", Minimum = 1, Maximum = 16777216,
                Help = "Maximum decoded input per call, default 1 MiB, range 1 byte through 16 MiB. Binding output limits also apply; oversized results fail rather than truncate cryptographic data."
            });
            Descriptor = new ConnectorDescriptor
            {
                Type = TypeName, Family = TypeName, DisplayName = "Cryptography", Category = ConnectorCategoryEnum.Other, IconKey = "shield", Version = "1.0.0",
                Description = "Encrypt and decrypt bytes with administrator-managed AES keys, compute SHA-256/384/512 hashes, and verify expected hashes. Local transforms support binary storage objects without exposing keys.",
                SettingsSchema = schema,
                SecretSlots = new List<SecretSlotDescriptor>
                {
                    new SecretSlotDescriptor("encryptionKey", "AES key bundle", SecretKindEnum.AesKey, SecretKindEnum.Generic) { Help = "AES key secret fields key, optional salt and iv, encoded using keyEncoding. Generic value/key remains supported. Salt is used only with explicit PBKDF2-SHA256. Separate CBC decryption IV precedence: tool nonce, bundle iv, legacy initializationVector. Encryption always generates a fresh IV. Optional for hashing; keys never supplied through tool arguments." },
                    new SecretSlotDescriptor("initializationVector", "Legacy CBC initialization vector", SecretKindEnum.Generic) { Help = "Optional Generic secret field value containing the 16-byte CBC IV, encoded using keyEncoding. Used only when decrypting separate CBC ciphertext without an IV argument or AES bundle iv. Encryption always generates a fresh IV." }
                },
                DefaultInstructionsKey = "connector.cryptography.instructions",
                DefaultInstructions = EmbeddedText.Read(typeof(CryptographyConnector).Assembly, "connector.cryptography.instructions.md"),
                DocumentationUrl = "https://learn.microsoft.com/dotnet/standard/security/cryptographic-services"
            };
        }

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(resource);
            token.ThrowIfCancellationRequested();
            ConnectorValidationResult result = new ConnectorValidationResult();
            string algorithm = resource.Settings.GetString("algorithm", "AES-GCM") ?? "AES-GCM";
            if (algorithm != "AES-GCM" && algorithm != "AES-CBC") result.Add("algorithm", "invalid_value", "Select AES-GCM or AES-CBC.");
            string encoding = resource.Settings.GetString("keyEncoding", "base64") ?? "base64";
            if (encoding != "base64" && encoding != "hex") result.Add("keyEncoding", "invalid_value", "Select base64 or hex.");
            string? raw = resource.Settings.GetString("maxInputBytes");
            if (raw != null && (!Int32.TryParse(raw, NumberStyles.Integer, CultureInfo.InvariantCulture, out int maximum) || maximum < 1 || maximum > 16777216)) result.Add("maxInputBytes", "out_of_range", "maxInputBytes must be between 1 and 16777216.");
            string derivation = resource.Settings.GetString("keyDerivation", "None") ?? "None";
            if (derivation != "None" && derivation != "PBKDF2-SHA256") result.Add("keyDerivation", "invalid_value", "Select None or PBKDF2-SHA256.");
            try { CryptoCodec.ReadInteger(resource, "iterations", 100000, 10000, 1000000); }
            catch (ConnectorException) { result.Add("iterations", "out_of_range", "iterations must be between 10000 and 1000000."); }
            try
            {
                int length = CryptoCodec.ReadInteger(resource, "derivedKeyBytes", 32, 16, 32);
                if (length != 16 && length != 24 && length != 32) result.Add("derivedKeyBytes", "invalid_value", "derivedKeyBytes must be 16, 24, or 32.");
            }
            catch (ConnectorException) { result.Add("derivedKeyBytes", "invalid_value", "derivedKeyBytes must be 16, 24, or 32."); }
            string? bundleIv = resource.Secrets.Get("encryptionKey", "iv");
            if (!String.IsNullOrEmpty(bundleIv))
            {
                try
                {
                    byte[] iv = CryptoCodec.Decode(bundleIv, encoding, 16, false);
                    if (iv.Length != 16) result.Add("encryptionKey", "invalid_value", "The AES secret iv must decode to 16 bytes.");
                    CryptographicOperations.ZeroMemory(iv);
                }
                catch (ConnectorException) { result.Add("encryptionKey", "invalid_value", "The AES secret iv must decode to 16 bytes in keyEncoding."); }
            }
            if (resource.Secrets.HasSlot("encryptionKey"))
            {
                try
                {
                    byte[] key = CryptoCodec.ReadKey(resource);
                    CryptographicOperations.ZeroMemory(key);
                }
                catch (ConnectorException) { result.Add("encryptionKey", "invalid_value", "The AES key bundle must match keyEncoding and the configured keyDerivation, salt, iterations and derivedKeyBytes settings."); }
            }
            if (resource.Secrets.HasSlot("initializationVector"))
            {
                try
                {
                    byte[] iv = CryptoCodec.Decode(resource.Secrets.Get("initializationVector", "value") ?? String.Empty, encoding, 16, false);
                    if (iv.Length != 16) result.Add("initializationVector", "invalid_value", "The initialization vector must decode to 16 bytes.");
                    CryptographicOperations.ZeroMemory(iv);
                }
                catch (ConnectorException) { result.Add("initializationVector", "invalid_value", "The initialization vector must decode to 16 bytes in keyEncoding."); }
            }
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            ConnectorValidationResult validation = await ValidateAsync(resource, token).ConfigureAwait(false);
            if (!validation.IsValid) return ConnectorHealthResult.Fail(ConnectorErrorCodes.InvalidArgument, "Cryptography settings or bound key material are invalid.", stopwatch.Elapsed.TotalMilliseconds);
            if (!resource.Secrets.HasSlot("encryptionKey"))
            {
                byte[] digest = SHA256.HashData(new byte[] { 1, 2, 3 });
                return ConnectorHealthResult.Ok(digest.Length == 32 ? "Local hashing is available. Bind encryptionKey to enable encryption and decryption." : "Hashing is unavailable.", stopwatch.Elapsed.TotalMilliseconds);
            }
            ConnectorInvocationContext context = new ConnectorInvocationContext { Resource = resource };
            CryptographyTool encrypt = new CryptographyTool("encrypt");
            ToolResult encrypted = await encrypt.ExecuteAsync(context, new ToolArguments("{\"data\":\"AQID\",\"format\":\"combined\"}"), token).ConfigureAwait(false);
            if (encrypted.IsError) return ConnectorHealthResult.Fail(encrypted.ErrorCode ?? "cryptographic_failure", "Local encryption self-test failed.", stopwatch.Elapsed.TotalMilliseconds);
            CryptoResponse? payload = System.Text.Json.JsonSerializer.Deserialize<CryptoResponse>(encrypted.StructuredJson!, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            string request = System.Text.Json.JsonSerializer.Serialize(new CryptoRequest { Data = payload!.Data, Format = "combined" }, new System.Text.Json.JsonSerializerOptions { PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase, DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull });
            ToolResult decrypted = await new CryptographyTool("decrypt").ExecuteAsync(context, new ToolArguments(request), token).ConfigureAwait(false);
            CryptoResponse? roundtrip = decrypted.IsError ? null : System.Text.Json.JsonSerializer.Deserialize<CryptoResponse>(decrypted.StructuredJson!, new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return roundtrip?.Data == "AQID" ? ConnectorHealthResult.Ok("Local encryption, decryption, and key validation succeeded. No network connection is needed.", stopwatch.Elapsed.TotalMilliseconds) : ConnectorHealthResult.Fail("cryptographic_failure", "Local cryptographic round-trip failed.", stopwatch.Elapsed.TotalMilliseconds);
        }
    }
}
