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
        /// Fields: <c>token</c>.
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
        Generic = 8
    }
}
