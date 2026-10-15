namespace Hugo.Sdk.Examples
{
    using System;
    using System.Collections.Generic;
    using System.Threading.Tasks;
    using Hugo.Sdk;
    using Hugo.Sdk.Enums;
    using Hugo.Sdk.Models;
    using Hugo.Sdk.Requests;
    using Hugo.Sdk.Responses;

    /// <summary>
    /// Quick start, mirroring the README: sign in, store a secret, create a resource that uses it, bind it to a new
    /// agent profile, preview the compiled instructions, dry-run a tool call, and clean up.
    /// Environment: HUGO_SDK_SERVER (default http://127.0.0.1:8800), HUGO_EMAIL, HUGO_PASSWORD, HUGO_TENANT.
    /// </summary>
    public static class Program
    {
        /// <summary>
        /// Entry point.
        /// </summary>
        /// <returns>Exit code.</returns>
        public static async Task<int> Main()
        {
            string server = Environment.GetEnvironmentVariable("HUGO_SDK_SERVER") ?? "http://127.0.0.1:8800";
            string email = Environment.GetEnvironmentVariable("HUGO_EMAIL") ?? "anderson@hugo.local";
            string password = Environment.GetEnvironmentVariable("HUGO_PASSWORD") ?? "password";
            string? tenant = Environment.GetEnvironmentVariable("HUGO_TENANT");

            using (HugoClient client = new HugoClient(server))
            {
                TokenResponse login = await client.LoginAsync(email, password, tenant).ConfigureAwait(false);
                if (login.MustChangePassword)
                {
                    // Advisory only: every route works. Pick a real password with client.ChangePasswordAsync(current, next).
                    Console.WriteLine("This account still has its seeded or reset password; consider changing it.");
                }
                TokenDetails me = await client.GetTokenDetailsAsync().ConfigureAwait(false);
                Console.WriteLine("Signed in to tenant " + client.TenantId + " as " + me.PrincipalName);

                SecretRequest secretRequest = new SecretRequest();
                secretRequest.Name = "quickstart-web-01-password";
                secretRequest.Kind = SecretKindEnum.Password;
                secretRequest.Values = new Dictionary<string, string> { { "password", "change-me" } };
                Secret secret = await client.Secrets.CreateAsync(secretRequest).ConfigureAwait(false);
                Console.WriteLine("Stored secret " + secret.Id + " with fields " + String.Join(", ", secret.Fields));

                try
                {
                    ResourceRequest resourceRequest = new ResourceRequest();
                    resourceRequest.Name = "web-01";
                    resourceRequest.ConnectorType = "ssh";
                    resourceRequest.Environment = "prod";
                    resourceRequest.Settings = new Dictionary<string, object?> { { "host", "web-01.example.com" }, { "port", 22 }, { "username", "deploy" }, { "authMethod", "Password" } };
                    ResourceSecretBinding slot = new ResourceSecretBinding();
                    slot.SlotName = "password";
                    slot.SecretId = secret.Id;
                    resourceRequest.SecretBindings = new List<ResourceSecretBinding> { slot };
                    Resource resource = await client.Resources.CreateAsync(resourceRequest).ConfigureAwait(false);

                    AgentProfileRequest profileRequest = new AgentProfileRequest();
                    profileRequest.Name = "quickstart";
                    profileRequest.DisplayName = "Quickstart";
                    AgentProfile profile = await client.AgentProfiles.CreateAsync(profileRequest).ConfigureAwait(false);
                    try
                    {
                        BindingRequest bind = new BindingRequest();
                        bind.ResourceId = resource.Id;
                        bind.Alias = "web-01";
                        bind.AccessMode = "ReadWriteWithApproval";
                        await client.AgentProfiles.CreateBindingAsync(profile.Id, bind).ConfigureAwait(false);

                        CompiledInstructions compiled = await client.AgentProfiles.GetCompiledInstructionsAsync(profile.Id, "Mcp").ConfigureAwait(false);
                        Console.WriteLine(compiled.Text);

                        ToolCallRequest call = new ToolCallRequest();
                        call.ProfileId = profile.Id;
                        call.Resource = "web-01";
                        call.ToolName = "ssh_exec";
                        call.Arguments = new { command = "uptime" };
                        ToolInvocationOutcome outcome = await client.ToolInvocations.DryRunAsync(call).ConfigureAwait(false);
                        Console.WriteLine("Dry run decision: " + outcome.Decision + " (" + outcome.Effect + ": " + outcome.EffectReason + ")");
                    }
                    finally
                    {
                        await client.AgentProfiles.DeleteAsync(profile.Id).ConfigureAwait(false);
                        await client.Resources.DeleteAsync(resource.Id).ConfigureAwait(false);
                    }
                }
                catch (HugoException e)
                {
                    Console.WriteLine("Hugo error " + e.StatusCode + " " + e.Code + ": " + e.Message + " (request " + e.RequestId + ")");
                    return 1;
                }
                finally
                {
                    await client.Secrets.DeleteAsync(secret.Id).ConfigureAwait(false);
                }
            }
            return 0;
        }
    }
}
