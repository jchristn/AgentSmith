namespace Hugo.Core.Secrets
{
    using System;
    using System.Collections.Generic;
    using System.IO;
    using System.Linq;
    using System.Security.Cryptography;
    using System.Text;
    using System.Text.RegularExpressions;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Inspects SSH private keys in OpenSSH and PEM (PKCS#1, PKCS#8, SEC1) formats and computes the OpenSSH SHA-256
    /// fingerprint of the public key. Never exposes key material. Thread-safe (stateless).
    /// </summary>
    public static class SshKeyInspector
    {
        #region Private-Members

        private static readonly byte[] _OpenSshMagic = Encoding.ASCII.GetBytes("openssh-key-v1\0");
        private static readonly Regex _Block = new Regex(@"-----BEGIN ([A-Z0-9 ]+)-----(.*?)-----END \1-----", RegexOptions.Singleline | RegexOptions.Compiled, TimeSpan.FromSeconds(1));

        #endregion

        #region Public-Methods

        /// <summary>
        /// Whether the text contains a private key block.
        /// </summary>
        /// <param name="text">Text.</param>
        /// <returns>True when a PRIVATE KEY block is present.</returns>
        public static bool ContainsPrivateKey(string? text)
        {
            return !String.IsNullOrEmpty(text) && text.Contains("PRIVATE KEY-----", StringComparison.Ordinal);
        }

        /// <summary>
        /// Inspect the first private key block in the text.
        /// </summary>
        /// <param name="text">Key text.</param>
        /// <param name="passphrase">Optional passphrase for encrypted PEM keys.</param>
        /// <returns>Result with kind PrivateKey.</returns>
        /// <exception cref="ArgumentException">Thrown when no supported private key is found.</exception>
        public static SecretImportResult Inspect(string text, string? passphrase)
        {
            if (String.IsNullOrEmpty(text)) throw new ArgumentException("The key text is empty.", nameof(text));

            SecretImportResult result = new SecretImportResult();
            result.Kind = SecretKindEnum.PrivateKey;
            result.SuggestedField = "privateKey";
            result.Fields = new List<string> { "privateKey", "passphrase" };
            result.HasPrivateKey = true;

            Match match = _Block.Matches(text).Cast<Match>().FirstOrDefault(m => m.Groups[1].Value.EndsWith("PRIVATE KEY", StringComparison.Ordinal))
                ?? throw new ArgumentException("No private key block was found.", nameof(text));
            string label = match.Groups[1].Value;
            string block = match.Value;

            if (label == "OPENSSH PRIVATE KEY")
            {
                InspectOpenSsh(Base64Body(match.Groups[2].Value), result);
                return result;
            }

            bool legacyEncrypted = match.Groups[2].Value.Contains("Proc-Type: 4,ENCRYPTED", StringComparison.Ordinal);
            result.Format = label switch
            {
                "RSA PRIVATE KEY" => "PKCS#1",
                "EC PRIVATE KEY" => "SEC1",
                "ENCRYPTED PRIVATE KEY" => "PKCS#8 (encrypted)",
                _ => "PKCS#8"
            };
            if (legacyEncrypted)
            {
                result.Encrypted = true;
                result.Warnings.Add("The key uses legacy PEM encryption; the fingerprint is shown after the key is converted to OpenSSH or PKCS#8 format.");
                return result;
            }
            if (label == "ENCRYPTED PRIVATE KEY")
            {
                result.Encrypted = true;
                if (String.IsNullOrEmpty(passphrase))
                {
                    result.Warnings.Add("The key is encrypted; supply the passphrase to compute its fingerprint.");
                    return result;
                }
            }

            if (TryRsa(block, label, passphrase, result)) return result;
            if (TryEcdsa(block, label, passphrase, result)) return result;
            if (label != "RSA PRIVATE KEY" && label != "EC PRIVATE KEY" && IsEd25519Pkcs8(Base64BodySafe(match.Groups[2].Value)))
            {
                result.KeyType = "ssh-ed25519";
                result.KeySizeBits = 256;
                result.Warnings.Add("Ed25519 keys in PKCS#8 format have no stored public key; convert to OpenSSH format to see the fingerprint.");
                return result;
            }
            throw new ArgumentException(result.Encrypted ? "The key could not be decrypted with the supplied passphrase." : "The private key format is not supported.", nameof(text));
        }

        /// <summary>
        /// Compute the OpenSSH SHA-256 fingerprint of a public key blob.
        /// </summary>
        /// <param name="publicKeyBlob">SSH wire-format public key.</param>
        /// <returns>Fingerprint, for example SHA256:abc...</returns>
        /// <exception cref="ArgumentNullException">Thrown when the blob is null.</exception>
        public static string Fingerprint(byte[] publicKeyBlob)
        {
            if (publicKeyBlob == null) throw new ArgumentNullException(nameof(publicKeyBlob));
            return "SHA256:" + Convert.ToBase64String(SHA256.HashData(publicKeyBlob)).TrimEnd('=');
        }

        #endregion

        #region Private-Methods

        private static void InspectOpenSsh(byte[] data, SecretImportResult result)
        {
            result.Format = "OpenSSH";
            if (data.Length < _OpenSshMagic.Length || !data.AsSpan(0, _OpenSshMagic.Length).SequenceEqual(_OpenSshMagic)) throw new ArgumentException("The OpenSSH key header is invalid.");
            SshWireReader reader = new SshWireReader(data);
            reader.ReadBytes(_OpenSshMagic.Length);
            string cipherName = reader.ReadText();
            reader.ReadText();
            reader.ReadString();
            int keys = reader.ReadUInt32();
            if (keys < 1) throw new ArgumentException("The OpenSSH key holds no keys.");
            byte[] publicBlob = reader.ReadString();
            for (int i = 1; i < keys; i++) reader.ReadString();
            byte[] privateSection = reader.ReadString();

            result.Fingerprint = Fingerprint(publicBlob);
            DescribePublicBlob(publicBlob, result);
            result.Encrypted = !String.Equals(cipherName, "none", StringComparison.Ordinal);
            if (!result.Encrypted)
            {
                try
                {
                    result.Comment = ReadComment(privateSection, result.KeyType);
                }
                catch (CryptographicException)
                {
                    result.Comment = null;
                }
            }
        }

        private static string? ReadComment(byte[] privateSection, string? keyType)
        {
            SshWireReader reader = new SshWireReader(privateSection);
            reader.ReadUInt32();
            reader.ReadUInt32();
            string type = reader.ReadText();
            switch (type)
            {
                case "ssh-ed25519":
                    reader.ReadString();
                    reader.ReadString();
                    break;
                case "ssh-rsa":
                    for (int i = 0; i < 6; i++) reader.ReadString();
                    break;
                default:
                    if (!type.StartsWith("ecdsa-sha2-", StringComparison.Ordinal)) return null;
                    reader.ReadString();
                    reader.ReadString();
                    reader.ReadString();
                    break;
            }
            string comment = reader.ReadText();
            return String.IsNullOrEmpty(comment) ? null : comment;
        }

        private static void DescribePublicBlob(byte[] blob, SecretImportResult result)
        {
            SshWireReader reader = new SshWireReader(blob);
            string type = reader.ReadText();
            result.KeyType = type;
            if (type == "ssh-rsa")
            {
                reader.ReadString();
                byte[] modulus = reader.ReadString();
                int leading = 0;
                while (leading < modulus.Length && modulus[leading] == 0) leading++;
                result.KeySizeBits = (modulus.Length - leading) * 8;
            }
            else if (type == "ssh-ed25519")
            {
                result.KeySizeBits = 256;
            }
            else if (type.StartsWith("ecdsa-sha2-nistp", StringComparison.Ordinal))
            {
                if (Int32.TryParse(type.Substring("ecdsa-sha2-nistp".Length), out int bits)) result.KeySizeBits = bits;
            }
        }

        private static bool TryRsa(string block, string label, string? passphrase, SecretImportResult result)
        {
            if (label == "EC PRIVATE KEY") return false;
            using (RSA rsa = RSA.Create())
            {
                try
                {
                    if (label == "ENCRYPTED PRIVATE KEY") rsa.ImportFromEncryptedPem(block, passphrase);
                    else rsa.ImportFromPem(block);
                }
                catch (Exception e) when (e is CryptographicException || e is ArgumentException)
                {
                    return false;
                }
                RSAParameters parameters = rsa.ExportParameters(false);
                using (MemoryStream blob = new MemoryStream())
                {
                    WriteString(blob, Encoding.ASCII.GetBytes("ssh-rsa"));
                    WriteMpint(blob, parameters.Exponent!);
                    WriteMpint(blob, parameters.Modulus!);
                    result.Fingerprint = Fingerprint(blob.ToArray());
                }
                result.KeyType = "ssh-rsa";
                result.KeySizeBits = rsa.KeySize;
                return true;
            }
        }

        private static bool TryEcdsa(string block, string label, string? passphrase, SecretImportResult result)
        {
            if (label == "RSA PRIVATE KEY") return false;
            using (ECDsa ecdsa = ECDsa.Create())
            {
                try
                {
                    if (label == "ENCRYPTED PRIVATE KEY") ecdsa.ImportFromEncryptedPem(block, passphrase);
                    else ecdsa.ImportFromPem(block);
                }
                catch (Exception e) when (e is CryptographicException || e is ArgumentException)
                {
                    return false;
                }
                ECParameters parameters = ecdsa.ExportParameters(false);
                string? curve = parameters.Curve.Oid?.Value switch
                {
                    "1.2.840.10045.3.1.7" => "nistp256",
                    "1.3.132.0.34" => "nistp384",
                    "1.3.132.0.35" => "nistp521",
                    _ => null
                };
                result.KeySizeBits = ecdsa.KeySize;
                if (curve == null)
                {
                    result.KeyType = "ecdsa";
                    result.Warnings.Add("The elliptic curve is not one SSH supports; no fingerprint was computed.");
                    return true;
                }
                byte[] point = new byte[1 + parameters.Q.X!.Length + parameters.Q.Y!.Length];
                point[0] = 0x04;
                Buffer.BlockCopy(parameters.Q.X, 0, point, 1, parameters.Q.X.Length);
                Buffer.BlockCopy(parameters.Q.Y, 0, point, 1 + parameters.Q.X.Length, parameters.Q.Y.Length);
                using (MemoryStream blob = new MemoryStream())
                {
                    WriteString(blob, Encoding.ASCII.GetBytes("ecdsa-sha2-" + curve));
                    WriteString(blob, Encoding.ASCII.GetBytes(curve));
                    WriteString(blob, point);
                    result.Fingerprint = Fingerprint(blob.ToArray());
                }
                result.KeyType = "ecdsa-sha2-" + curve;
                return true;
            }
        }

        private static bool IsEd25519Pkcs8(byte[]? der)
        {
            if (der == null) return false;
            byte[] oid = new byte[] { 0x06, 0x03, 0x2B, 0x65, 0x70 };
            return der.AsSpan().IndexOf(oid) >= 0;
        }

        private static void WriteString(Stream stream, byte[] value)
        {
            byte[] length = new byte[] { (byte)(value.Length >> 24), (byte)(value.Length >> 16), (byte)(value.Length >> 8), (byte)value.Length };
            stream.Write(length, 0, 4);
            stream.Write(value, 0, value.Length);
        }

        private static void WriteMpint(Stream stream, byte[] value)
        {
            int start = 0;
            while (start < value.Length - 1 && value[start] == 0) start++;
            bool pad = (value[start] & 0x80) != 0;
            byte[] encoded = new byte[value.Length - start + (pad ? 1 : 0)];
            Buffer.BlockCopy(value, start, encoded, pad ? 1 : 0, value.Length - start);
            WriteString(stream, encoded);
        }

        private static byte[] Base64Body(string body)
        {
            byte[]? decoded = Base64BodySafe(body);
            if (decoded == null) throw new ArgumentException("The key body is not valid base64.");
            return decoded;
        }

        private static byte[]? Base64BodySafe(string body)
        {
            StringBuilder builder = new StringBuilder();
            foreach (string line in body.Split('\n'))
            {
                string trimmed = line.Trim();
                if (trimmed.Length == 0 || trimmed.Contains(':')) continue;
                builder.Append(trimmed);
            }
            try
            {
                return Convert.FromBase64String(builder.ToString());
            }
            catch (FormatException)
            {
                return null;
            }
        }

        #endregion
    }
}
