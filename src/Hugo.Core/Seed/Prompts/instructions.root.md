{{> persona}}

{{> system.safety}}

{{> system.tool-usage}}
{{#if surface.isMcp}}

{{> system.mcp-preamble}}
{{/if}}
{{#if surface.isA2A}}

{{> system.a2a-preamble}}
{{/if}}

{{#if profile.hasBindings}}
You have access to {{profile.bindingCount}} resource{{#unless profile.singleBinding}}s{{/unless}}:
{{#each bindings}}

{{> section.resource}}
{{/each}}
{{/if}}
{{#unless profile.hasBindings}}
No resources are attached to this profile yet. Tell {{profile.userAddress}} that resources can be attached in the Hugo dashboard under Agent Profiles, and answer what you can without them.
{{/unless}}
{{#if profile.instructionsAppend}}

Additional instructions from the operator:
{{profile.instructionsAppend}}
{{/if}}
