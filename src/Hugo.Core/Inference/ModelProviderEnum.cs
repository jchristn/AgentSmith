namespace Hugo.Core.Inference
{
    /// <summary>
    /// Model provider. Each value maps to a PolyPrompt client in <see cref="ModelClientFactory"/>.
    /// </summary>
    public enum ModelProviderEnum
    {
        /// <summary>
        /// Ollama (<c>/api/chat</c>, <c>/api/embed</c>). Inference and embeddings.
        /// </summary>
        Ollama = 0,

        /// <summary>
        /// OpenAI. Inference and embeddings.
        /// </summary>
        OpenAI = 1,

        /// <summary>
        /// Any server speaking the OpenAI chat completions and embeddings API (vLLM, LM Studio, llama.cpp, the
        /// Hugo test stub). Inference and embeddings.
        /// </summary>
        OpenAICompatible = 2,

        /// <summary>
        /// Anthropic Messages API. Inference only.
        /// </summary>
        Anthropic = 3,

        /// <summary>
        /// Google Gemini API. Inference and embeddings.
        /// </summary>
        Gemini = 4,

        /// <summary>
        /// Azure OpenAI. Inference and embeddings. Requires a deployment name.
        /// </summary>
        AzureOpenAI = 5,

        /// <summary>
        /// AWS Bedrock. Inference and embeddings. Requires a region and an access key ID; the API key is the
        /// secret access key.
        /// </summary>
        Bedrock = 6,

        /// <summary>
        /// Google Vertex AI. Inference and embeddings. Requires a project and a region; the API key is the
        /// service-account JSON.
        /// </summary>
        VertexAI = 7,

        /// <summary>
        /// Voyage AI. Embeddings only.
        /// </summary>
        VoyageAI = 8
    }
}
