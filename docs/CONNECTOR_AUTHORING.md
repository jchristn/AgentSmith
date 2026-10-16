# Writing a Hugo connector

A connector teaches Hugo how to talk to one family of systems. SSH, S3, PostgreSQL, and Papertrail are all connectors, and none of them is special: the server only knows the contract in `src/Hugo.Connectors.Abstractions`. If you can implement two interfaces, you can add a new service to every agent profile, every MCP client, and the dashboard without touching the core.

This guide walks through the contract, then builds a small connector end to end.

## The shape of a connector

A connector is one class implementing `IConnector`. It owns three things.

**A descriptor.** `ConnectorDescriptor` says what the connector is: its `Type` (a stable identifier such as `ssh` or `azure-blob`), its `Family` (the tool name prefix, shared by connectors that expose the same tools, such as all four SQL engines under `sql`), a display name, a category, an icon key, the settings schema, the secret slots, and the default instructions an LLM receives when a resource of this type is attached to a profile.

**Tools.** Each `IConnectorTool` is one operation, named `family_operation` in snake case (`sql_query`, `storage_list`). A tool has a JSON Schema for its arguments, a description written for an LLM, a base effect, and two methods: `Classify` and `ExecuteAsync`.

**Validation and a health probe.** `ValidateAsync` checks rules the schema cannot express ("either a password or a private key"). `TestConnectionAsync` opens a connection and runs something cheap and read-only. The dashboard's Test Connection button and the background health sweep both call it.

You never declare the `resource` argument in a tool schema. Hugo adds it, and for each caller it lists only the aliases that caller's profile has bound for your family.

## Settings and secrets

Settings are plain values: hosts, ports, limits, allow lists. Declare each one with a `SettingDescriptor`: key, value kind (`String`, `Text`, `Integer`, `Boolean`, `StringList`, `Enum`), whether it is required, a default, a range, allowed values, a pattern, and a group for the form. Hugo stores settings in a structured table, validates them against your schema before your code ever sees them, and renders the dashboard form from the same schema. At run time you read them through `ResourceSettings`, which gives typed getters with fallbacks and clamping.

Credentials never go in settings. Declare a `SecretSlotDescriptor` for each credential you need (`password`, `privateKey`, `apiToken`) and the secret kinds it accepts. Operators store secrets once in Hugo's encrypted vault and bind them to slots, so one AWS key pair can serve several resources. At run time you read plaintext through `SecretAccessor.Get(slot, field)`. The accessor exists only for the duration of one call and is cleared afterwards. Never log, return, or cache a value you read from it. Hugo scrubs every resolved secret value out of tool output as a backstop, but the backstop is not an excuse.

## Effects: the most important method you will write

Every call is classified as `Read`, `Write`, or `Destructive` before it runs. Hugo compares that effect with the binding's access mode and decides whether to allow the call, deny it, or hold it for a human. Your `Classify` method is where that judgment lives.

For tools whose effect never varies, return the descriptor's `BaseEffect`. `ConnectorToolBase` does this for you. For general-purpose tools the effect depends on the arguments. `sql_query` parses the statement, and `ssh_exec` inspects the command line. These tools set `ClassifiedPerCall = true` and look at the arguments.

Three rules apply:

1. **Fail closed.** If you cannot understand the input, classify it as `Destructive`. A false alarm costs one approval click. A false "Read" can cost a production database.
2. **Classify by the most severe part.** A pipeline, a multi-statement batch, or a CTE that modifies data takes the severity of its worst segment.
3. **Write a useful `ApprovalSummary`.** It is the sentence a human reads before clicking Approve, for example `Run "systemctl restart nginx" on web-01`. Name the exact action and the exact target, and never include secrets.

Use `EffectClassification.Reject(reason)` for calls that must never run (a command matching a deny pattern). Set `AlwaysRequireApproval` for calls that need a human even when the operator has opted out of destructive approvals, such as terminating cloud instances.

Classifiers are pure functions. Put them in their own class and test them with a table of cases. The built-in connectors carry between 150 and 200 cases for their general-purpose tools.

## Executing calls

`ExecuteAsync` receives a `ConnectorInvocationContext` and validated `ToolArguments`. The context carries the resolved resource, the binding alias, the access mode, the output limits, the timeout, and a session pool.

- **Honor the cancellation token.** Hugo cancels it when the timeout elapses or the caller goes away.
- **Bound your output** with `context.Limits`. `OutputLimiter.Head` and `OutputLimiter.Tail` truncate by bytes and lines and mark the result as truncated, and `TextTable.Render` formats rows compactly for an LLM.
- **Reuse connections** through `context.Resource.Sessions.GetOrCreateAsync(context.Resource.SessionKey(), ...)`. The key includes a hash of the resource's configuration, so an edited resource never reuses a stale session.
- **Report expected failures** as `ToolResult.Error(code, message)` or by throwing `ConnectorException`. Use stable codes: `connection_failed`, `auth_failed`, `timeout`, `not_found`, `permission_denied`, `invalid_argument`, `rate_limited`. A failed call is a normal result the LLM can reason about, not a server error.
- **Use access mode for defense in depth** where the target supports it. For example, the SQL connectors open read-only transactions for ReadOnly bindings.

## Default instructions

Ship a Markdown file as an embedded resource (`Instructions/connector.<type>.instructions.md`) and load it into `Descriptor.DefaultInstructions` with `EmbeddedText.Read`. Hugo seeds it into the Global prompt layer, where operators can edit it and reset it.

Write it for the calling model, not for a human. Cover what the service is, which tool to reach for first, a safe investigation order, how limits and paging work, and what never to do. Keep it short enough that eight attached resources still fit comfortably in a context window.

## A worked example

This minimal connector reports the time on an HTTP endpoint's `Date` header. It has one setting, no secrets, and one Read tool.

```csharp
namespace Example.Connectors.Clock
{
    using System;
    using System.Collections.Generic;
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Reads the Date header of an HTTP endpoint.
    /// </summary>
    public class ClockConnector : IConnector
    {
        /// <inheritdoc />
        public ConnectorDescriptor Descriptor { get; }

        /// <inheritdoc />
        public IReadOnlyList<IConnectorTool> Tools { get; }

        /// <summary>
        /// Instantiate.
        /// </summary>
        public ClockConnector()
        {
            ConnectorSettingsSchema schema = new ConnectorSettingsSchema()
                .Add(new SettingDescriptor("url", SettingValueKindEnum.String, "URL") { Required = true, ShowInSummary = true });

            Descriptor = new ConnectorDescriptor
            {
                Type = "clock",
                DisplayName = "HTTP clock",
                Category = ConnectorCategoryEnum.Other,
                SettingsSchema = schema,
                DefaultInstructions = "Use clock_now to read the server time of the endpoint."
            };
            Tools = new List<IConnectorTool> { new ClockNowTool() };
        }

        /// <inheritdoc />
        public Task<ConnectorValidationResult> ValidateAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            ConnectorValidationResult result = new ConnectorValidationResult();
            if (!Uri.TryCreate(resource.Settings.GetString("url"), UriKind.Absolute, out Uri? _))
                result.Add("url", "invalid_format", "Enter an absolute URL.");
            return Task.FromResult(result);
        }

        /// <inheritdoc />
        public async Task<ConnectorHealthResult> TestConnectionAsync(ConnectorResourceContext resource, CancellationToken token = default)
        {
            DateTime started = DateTime.UtcNow;
            try
            {
                using (HttpClient client = new HttpClient())
                using (HttpResponseMessage response = await client.GetAsync(resource.Settings.GetRequiredString("url"), token).ConfigureAwait(false))
                {
                    return ConnectorHealthResult.Ok("HTTP " + (int)response.StatusCode, (DateTime.UtcNow - started).TotalMilliseconds);
                }
            }
            catch (HttpRequestException e)
            {
                return ConnectorHealthResult.Fail("connection_failed", e.Message, (DateTime.UtcNow - started).TotalMilliseconds);
            }
        }
    }
}
```

The tool lives in its own file. One class per file is a hard rule in this repository.

```csharp
namespace Example.Connectors.Clock
{
    using System.Net.Http;
    using System.Threading;
    using System.Threading.Tasks;
    using Hugo.Connectors.Abstractions;

    /// <summary>
    /// Returns the endpoint's Date header.
    /// </summary>
    public class ClockNowTool : ConnectorToolBase
    {
        /// <summary>
        /// Instantiate.
        /// </summary>
        public ClockNowTool() : base(new ToolDescriptor
        {
            Name = "clock_now",
            Title = "Read server time",
            Description = "Returns the Date header reported by the endpoint. Use it to check clock skew.",
            BaseEffect = EffectEnum.Read
        })
        {
        }

        /// <inheritdoc />
        public override async Task<ToolResult> ExecuteAsync(ConnectorInvocationContext context, ToolArguments arguments, CancellationToken token = default)
        {
            using (HttpClient client = new HttpClient { Timeout = context.Timeout })
            using (HttpResponseMessage response = await client.GetAsync(context.Resource.Settings.GetRequiredString("url"), token).ConfigureAwait(false))
            {
                return ToolResult.FromText(response.Headers.Date?.ToString("o") ?? "The endpoint sent no Date header.");
            }
        }
    }
}
```

## Built-in connector or plugin

A built-in connector lives in `src/Hugo.Connectors.<Family>` and is registered in `ConnectorRegistrations.cs` in the server. A plugin is a separate assembly that references only `Hugo.Connectors.Abstractions`. Drop it into `plugins/<name>/` and enable `Connectors.EnablePlugins` (a restart-required, audited setting). Hugo loads each plugin into its own collectible load context and discovers every public `IConnector` with a parameterless constructor. A plugin whose type collides with a built-in is refused, and the refusal shows under Settings, Plugins.

Plugins run with the server's privileges, so only load code you would be willing to put in the image.

## Checklist before you call it done

1. Its own project (or plugin) referencing only `Hugo.Connectors.Abstractions` plus client libraries.
2. A descriptor with settings schema, secret slots, category, and icon key.
3. Every tool has an input schema, an LLM-facing description, a base effect, and a classifier where effects vary.
4. `TestConnectionAsync` performs a cheap read-only probe.
5. Default instructions embedded and loaded.
6. A testbed target in `docker/testbed/` or an in-process stub in `src/Test.Shared`.
7. A Touchstone suite covering settings validation, test connection, every tool, the classifier table, output limits, timeout, cancellation, and secret scrubbing.
8. Telemetry labels (the pipeline records `hugo_tool_*` metrics and a `<connector> <tool>` span automatically; add connector-specific spans only for long internal steps).
9. A row in `docs/CONNECTORS.md`, a section in `MCP_API.md`, a Postman dry-run example, and a CHANGELOG entry.
10. Registration in `ConnectorRegistrations.cs` or a plugin README.

## Shared resource context

Registration adds `resource_get_context` and `resource_update_context` to every connector, including plugins. Those names are reserved; connectors must not declare their own tools with those names. The wrapper preserves descriptors, connection validation, health checks, and optional `IRemoteInstructionsProvider` capability.

The shared tools use the resource-bound knowledge store and normal invocation authorization. Embedded integrations supplying a custom store should implement `IVersionedResourceKnowledgeStore` for atomic writes; the generic update tool refuses a store that cannot check versions atomically. Existing SQL context tools retain compatibility with legacy nonversioned stores. See [resource context](RESOURCE_CONTEXT.md).
