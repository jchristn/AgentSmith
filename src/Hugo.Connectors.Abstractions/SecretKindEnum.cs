namespace Hugo.Connectors.Abstractions
{
    /// <summary>
    /// Kind of credential material held by a secret. Determines which fields the secret carries.
    /// </summary>
    public enum SecretKindEnum
    {
        /// <summary>
        /// Fields: <c>password</c>, optional <c>username</c>.
        /// </summary>
        Password = 0,

        /// <summary>
        /// Fields: <c>privateKey</c>, optional <c>passphrase</c>.
        /// </summary>
        PrivateKey = 1,

        /// <summary>
        /// Fields: <c>token</c>. An API token or key whose transport depends on the service (it may be a bearer token,
        /// a custom header such as <c>X-API-Key</c>, or something else). See <see cref="BearerToken"/>.
        /// </summary>
        ApiToken = 2,

        /// <summary>
        /// Fields: <c>accessKeyId</c>, <c>secretAccessKey</c>, optional <c>sessionToken</c>.
        /// </summary>
        AccessKeyPair = 3,

        /// <summary>
        /// Fields: <c>json</c> (for example a Google Cloud service account key).
        /// </summary>
        ServiceAccountJson = 4,

        /// <summary>
        /// Fields: <c>kubeconfig</c>.
        /// </summary>
        Kubeconfig = 5,

        /// <summary>
        /// Fields: <c>connectionString</c>.
        /// </summary>
        ConnectionString = 6,

        /// <summary>
        /// Fields: <c>certificate</c>, optional <c>privateKey</c>, optional <c>caCertificate</c>.
        /// </summary>
        Certificate = 7,

        /// <summary>
        /// Any named fields.
        /// </summary>
        Generic = 8,

        /// <summary>
        /// Fields: <c>token</c>. A token sent exactly as <c>Authorization: Bearer &lt;token&gt;</c>. Use
        /// <see cref="ApiToken"/> for tokens a service expects somewhere else (a custom header, a query parameter, or a
        /// different scheme).
        /// </summary>
        BearerToken = 9,

        /// <summary>
        /// Fields: <c>key</c>, optional <c>salt</c> and <c>iv</c>. Encoding and explicit derivation are configured on the cryptography resource.
        /// </summary>
        AesKey = 10
    }
}
