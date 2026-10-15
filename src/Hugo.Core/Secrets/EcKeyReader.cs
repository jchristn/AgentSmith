namespace Hugo.Core.Secrets
{
    using System;
    using System.Formats.Asn1;

    /// <summary>
    /// Reads the named curve and public point from an unencrypted EC private key (SEC1 ECPrivateKey, or PKCS#8
    /// PrivateKeyInfo wrapping one) with an ASN.1 reader, so the SSH fingerprint does not depend on the platform's
    /// EC provider. Thread-safe (stateless).
    /// </summary>
    internal static class EcKeyReader
    {
        #region Private-Members

        private static readonly string _EcPublicKeyOid = "1.2.840.10045.2.1";

        #endregion

        #region Public-Methods

        /// <summary>
        /// Read the curve OID and public point.
        /// </summary>
        /// <param name="der">DER bytes of the key.</param>
        /// <param name="pkcs8">True for PKCS#8 PrivateKeyInfo, false for SEC1 ECPrivateKey.</param>
        /// <param name="curveOid">Curve OID, or null.</param>
        /// <param name="point">Uncompressed public point, or null.</param>
        /// <returns>True when both were found.</returns>
        public static bool TryRead(byte[] der, bool pkcs8, out string? curveOid, out byte[]? point)
        {
            curveOid = null;
            point = null;
            try
            {
                if (pkcs8)
                {
                    AsnReader outer = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
                    outer.ReadInteger();
                    AsnReader algorithm = outer.ReadSequence();
                    if (algorithm.ReadObjectIdentifier() != _EcPublicKeyOid) return false;
                    curveOid = algorithm.ReadObjectIdentifier();
                    byte[] inner = outer.ReadOctetString();
                    ReadSec1(inner, ref curveOid, out point);
                }
                else
                {
                    ReadSec1(der, ref curveOid, out point);
                }
                return curveOid != null && point != null;
            }
            catch (AsnContentException)
            {
                return false;
            }
        }

        #endregion

        #region Private-Methods

        private static void ReadSec1(byte[] der, ref string? curveOid, out byte[]? point)
        {
            point = null;
            AsnReader key = new AsnReader(der, AsnEncodingRules.DER).ReadSequence();
            key.ReadInteger();
            key.ReadOctetString();
            Asn1Tag parametersTag = new Asn1Tag(TagClass.ContextSpecific, 0, true);
            Asn1Tag publicKeyTag = new Asn1Tag(TagClass.ContextSpecific, 1, true);
            while (key.HasData)
            {
                Asn1Tag tag = key.PeekTag();
                if (tag.HasSameClassAndValue(parametersTag))
                {
                    AsnReader parameters = key.ReadSequence(parametersTag);
                    if (parameters.PeekTag().HasSameClassAndValue(Asn1Tag.ObjectIdentifier))
                    {
                        curveOid = parameters.ReadObjectIdentifier();
                    }
                    else
                    {
                        // Explicit curve parameters (written by some OpenSSL and LibreSSL builds): identify the curve by
                        // its field prime.
                        AsnReader explicitCurve = parameters.ReadSequence();
                        explicitCurve.ReadInteger();
                        AsnReader field = explicitCurve.ReadSequence();
                        field.ReadObjectIdentifier();
                        string prime = Convert.ToHexString(field.ReadIntegerBytes().ToArray()).TrimStart('0');
                        curveOid = prime switch
                        {
                            "FFFFFFFF00000001000000000000000000000000FFFFFFFFFFFFFFFFFFFFFFFF" => "1.2.840.10045.3.1.7",
                            "FFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFFEFFFFFFFF0000000000000000FFFFFFFF" => "1.3.132.0.34",
                            _ => prime.Length == 131 && prime.TrimStart('1').Trim('F').Length == 0 ? "1.3.132.0.35" : "explicit"
                        };
                    }
                }
                else if (tag.HasSameClassAndValue(publicKeyTag))
                {
                    AsnReader publicKey = key.ReadSequence(publicKeyTag);
                    point = publicKey.ReadBitString(out int _);
                }
                else
                {
                    key.ReadEncodedValue();
                }
            }
        }

        #endregion
    }
}
