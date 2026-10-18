namespace Hugo.Sdk.Documents
{
    using System;
    using System.IO;
    using System.Text;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>A local managed-document import and delivery example using an isolated test tenant.</summary>
    public static class Program
    {
        /// <summary>Import a local UTF-8 file, attach it in both modes, preview and remove example records.</summary>
        /// <param name="args">Optional local Markdown or text filename.</param>
        /// <returns>Process exit code.</returns>
        public static async Task<int> Main(string[] args)
        {
            CancellationToken token = CancellationToken.None;
            string server = Environment.GetEnvironmentVariable("HUGO_SDK_SERVER") ?? "http://127.0.0.1:8800";
            string email = Environment.GetEnvironmentVariable("HUGO_EMAIL") ?? throw new InvalidOperationException("Set HUGO_EMAIL for an isolated test tenant.");
            string password = Environment.GetEnvironmentVariable("HUGO_PASSWORD") ?? throw new InvalidOperationException("Set HUGO_PASSWORD for that test user.");
            string tenant = Environment.GetEnvironmentVariable("HUGO_TENANT") ?? "default";
            string path = args.Length > 0 ? args[0] : Path.Combine(AppContext.BaseDirectory, "LOG_MESSAGE_STRUCTURE.md");
            byte[] bytes = await File.ReadAllBytesAsync(path, token).ConfigureAwait(false);
            string markdown = new UTF8Encoding(false, true).GetString(bytes);
            string suffix = Guid.NewGuid().ToString("N").Substring(0, 10);
            string name = Path.GetFileNameWithoutExtension(path) + "-" + suffix + Path.GetExtension(path);
            AgentProfile? profile = null;
            PromptDocument? document = null;
            using (HugoClient client = new HugoClient(server))
            {
                await client.LoginAsync(email, password, tenant, token).ConfigureAwait(false);
                try
                {
                    AgentProfileRequest profileRequest = new AgentProfileRequest();
                    profileRequest.Name = "document-example-" + suffix;
                    profile = await client.AgentProfiles.CreateAsync(profileRequest, token).ConfigureAwait(false);
                    PromptDocumentRequest create = new PromptDocumentRequest();
                    create.Name = name;
                    create.Description = "Read before analyzing synthetic application logs.";
                    create.Markdown = markdown;
                    create.Visibility = "Private";
                    create.Active = true;
                    document = await client.PromptDocuments.CreateAsync(create, token).ConfigureAwait(false);
                    AgentPromptDocumentList initial = await client.AgentPromptDocuments.ListAsync(profile.Id, token).ConfigureAwait(false);
                    AgentPromptDocumentRequest attach = new AgentPromptDocumentRequest();
                    attach.DocumentId = document.Id;
                    attach.DeliveryMode = "Reference";
                    attach.RevisionPolicy = "Latest";
                    attach.Enabled = true;
                    attach.AllowPublicAccess = false;
                    attach.ExpectedDocumentSetVersion = initial.DocumentSetVersion;
                    AgentPromptDocument attachment = await client.AgentPromptDocuments.CreateAsync(profile.Id, attach, token).ConfigureAwait(false);
                    AgentPromptDocumentList attached = await client.AgentPromptDocuments.ListAsync(profile.Id, token).ConfigureAwait(false);
                    Console.WriteLine("Attached document count: " + attached.Items.Count);
                    PromptDocumentPreviewRequest previewRequest = new PromptDocumentPreviewRequest();
                    previewRequest.Audience = "Authenticated";
                    PromptDocumentPreview reference = await client.AgentPromptDocuments.PreviewAsync(profile.Id, previewRequest, token).ConfigureAwait(false);
                    Console.WriteLine("Reference manifest count: " + reference.Manifest.Count + "; readiness issues: " + reference.Issues.Count);
                    AgentPromptDocumentRequest update = new AgentPromptDocumentRequest();
                    update.ExpectedVersion = attachment.Version;
                    update.ExpectedDocumentSetVersion = attached.DocumentSetVersion;
                    update.DeliveryMode = "Inline";
                    attachment = await client.AgentPromptDocuments.UpdateAsync(profile.Id, attachment.Id, update, token).ConfigureAwait(false);
                    PromptDocumentPreview inline = await client.AgentPromptDocuments.PreviewAsync(profile.Id, previewRequest, token).ConfigureAwait(false);
                    Console.WriteLine("Inline bytes: " + inline.InlineBytes + "; estimated tokens: " + inline.EstimatedTokens);
                    if (Path.GetFileName(path) == "LOG_MESSAGE_STRUCTURE.md" && !inline.CompiledText.Contains("Synthetic application log schema", StringComparison.Ordinal))
                        throw new InvalidOperationException("Inline preview omitted the example content.");
                    AgentPromptDocumentList final = await client.AgentPromptDocuments.ListAsync(profile.Id, token).ConfigureAwait(false);
                    await client.AgentPromptDocuments.DeleteAsync(profile.Id, attachment.Id, attachment.Version, final.DocumentSetVersion, token).ConfigureAwait(false);
                }
                finally
                {
                    try
                    {
                        if (profile != null) await client.AgentProfiles.DeleteAsync(profile.Id, token).ConfigureAwait(false);
                    }
                    finally
                    {
                        try
                        {
                            if (document != null)
                            {
                                PromptDocument current = await client.PromptDocuments.GetAsync(document.Id, token).ConfigureAwait(false);
                                await client.PromptDocuments.DeleteAsync(document.Id, current.Version, token).ConfigureAwait(false);
                            }
                        }
                        finally { await client.LogoutAsync(token).ConfigureAwait(false); }
                    }
                }
            }
            return 0;
        }
    }
}
