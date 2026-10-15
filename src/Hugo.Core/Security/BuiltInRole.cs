namespace Hugo.Core.Security
{
    using System;
    using System.Collections.Generic;

    /// <summary>
    /// A built-in role definition seeded at startup with a stable identifier.
    /// </summary>
    public class BuiltInRole
    {
        /// <summary>
        /// Stable role identifier.
        /// </summary>
        public string Id { get; set; } = String.Empty;

        /// <summary>
        /// Role name.
        /// </summary>
        public string Name { get; set; } = String.Empty;

        /// <summary>
        /// Description.
        /// </summary>
        public string Description { get; set; } = String.Empty;

        /// <summary>
        /// Permissions.
        /// </summary>
        public List<BuiltInPermission> Permissions { get; set; } = new List<BuiltInPermission>();
    }
}
