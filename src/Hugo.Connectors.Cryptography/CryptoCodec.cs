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
            if (String.IsNullOrEmpty(value)) throw Invalid("Bind a Generic secret with a value field to encryptionKey before encrypting or decrypting.");
            byte[] key = Decode(value, resource.Settings.GetString("keyEncoding", "base64") ?? "base64", 32, false);
            if (key.Length != 16 && key.Length != 24 && key.Length != 32)
            {
                CryptographicOperations.ZeroMemory(key);
                throw Invalid("The encryption key must decode to 16, 24, or 32 bytes.");
            }
            return key;
        }

        internal static ConnectorException Invalid(string message)
        {
            return new ConnectorException(ConnectorErrorCodes.InvalidArgument, message);
        }
    }
}
