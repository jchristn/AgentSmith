namespace Hugo.Sdk.Enums
{
    /// <summary>
    /// Kind of secret material.
    /// </summary>
    public enum SecretKindEnum
    {
        /// <summary>
        /// Password.
        /// </summary>
        Password,

        /// <summary>
        /// Private key.
        /// </summary>
        PrivateKey,

        /// <summary>
        /// API token.
        /// </summary>
        ApiToken,

        /// <summary>
        /// Access key pair.
        /// </summary>
        AccessKeyPair,

        /// <summary>
        /// Service account JSON.
        /// </summary>
        ServiceAccountJson,

        /// <summary>
        /// Kubeconfig.
        /// </summary>
        Kubeconfig,

        /// <summary>
        /// Connection string.
        /// </summary>
        ConnectionString,

        /// <summary>
        /// Certificate.
        /// </summary>
        Certificate,

        /// <summary>
        /// Generic.
        /// </summary>
        Generic,

        /// <summary>
        /// Bearer token: sent exactly as Authorization: Bearer (field token).
        /// </summary>
        BearerToken,

        /// <summary>Bundled AES key material: key, optional salt and iv.</summary>
        AesKey
    }
}
