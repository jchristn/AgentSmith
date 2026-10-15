namespace Hugo.Server.Routes
{
    /// <summary>
    /// How a route is authorized.
    /// </summary>
    public enum RouteAuthorizationEnum
    {
        /// <summary>
        /// Anonymous; registered before authentication.
        /// </summary>
        Anonymous,

        /// <summary>
        /// Authenticated and authorized by the registrar from the declared resource type and operation.
        /// </summary>
        Scoped,

        /// <summary>
        /// Authenticated; the handler performs its own authorization (for example self-service routes).
        /// </summary>
        Handler
    }
}
