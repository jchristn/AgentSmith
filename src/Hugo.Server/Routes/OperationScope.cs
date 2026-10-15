namespace Hugo.Server.Routes
{
    using System;
    using System.Collections.Generic;
    using Hugo.Core.Enums;

    /// <summary>
    /// The permission a route or MCP tool requires.
    /// </summary>
    public class OperationScope
    {
        /// <summary>
        /// HTTP method or MCP for tools.
        /// </summary>
        public string Method { get; set; } = String.Empty;

        /// <summary>
        /// Route path or tool name.
        /// </summary>
        public string Path { get; set; } = String.Empty;

        /// <summary>
        /// Resource type, or null for anonymous and handler-authorized routes.
        /// </summary>
        public ResourceTypeEnum? ResourceType { get; set; } = null;

        /// <summary>
        /// Operation, or null for anonymous and handler-authorized routes.
        /// </summary>
        public OperationTypeEnum? Operation { get; set; } = null;

        /// <summary>
        /// Authorization mode.
        /// </summary>
        public RouteAuthorizationEnum Authorization { get; set; } = RouteAuthorizationEnum.Scoped;

        /// <summary>
        /// OpenAPI tag.
        /// </summary>
        public string Tag { get; set; } = String.Empty;

        /// <summary>
        /// Summary.
        /// </summary>
        public string Summary { get; set; } = String.Empty;

        /// <summary>
        /// Success status codes the route declares in OpenAPI (for example 200, 201, 202, or 204). Empty when the route
        /// declares none (streams and proxies).
        /// </summary>
        public List<int> SuccessStatuses { get; set; } = new List<int>();
    }
}
