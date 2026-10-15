## {{this.alias}} ({{this.connectorDisplayName}}{{#if this.environment}}, environment: {{this.environment}}{{/if}}, access: {{this.accessMode}})
{{#if this.endpoint}}
Endpoint: {{this.endpoint}}
{{/if}}
{{#if this.description}}
{{this.description}}
{{/if}}
{{#if this.toolList}}
Tools: {{this.toolList}}
{{/if}}
{{#if this.readOnly}}
This resource is read-only for you. Write and Destructive calls will be rejected.
{{/if}}
{{#if this.approvalRequired}}
Write and Destructive calls on this resource wait for human approval.
{{/if}}

{{this.instructions}}
{{#if this.notes}}

Operator notes: {{this.notes}}
{{/if}}
