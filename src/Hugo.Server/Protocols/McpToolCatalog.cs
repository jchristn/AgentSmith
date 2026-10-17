namespace Hugo.Server.Protocols
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using Hugo.Core.Enums;

    /// <summary>
    /// The built-in MCP tools (plan section 14.3): meta tools, ask_agent_smith, and the management tools. Connector
    /// tools come from the connector registry. Thread-safe (immutable).
    /// </summary>
    public static class McpToolCatalog
    {
        #region Public-Members

        /// <summary>hugo_whoami.</summary>
        public static readonly string Whoami = "hugo_whoami";

        /// <summary>hugo_get_instructions.</summary>
        public static readonly string GetInstructions = "hugo_get_instructions";

        /// <summary>hugo_list_resources.</summary>
        public static readonly string ListResources = "hugo_list_resources";

        /// <summary>hugo_describe_resource.</summary>
        public static readonly string DescribeResource = "hugo_describe_resource";

        /// <summary>hugo_list_approvals.</summary>
        public static readonly string ListApprovals = "hugo_list_approvals";

        /// <summary>hugo_await_approval.</summary>
        public static readonly string AwaitApproval = "hugo_await_approval";

        /// <summary>ask_agent_smith.</summary>
        public static readonly string AskAgentSmith = "ask_agent_smith";

        /// <summary>hugo_admin_list_profiles.</summary>
        public static readonly string AdminListProfiles = "hugo_admin_list_profiles";

        /// <summary>hugo_admin_describe_profile.</summary>
        public static readonly string AdminDescribeProfile = "hugo_admin_describe_profile";

        /// <summary>hugo_admin_list_resources.</summary>
        public static readonly string AdminListResources = "hugo_admin_list_resources";

        /// <summary>hugo_admin_describe_resource.</summary>
        public static readonly string AdminDescribeResource = "hugo_admin_describe_resource";

        /// <summary>hugo_admin_list_bindings.</summary>
        public static readonly string AdminListBindings = "hugo_admin_list_bindings";

        /// <summary>hugo_admin_create_resource.</summary>
        public static readonly string AdminCreateResource = "hugo_admin_create_resource";

        /// <summary>hugo_admin_update_binding.</summary>
        public static readonly string AdminUpdateBinding = "hugo_admin_update_binding";

        #endregion

        #region Private-Members

        private static readonly List<McpToolDefinition> _All = Build();

        #endregion

        #region Public-Methods

        /// <summary>
        /// Every built-in tool.
        /// </summary>
        /// <returns>Definitions (a new list).</returns>
        public static List<McpToolDefinition> All()
        {
            return new List<McpToolDefinition>(_All);
        }

        /// <summary>
        /// A built-in tool by name.
        /// </summary>
        /// <param name="name">Name.</param>
        /// <returns>Definition or null.</returns>
        public static McpToolDefinition? Find(string? name)
        {
            return _All.FirstOrDefault(t => String.Equals(t.Name, name, StringComparison.Ordinal));
        }

        #endregion

        #region Private-Methods

        private static List<McpToolDefinition> Build()
        {
            List<McpToolDefinition> list = new List<McpToolDefinition>();
            list.AddRange(Hugo.Server.Services.AgentInstructionTools.Definitions());
            list.Add(Meta(Whoami, "Who am I", "Return the tenant, principal, agent profile (persona and how it addresses the operator), and a summary of the bound resources and their access modes.", Empty()));
            list.Add(Meta(GetInstructions, "Get instructions", "Return the compiled instructions of this agent profile (the same text as the MCP initialize instructions). Call it when your client did not show them.", Empty()));
            list.Add(Meta(ListResources, "List resources", "List the resources bound to this agent profile: alias (the value of each tool's resource argument), connector, environment, access mode, and operator notes.", Empty()));
            list.Add(Meta(DescribeResource, "Describe resource", "Return the full guidance for one bound resource: the connector's complete instructions, operator notes, durable knowledge, access mode, limits, and the tools you can use on it. Call it before working with a resource when the instructions only summarize it.",
                """{"type":"object","properties":{"resource":{"type":"string","description":"Alias of a bound resource (see hugo_list_resources)."}},"required":["resource"],"additionalProperties":false}"""));
            list.Add(Meta(ListApprovals, "List approvals", "List your pending and recent approvals (calls that wait for a human decision), newest first.",
                """{"type":"object","properties":{"status":{"type":"string","enum":["Pending","Approved","Rejected","Expired","Cancelled"],"description":"Only approvals with this status."},"maxResults":{"type":"integer","minimum":1,"maximum":100,"description":"Most approvals to return (default 25)."}},"additionalProperties":false}"""));
            list.Add(Meta(AwaitApproval, "Await approval", "Wait for a pending approval (from a tool result with status approval_pending) and return the original call's result once a human decides it. Returns approval_pending again if it is still undecided when the wait ends.",
                """{"type":"object","properties":{"approvalId":{"type":"string","description":"Approval identifier (apr_...)."},"waitSeconds":{"type":"integer","minimum":0,"maximum":600,"description":"Seconds to wait (default Approvals.McpWaitSeconds)."}},"required":["approvalId"],"additionalProperties":false}"""));

            McpToolDefinition ask = Meta(AskAgentSmith, "Ask Agent Smith", "Delegate a whole task to Hugo's own agent, which plans and runs the tool calls itself on this profile's resources and returns its final answer with a compact tool trace. Pass conversationId to continue a conversation.",
                """{"type":"object","properties":{"message":{"type":"string","minLength":1,"description":"The task or question."},"conversationId":{"type":"string","description":"Conversation to continue (from a previous answer)."}},"required":["message"],"additionalProperties":false}""");
            ask.Kind = McpToolKindEnum.Delegation;
            ask.ReadOnly = false;
            list.Add(ask);

            string search = "\"search\":{\"type\":\"string\",\"description\":\"Text to match.\"},\"maxResults\":{\"type\":\"integer\",\"minimum\":1,\"maximum\":100,\"description\":\"Most results (default 25).\"}";
            list.Add(Admin(AdminListProfiles, "List profiles", "List the tenant's agent profiles.", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read,
                "{\"type\":\"object\",\"properties\":{" + search + "},\"additionalProperties\":false}"));
            list.Add(Admin(AdminDescribeProfile, "Describe profile", "Return one agent profile (by name or id) with its bindings.", ResourceTypeEnum.AgentProfile, OperationTypeEnum.Read,
                """{"type":"object","properties":{"profile":{"type":"string","description":"Profile name or id."}},"required":["profile"],"additionalProperties":false}"""));
            list.Add(Admin(AdminListResources, "List resources (admin)", "List the tenant's resources (not only those bound to this profile).", ResourceTypeEnum.Resource, OperationTypeEnum.Read,
                "{\"type\":\"object\",\"properties\":{" + search + ",\"connectorType\":{\"type\":\"string\",\"description\":\"Only resources of this connector type.\"}},\"additionalProperties\":false}"));
            list.Add(Admin(AdminDescribeResource, "Describe resource (admin)", "Return one resource (by name or id) with its settings (secrets are never shown) and the profiles that bind it.", ResourceTypeEnum.Resource, OperationTypeEnum.Read,
                """{"type":"object","properties":{"resource":{"type":"string","description":"Resource name or id."}},"required":["resource"],"additionalProperties":false}"""));
            list.Add(Admin(AdminListBindings, "List bindings", "List the bindings of an agent profile (by name or id).", ResourceTypeEnum.Binding, OperationTypeEnum.Read,
                """{"type":"object","properties":{"profile":{"type":"string","description":"Profile name or id."}},"required":["profile"],"additionalProperties":false}"""));
            McpToolDefinition create = Admin(AdminCreateResource, "Create resource", "Create a resource. Settings are validated against the connector's settings schema; secrets are referenced by slot and secret id, never passed as values.", ResourceTypeEnum.Resource, OperationTypeEnum.Create,
                """{"type":"object","properties":{"name":{"type":"string","minLength":1},"connectorType":{"type":"string","minLength":1},"displayName":{"type":"string"},"description":{"type":"string"},"endpoint":{"type":"string"},"environment":{"type":"string"},"notes":{"type":"string"},"defaultAccessModeCeiling":{"type":"string","enum":["ReadOnly","ReadWriteWithApproval","ReadWrite"]},"settings":{"type":"object","description":"Connector settings by key."},"secretBindings":{"type":"array","items":{"type":"object","properties":{"slotName":{"type":"string"},"secretId":{"type":"string"}},"required":["slotName","secretId"]}}},"required":["name","connectorType"],"additionalProperties":false}""");
            create.ReadOnly = false;
            list.Add(create);
            McpToolDefinition update = Admin(AdminUpdateBinding, "Update binding", "Change the access mode (and optionally the destructive approval policy or enabled flag) of a binding of an agent profile.", ResourceTypeEnum.Binding, OperationTypeEnum.Update,
                """{"type":"object","properties":{"profile":{"type":"string","description":"Profile name or id."},"binding":{"type":"string","description":"Binding alias or id."},"accessMode":{"type":"string","enum":["ReadOnly","ReadWriteWithApproval","ReadWrite"]},"requireApprovalForDestructive":{"type":"boolean"},"enabled":{"type":"boolean"}},"required":["profile","binding","accessMode"],"additionalProperties":false}""");
            update.ReadOnly = false;
            list.Add(update);
            return list;
        }

        private static McpToolDefinition Meta(string name, string title, string description, string schema)
        {
            McpToolDefinition d = new McpToolDefinition();
            d.Name = name;
            d.Title = title;
            d.Description = description;
            d.InputSchemaJson = schema;
            d.Kind = McpToolKindEnum.Meta;
            d.ResourceType = ResourceTypeEnum.AgentProfile;
            d.Operation = OperationTypeEnum.Execute;
            return d;
        }

        private static McpToolDefinition Admin(string name, string title, string description, ResourceTypeEnum resourceType, OperationTypeEnum operation, string schema)
        {
            McpToolDefinition d = new McpToolDefinition();
            d.Name = name;
            d.Title = title;
            d.Description = description;
            d.InputSchemaJson = schema;
            d.Kind = McpToolKindEnum.Management;
            d.ResourceType = resourceType;
            d.Operation = operation;
            return d;
        }

        private static string Empty()
        {
            return "{\"type\":\"object\",\"properties\":{},\"additionalProperties\":false}";
        }

        #endregion
    }
}
