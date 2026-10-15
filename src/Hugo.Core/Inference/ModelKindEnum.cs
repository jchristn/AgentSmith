namespace Hugo.Core.Inference
{
    /// <summary>
    /// What a model endpoint serves.
    /// </summary>
    public enum ModelKindEnum
    {
        /// <summary>
        /// Chat and tool-chat completions.
        /// </summary>
        Inference = 0,

        /// <summary>
        /// Text embeddings.
        /// </summary>
        Embedding = 1
    }
}
