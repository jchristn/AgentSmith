namespace Hugo.Connectors.Cryptography
{
    using System;
    using System.Security.Cryptography;
    using System.Text;
    using Hugo.Connectors.Abstractions;

    internal static class CryptoCodec
    {
        internal static byte[] Decode(string value, string encoding, int limit, bool plaintext = true)
        {
            if (value == null || value.Length > (long)limit * 4 + 16) throw Invalid("Input exceeds the configured byte limit.");
            byte[] bytes;
            try
            {
                bytes = encoding switch
                {
                    "base64" => Convert.FromBase64String(value),
                    "hex" => Convert.FromHexString(value),
                    "utf8" when plaintext => new UTF8Encoding(false, true).GetBytes(value),
                    _ => throw Invalid("Encoding must be base64 or hex; utf8 is permitted only for plaintext and hashing.")
                };
            }
            catch (Exception e) when (e is FormatException || e is EncoderFallbackException)
            {
                throw Invalid("Data is not valid in the selected encoding.");
            }
            if (bytes.Length > limit)
            {
                CryptographicOperations.ZeroMemory(bytes);
                throw Invalid("Input exceeds the configured byte limit.");
            }
            return bytes;
        }

        internal static string Encode(byte[] value, string encoding, bool plaintext = false)
        {
            try
            {
                return encoding switch
                {
                    "base64" => Convert.ToBase64String(value),
                    "hex" => Convert.ToHexString(value).ToLowerInvariant(),
                    "utf8" when plaintext => new UTF8Encoding(false, true).GetString(value),
                    _ => throw Invalid("Output encoding must be base64 or hex; utf8 is permitted only for decrypted plaintext.")
                };
            }
            catch (DecoderFallbackException)
            {
                throw Invalid("Decrypted bytes are not valid UTF-8; request base64 or hex output.");
            }
        }

        internal static byte[] ReadKey(ConnectorResourceContext resource)
        {
            string? value = resource.Secrets.Get("encryptionKey", "value") ?? resource.Secrets.Get("encryptionKey", "key");
            if (String.IsNullOrEmpty(value)) throw Invalid("Bind an AES key secret with a key field, or a legacy Generic secret with value, to encryptionKey.");
            string encoding = MaterialEncoding(resource, "key");
            string derivation = resource.Settings.GetString("keyDerivation", "None") ?? "None";
            if (derivation != "None" && derivation != "PBKDF2-SHA256") throw Invalid("keyDerivation must be None or PBKDF2-SHA256.");
            byte[] key = Decode(value, encoding, derivation == "None" ? 32 : 4096, false);
            if (derivation == "PBKDF2-SHA256")
            {
                try
                {
                    if (key.Length == 0) throw Invalid("PBKDF2 requires a nonempty decoded key/password.");
                    int iterations = ReadInteger(resource, "iterations", 100000, 10000, 1000000);
                    int length = ReadInteger(resource, "derivedKeyBytes", 32, 16, 32);
                    if (length != 16 && length != 24 && length != 32) throw Invalid("derivedKeyBytes must be 16, 24, or 32.");
                    string saltValue = resource.Secrets.Get("encryptionKey", "salt") ?? throw Invalid("PBKDF2 requires salt in the bound AES key secret.");
                    byte[] salt = Decode(saltValue, MaterialEncoding(resource, "salt"), 4096, false);
                    try
                    {
                        if (salt.Length < 16) throw Invalid("PBKDF2 salt must decode to at least 16 bytes.");
                        return Rfc2898DeriveBytes.Pbkdf2(key, salt, iterations, HashAlgorithmName.SHA256, length);
                    }
                    finally { CryptographicOperations.ZeroMemory(salt); }
                }
                finally { CryptographicOperations.ZeroMemory(key); }
            }
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
            {
                CryptographicOperations.ZeroMemory(key);
                throw Invalid("The encryption key must decode to 16, 24, or 32 bytes when keyDerivation is None.");
            }
            return key;
        }

        internal static string MaterialEncoding(ConnectorResourceContext resource, string field)
        {
            string encoding = resource.Secrets.Get("encryptionKey", field + "Encoding") ?? resource.Settings.GetString("keyEncoding", "base64") ?? "base64";
            if (encoding != "base64" && encoding != "hex") throw Invalid("AES material encoding must be base64 or hex.");
            return encoding;
        }

        internal static int ReadInteger(ConnectorResourceContext resource, string name, int fallback, int minimum, int maximum)
        {
            string? raw = resource.Settings.GetString(name);
            if (raw == null) return fallback;
            if (!Int32.TryParse(raw, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out int value) || value < minimum || value > maximum) throw Invalid(name + " is outside its configured range.");
            return value;
        }

        internal static ConnectorException Invalid(string message)
        {
            return new ConnectorException(ConnectorErrorCodes.InvalidArgument, message);
        }
    }
}
