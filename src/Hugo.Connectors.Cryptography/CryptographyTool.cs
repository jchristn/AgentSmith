namespace Hugo.Connectors.Cryptography
{
    using System;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.Json;
    using System.Text.Json.Serialization;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>Bounded local byte transforms. Immutable and thread-safe; no external mutation occurs.</summary>
    public class CryptographyTool : IConnectorTool
    {
        private readonly string _Operation;
        private static readonly JsonSerializerOptions _Json = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        /// <inheritdoc />
        public ToolDescriptor Descriptor { get; }

        /// <summary>Create a supported cryptographic operation.</summary>
        /// <param name="operation">encrypt, decrypt, hash, or verify_hash.</param>
        /// <exception cref="ArgumentException">The operation is unsupported.</exception>
        public CryptographyTool(string operation)
        {
            if (!new string[] { "encrypt", "decrypt", "hash", "verify_hash" }.Contains(operation)) throw new ArgumentException("Unsupported cryptographic operation.", nameof(operation));
            _Operation = operation;
            string fields = "\"data\":{\"type\":\"string\"},\"inputEncoding\":{\"type\":\"string\",\"enum\":[\"base64\",\"hex\",\"utf8\"]},\"outputEncoding\":{\"type\":\"string\",\"enum\":[\"base64\",\"hex\",\"utf8\"]}";
            if (operation == "encrypt" || operation == "decrypt") fields += ",\"format\":{\"type\":\"string\",\"enum\":[\"separate\",\"combined\"]},\"associatedData\":{\"type\":\"string\"}";
            if (operation == "decrypt") fields += ",\"nonce\":{\"type\":\"string\"},\"tag\":{\"type\":\"string\"}";
            if (operation == "hash" || operation == "verify_hash") fields += ",\"hashAlgorithm\":{\"type\":\"string\",\"enum\":[\"SHA256\",\"SHA384\",\"SHA512\"]}";
            if (operation == "verify_hash") fields += ",\"expectedHash\":{\"type\":\"string\"}";
            Descriptor = new ToolDescriptor
            {
                Name = "crypto_" + operation,
                Title = "Cryptography " + operation.Replace('_', ' '),
                Description = "Locally " + operation.Replace('_', ' ') + " bytes with this resource's administrator-managed key and cipher. Never supply keys in arguments. Input defaults to base64, output defaults to base64. Encryption generates a fresh random nonce/IV. Separate format returns nonce and GCM tag; combined format is nonce/IV || ciphertext || GCM tag (no tag for CBC). GCM nonce is 12 bytes and tag is 16 bytes. CBC uses a 16-byte IV and PKCS7 padding and is unauthenticated. Associated data uses inputEncoding. Hashes support SHA256/384/512. Plaintext can be utf8; decryption input cannot. Output and input limits apply; never truncate encrypted blobs or guess their format.",
                BaseEffect = EffectEnum.Read,
                InputSchemaJson = "{\"type\":\"object\",\"additionalProperties\":false,\"required\":[\"data\"" + (operation == "verify_hash" ? ",\"expectedHash\"" : "") + "],\"properties\":{" + fields + "}}",
                OutputDescription = "JSON {algorithm,data,encoding,nonce?,tag?,matches?,warning?}. Data is complete or the operation fails; never truncated."
            };
        }

        /// <inheritdoc />
        public EffectClassification Classify(ConnectorInvocationContext context, ToolArguments arguments)
        {
            return new EffectClassification(EffectEnum.Read, "Local cryptographic transform without external mutation", Descriptor.Title);
        }

        /// <inheritdoc />
        public Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            ArgumentNullException.ThrowIfNull(context);
            ArgumentNullException.ThrowIfNull(arguments);
            token.ThrowIfCancellationRequested();
            try
            {
                CryptoRequest request = JsonSerializer.Deserialize<CryptoRequest>(arguments.Json, _Json) ?? throw CryptoCodec.Invalid("A request object is required.");
                if (!arguments.Has("data")) throw CryptoCodec.Invalid("data is required.");
                int limit = context.Resource.Settings.GetInt("maxInputBytes", 1048576, 1, 16777216);
                bool cipher = _Operation == "encrypt" || _Operation == "decrypt";
                byte[] data = CryptoCodec.Decode(request.Data, request.InputEncoding, limit, _Operation != "decrypt");
                try
                {
                    CryptoResponse response = cipher ? Transform(context.Resource, request, data, limit) : Hash(request, data);
                    token.ThrowIfCancellationRequested();
                    string json = JsonSerializer.Serialize(response, _Json);
                    if (Encoding.UTF8.GetByteCount(json) > context.Limits.MaxBytes) throw CryptoCodec.Invalid("Complete cryptographic output exceeds the binding output limit. Increase maxOutputBytes or use a smaller input; partial ciphertext and plaintext are not returned.");
                    return Task.FromResult(ToolResult.FromText(json, json));
                }
                finally { CryptographicOperations.ZeroMemory(data); }
            }
            catch (Exception e) when (e is JsonException || e is NotSupportedException)
            {
                return Task.FromResult(ToolResult.Error(ConnectorErrorCodes.InvalidArgument, "Invalid cryptographic request fields or types."));
            }
            catch (ConnectorException e)
            {
                return Task.FromResult(ToolResult.Error(e.Code, e.Message));
            }
            catch (CryptographicException)
            {
                return Task.FromResult(ToolResult.Error("cryptographic_failure", "Cryptographic operation failed. Check the configured key, cipher, encoding, envelope, nonce/IV, authentication tag, and associated data."));
            }
        }

        private CryptoResponse Hash(CryptoRequest request, byte[] data)
        {
            byte[] digest = request.HashAlgorithm switch
            {
                "SHA256" => SHA256.HashData(data),
                "SHA384" => SHA384.HashData(data),
                "SHA512" => SHA512.HashData(data),
                _ => throw CryptoCodec.Invalid("hashAlgorithm must be SHA256, SHA384, or SHA512.")
            };
            CryptoResponse response = new CryptoResponse { Algorithm = request.HashAlgorithm, Encoding = request.OutputEncoding };
            if (_Operation == "verify_hash")
            {
                if (request.ExpectedHash == null) throw CryptoCodec.Invalid("expectedHash is required.");
                byte[] expected = CryptoCodec.Decode(request.ExpectedHash, request.OutputEncoding, digest.Length, false);
                response.Matches = CryptographicOperations.FixedTimeEquals(digest, expected);
            }
            else response.Data = CryptoCodec.Encode(digest, request.OutputEncoding);
            return response;
        }

        private CryptoResponse Transform(ConnectorResourceContext resource, CryptoRequest request, byte[] data, int limit)
        {
            string algorithm = resource.Settings.GetString("algorithm", "AES-GCM") ?? "AES-GCM";
            if (algorithm != "AES-GCM" && algorithm != "AES-CBC") throw CryptoCodec.Invalid("algorithm must be AES-GCM or AES-CBC.");
            if (request.Format != "separate" && request.Format != "combined") throw CryptoCodec.Invalid("format must be separate or combined.");
            if (_Operation == "encrypt" && (request.Nonce != null || request.Tag != null)) throw CryptoCodec.Invalid("Encryption generates fresh nonce/IV values; supplied nonce or tag is prohibited.");
            if (algorithm == "AES-CBC" && (request.AssociatedData != null || request.Tag != null)) throw CryptoCodec.Invalid("AES-CBC does not support associatedData or authentication tags.");
            byte[] key = CryptoCodec.ReadKey(resource);
            byte[]? output = null;
            try
            {
                int nonceLength = algorithm == "AES-GCM" ? 12 : 16;
                byte[] nonce;
                byte[] tag = new byte[algorithm == "AES-GCM" ? 16 : 0];
                byte[] ciphertext = data;
                byte[] aad = request.AssociatedData == null ? Array.Empty<byte>() : CryptoCodec.Decode(request.AssociatedData, request.InputEncoding, limit);
                if (_Operation == "encrypt") nonce = RandomNumberGenerator.GetBytes(nonceLength);
                else if (request.Format == "combined")
                {
                    if (request.Nonce != null || request.Tag != null) throw CryptoCodec.Invalid("Combined format cannot also supply separate nonce or tag.");
                    if (data.Length < nonceLength + tag.Length) throw CryptoCodec.Invalid("The combined ciphertext envelope is too short.");
                    nonce = data.AsSpan(0, nonceLength).ToArray();
                    ciphertext = data.AsSpan(nonceLength, data.Length - nonceLength - tag.Length).ToArray();
                    if (tag.Length > 0) tag = data.AsSpan(data.Length - tag.Length).ToArray();
                }
                else
                {
                    if (request.Nonce == null && algorithm == "AES-CBC")
                    {
                        string boundIv = resource.Secrets.Get("encryptionKey", "iv") ?? resource.Secrets.Get("initializationVector", "value") ?? throw CryptoCodec.Invalid("Separate CBC decryption needs nonce, AES secret iv, or a bound initializationVector.");
                        nonce = CryptoCodec.Decode(boundIv, resource.Settings.GetString("keyEncoding", "base64") ?? "base64", nonceLength, false);
                    }
                    else
                    {
                        if (request.Nonce == null) throw CryptoCodec.Invalid("Separate GCM decryption requires nonce.");
                        nonce = CryptoCodec.Decode(request.Nonce, request.InputEncoding, nonceLength, false);
                    }
                    if (algorithm == "AES-GCM")
                    {
                        if (request.Tag == null) throw CryptoCodec.Invalid("Separate GCM decryption requires tag.");
                        tag = CryptoCodec.Decode(request.Tag, request.InputEncoding, 16, false);
                    }
                }
                if (nonce.Length != nonceLength || (algorithm == "AES-GCM" && tag.Length != 16)) throw CryptoCodec.Invalid("GCM requires a 12-byte nonce and 16-byte tag; CBC requires a 16-byte IV.");
                if (algorithm == "AES-GCM")
                {
                    output = new byte[ciphertext.Length];
                    using (AesGcm aes = new AesGcm(key, 16))
                    {
                        if (_Operation == "encrypt") aes.Encrypt(nonce, data, output, tag, aad);
                        else aes.Decrypt(nonce, ciphertext, tag, output, aad);
                    }
                }
                else
                {
                    using (Aes aes = Aes.Create())
                    {
                        aes.Key = key;
                        output = _Operation == "encrypt" ? aes.EncryptCbc(data, nonce, PaddingMode.PKCS7) : aes.DecryptCbc(ciphertext, nonce, PaddingMode.PKCS7);
                    }
                }
                CryptoResponse response = new CryptoResponse { Algorithm = algorithm, Encoding = request.OutputEncoding, Warning = algorithm == "AES-CBC" ? "AES-CBC is unauthenticated. Verify a trusted signature or MAC before using plaintext." : null };
                if (_Operation == "encrypt" && request.Format == "combined") response.Data = CryptoCodec.Encode(nonce.Concat(output).Concat(tag).ToArray(), request.OutputEncoding);
                else
                {
                    response.Data = CryptoCodec.Encode(output, request.OutputEncoding, _Operation == "decrypt");
                    if (_Operation == "encrypt")
                    {
                        response.Nonce = CryptoCodec.Encode(nonce, request.OutputEncoding);
                        if (tag.Length > 0) response.Tag = CryptoCodec.Encode(tag, request.OutputEncoding);
                    }
                }
                return response;
            }
            finally
            {
                CryptographicOperations.ZeroMemory(key);
                if (output != null) CryptographicOperations.ZeroMemory(output);
            }
        }
    }
}
