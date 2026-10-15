namespace Hugo.Connectors.Observability
{
    /// <summary>
    /// A rule paired with its group name, for listing.
    /// </summary>
    public class RuleRow
    {
        /// <summary>
        /// Group name.
        /// </summary>
        public string? Group { get; set; } = null;

        /// <summary>
        /// Rule.
        /// </summary>
        public PromRule Rule { get; set; } = new PromRule();
    }
}
