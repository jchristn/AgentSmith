"""Domain routes: connectors, resources, knowledge, agent profiles, bindings, tool invocations, approvals, lockdown,
prompt layers, model endpoints (filters, health history, draft validation), list filters, and the tenant override.
Conversations, chat, ask, and analytics are in test_chat.py."""

from datetime import datetime, timedelta, timezone

import pytest

from hugo_sdk import HugoError

from conftest import ADMIN_TENANT, new_async_client, unique

CLOSED = "http://127.0.0.1:9"


def _http_resource(client, name=None):
    return client.resources.create({"name": name or unique("api"), "connectorType": "http", "settings": {"baseUrl": CLOSED}})


def _call(profile_id, method, path="/"):
    return {"profile_id": profile_id, "resource": "api", "arguments": {"method": method, "path": path}}


@pytest.fixture
def resource(client):
    r = _http_resource(client)
    yield r
    try:
        client.resources.delete(r.id)
    except HugoError:
        pass


@pytest.fixture
def profile(client):
    p = client.agent_profiles.create({"name": unique("profile"), "displayName": "SDK profile"})
    yield p
    try:
        client.agent_profiles.delete(p.id)
    except HugoError:
        pass


def _bind(client, profile, resource, mode):
    return client.agent_profiles.create_binding(profile.id, {"resourceId": resource.id, "alias": "api", "accessMode": mode})


def test_connectors_list_and_http(client):
    connectors = client.connectors.list()
    assert any(c.type == "http" for c in connectors) and len(connectors) > 5
    http = client.connectors.get("http")
    assert http.type == "http" and http.category == "Http"
    base = next(s for s in http.settings if s.key == "baseUrl")
    assert base.required
    assert any(t.name == "http_request" for t in http.tools)


def test_resources_crud_filter_test_duplicate(client, resource):
    assert client.resources.get(resource.id).connector_type == "http"
    updated = client.resources.update(resource.id, {"name": resource.name, "connectorType": "http", "notes": "sdk notes",
                                                    "settings": {"baseUrl": CLOSED}})
    assert updated.notes == "sdk notes"
    assert any(r.id == resource.id for r in client.resources.list(connector_type="http").objects)
    assert not any(r.id == resource.id for r in client.resources.list(connector_type="ssh").objects)
    tested = client.resources.test(resource.id)
    assert tested.success is False and tested.error_code and isinstance(tested.validation_errors, list)
    assert client.resources.get(resource.id).last_test_result is False
    unsaved = client.resources.test_unsaved({"name": "unsaved", "connectorType": "http", "settings": {"baseUrl": CLOSED}})
    assert unsaved.success is False and isinstance(unsaved.details, dict)
    copy = client.resources.duplicate(resource.id, unique("copy"))
    try:
        assert copy.id != resource.id and copy.connector_type == "http"
    finally:
        client.resources.delete(copy.id)
    assert client.resources.referrers(resource.id) == []


def test_resource_filters_environment_and_health(client):
    env = unique("env")
    tested = client.resources.create({"name": unique("tested"), "connectorType": "http", "environment": env, "settings": {"baseUrl": CLOSED}})
    untested = client.resources.create({"name": unique("untested"), "connectorType": "http", "environment": env, "settings": {"baseUrl": CLOSED}})
    try:
        client.resources.test(tested.id)
        in_env = client.resources.list(environment=env, max_results=1000)
        assert sorted(r.id for r in in_env.objects) == sorted([tested.id, untested.id])
        unhealthy = client.resources.list(environment=env, health="Unhealthy", max_results=1000)
        assert [r.id for r in unhealthy.objects] == [tested.id]
        unknown = client.resources.list(environment=env, health="Unknown", max_results=1000)
        assert [r.id for r in unknown.objects] == [untested.id]
        assert client.resources.list(environment=env, health="Healthy").objects == []
        with pytest.raises(HugoError) as err:
            client.resources.list(health="Sick")
        assert err.value.status == 400 and err.value.code == "ValidationFailed"
    finally:
        client.resources.delete(tested.id)
        client.resources.delete(untested.id)


def test_resource_invalid_create(client):
    with pytest.raises(HugoError) as err:
        client.resources.create({"name": unique("bad"), "connectorType": "http", "settings": {}})
    assert err.value.status == 400 and err.value.code


def test_resource_knowledge(client, resource):
    first = client.resources.put_knowledge(resource.id, "service", "Checkout runs here.", key="notes")
    assert first.version == 1
    second = client.resources.put_knowledge(resource.id, "service", "Checkout runs on web-01.", key="notes")
    assert second.version == 2
    root = client.resources.put_knowledge(resource.id, "service", "Service root")
    assert root.content == "Service root" and root.key == "_root"
    assert client.resources.get_knowledge(resource.id, "service", "_root").content == "Service root"
    assert len(client.resources.knowledge(resource.id)) == 2
    assert len(client.resources.knowledge_scope(resource.id, "service")) == 2
    versions = client.resources.knowledge_versions(resource.id, "service", "notes")
    assert [v.version for v in versions][:2] == [2, 1]
    client.resources.delete_knowledge(resource.id, "service", "notes")
    client.resources.delete_knowledge(resource.id, "service")
    assert client.resources.knowledge(resource.id) == []


def test_agent_profiles_crud_card_compiled_tools(client, profile, resource):
    assert profile.persona_name == "Agent Smith" and profile.user_address == "Mr. Anderson"
    assert client.agent_profiles.get(profile.id).name == profile.name
    updated = client.agent_profiles.update(profile.id, {"name": profile.name, "description": "sdk"})
    assert updated.description == "sdk"
    assert any(p.id == profile.id for p in client.agent_profiles.list().objects)
    assert client.agent_profiles.get(profile.id).binding_count == 0
    _bind(client, profile, resource, "ReadOnly")
    assert client.agent_profiles.get(profile.id).binding_count == 1
    listed = next(p for p in client.agent_profiles.list(max_results=1000).objects if p.id == profile.id)
    assert listed.binding_count == 1
    card = client.agent_profiles.card(profile.id)
    assert "/v1.0/a2a/" in card.url and isinstance(card.skills, list)
    compiled = client.agent_profiles.compiled_instructions(profile.id, surface="Mcp")
    assert compiled.surface == "Mcp" and "Agent Smith" in compiled.text
    text = client.agent_profiles.compiled_instructions_text(profile.id, surface="Mcp")
    assert isinstance(text, str) and "Agent Smith" in text
    catalog = client.agent_profiles.tools(profile.id, surface="Mcp")
    assert catalog.profile_id == profile.id and any(t.name == "http_request" for t in catalog.tools)
    copy = client.agent_profiles.duplicate(profile.id, unique("dup"))
    try:
        assert copy.id != profile.id
    finally:
        client.agent_profiles.delete(copy.id)


def test_bindings_crud_tool_rules_referrers(client, profile, resource):
    binding = _bind(client, profile, resource, "ReadOnly")
    assert binding.alias == "api" and binding.access_mode == "ReadOnly"
    assert client.agent_profiles.get_binding(profile.id, binding.id).id == binding.id
    updated = client.agent_profiles.update_binding(profile.id, binding.id, {"resourceId": resource.id, "alias": "api", "accessMode": "ReadWriteWithApproval"})
    assert updated.access_mode == "ReadWriteWithApproval"
    assert any(b.id == binding.id for b in client.agent_profiles.list_bindings(profile.id))
    with_rules = client.agent_profiles.set_tool_rules(profile.id, binding.id, [{"toolName": "http_request", "rule": "RequireApproval"}])
    assert with_rules.tool_rules and with_rules.tool_rules[0].rule == "RequireApproval"
    rules = client.agent_profiles.get_tool_rules(profile.id, binding.id)
    assert [r.tool_name for r in rules] == ["http_request"]
    referrers = client.resources.referrers(resource.id)
    assert any(r.binding_id == binding.id and r.agent_profile_id == profile.id for r in referrers)
    client.agent_profiles.delete_binding(profile.id, binding.id)
    assert client.agent_profiles.list_bindings(profile.id) == []


def test_dry_run_read_only_binding(client, profile, resource):
    _bind(client, profile, resource, "ReadOnly")
    get = client.tool_invocations.dry_run("http_request", **_call(profile.id, "GET"))
    assert get.dry_run and get.decision == "Allowed" and get.effect == "Read"
    delete = client.tool_invocations.dry_run("http_request", **_call(profile.id, "DELETE", "/x"))
    assert delete.decision == "Denied" and delete.effect == "Destructive" and delete.decision_code


def test_invoke_approval_reject_and_approve(client, profile, resource):
    _bind(client, profile, resource, "ReadWriteWithApproval")
    pending = client.tool_invocations.invoke("http_request", **_call(profile.id, "POST", "/x"))
    assert pending.decision == "ApprovalRequired" and pending.approval_id and pending.approval_status == "Pending"
    listed = client.approvals.list(status="Pending")
    assert any(a.id == pending.approval_id for a in listed.objects)
    now = datetime.now(timezone.utc)
    window = client.approvals.list(status="Pending", effect="Write", from_utc=now - timedelta(minutes=10), to_utc=now + timedelta(minutes=10))
    assert any(a.id == pending.approval_id for a in window.objects)
    assert all(a.effect == "Write" for a in window.objects)
    assert not any(a.id == pending.approval_id for a in client.approvals.list(effect="Destructive").objects)
    assert not any(a.id == pending.approval_id for a in client.approvals.list(from_utc=now + timedelta(hours=1)).objects)
    assert not any(a.id == pending.approval_id for a in client.approvals.list(to_utc=now - timedelta(hours=1)).objects)
    with pytest.raises(HugoError) as err:
        client.approvals.list(effect="Spicy")
    assert err.value.status == 400 and err.value.code == "ValidationFailed"
    waited = client.approvals.await_decision(pending.approval_id, timeout=1, poll_interval=0.25)
    assert waited.status == "Pending" and not waited.is_decided
    rejected = client.approvals.reject(pending.approval_id, note="not now")
    assert rejected.status == "Rejected" and rejected.decision_note == "not now"

    second = client.tool_invocations.invoke("http_request", **_call(profile.id, "POST", "/y"))
    approved = client.approvals.approve(second.approval_id, note="go")
    assert approved.status == "Approved"
    detail = client.approvals.get(second.approval_id)
    assert detail.invocation is not None and detail.invocation.status != "Pending"
    invocations = client.tool_invocations.list(agentProfileId=profile.id)
    assert len(invocations.objects) >= 2
    now = datetime.now(timezone.utc)
    writes = client.tool_invocations.list(effect="Write", from_utc=now - timedelta(minutes=10), to_utc=now + timedelta(minutes=10),
                                          agentProfileId=profile.id)
    assert len(writes.objects) >= 2 and all(i.effect == "Write" for i in writes.objects)
    assert client.tool_invocations.list(effect="Read", agentProfileId=profile.id).objects == []
    assert client.tool_invocations.list(to_utc=now - timedelta(hours=1), agentProfileId=profile.id).objects == []
    assert client.tool_invocations.list(since=now + timedelta(hours=1), agentProfileId=profile.id).objects == []
    with pytest.raises(HugoError) as err:
        client.tool_invocations.list(effect="Spicy")
    assert err.value.status == 400 and err.value.code == "ValidationFailed"
    assert client.tool_invocations.get(second.invocation_id).tool_name == "http_request"
    summary = client.tool_invocations.summary()
    assert summary.total >= 2 and any(c.status == "http_request" for c in summary.by_tool)


def test_direct_resource_invoke_cancel(client, resource):
    outcome = client.resources.invoke(resource.id, "http_request", {"method": "POST", "path": "/x"})
    assert outcome.decision == "ApprovalRequired" and outcome.approval_id
    cancelled = client.approvals.cancel(outcome.approval_id, note="changed my mind")
    assert cancelled.status == "Cancelled"
    with pytest.raises(HugoError) as err:
        client.approvals.approve(outcome.approval_id)
    assert err.value.status == 409


def test_lockdown(client, harness, profile, resource):
    _bind(client, profile, resource, "ReadWriteWithApproval")
    pending = client.tool_invocations.invoke("http_request", **_call(profile.id, "POST", "/z"))
    assert client.lockdown.get().lockdown is False
    on = client.lockdown.enable("sdk harness")
    try:
        assert on.lockdown and on.approvals_cancelled >= 1
        assert client.approvals.get(pending.approval_id).status == "Cancelled"
        assert client.tool_invocations.dry_run("http_request", **_call(profile.id, "POST", "/z")).decision == "Denied"
        assert client.tool_invocations.dry_run("http_request", **_call(profile.id, "GET")).decision == "Allowed"
        assert client.tenants.get(harness.tenant_id).lockdown is True
    finally:
        off = client.lockdown.disable()
        assert off.lockdown is False


def test_prompt_layers(client):
    key = "persona.agent-smith"
    views = client.prompt_layers.list()
    assert any(v.key == key for v in views)
    saved = client.prompt_layers.save(key, "You are {{profile.personaName}}, Python SDK edition.", change_note="sdk python")
    try:
        assert saved.active_layer == "Tenant" and saved.tenant is not None and saved.tenant.exists
        versions = client.prompt_layers.versions(key, "Tenant")
        assert versions[0].change_note == "sdk python"
        diff = client.prompt_layers.diff(key, "Tenant")
        assert any(line.op == "add" for line in diff)
        builtin = client.prompt_layers.builtin(key)
        assert builtin.content == saved.built_in_content
        with pytest.raises(HugoError) as err:
            client.prompt_layers.save(key, "global attempt", layer="Global")
        assert err.value.status == 403
    finally:
        reset = client.prompt_layers.reset(key, "Tenant")
    assert reset.active_layer != "Tenant"
    assert client.prompt_layers.get(key).active_layer != "Tenant"
    rendered = client.prompt_layers.render("Good evening, {{input.who}}.", {"input": {"who": "Mr. Anderson"}})
    assert rendered.rendered == "Good evening, Mr. Anderson."


def test_model_endpoints(client):
    # Names are labels: letters, digits, spaces, and . _ - : / @ are accepted.
    name = unique("ollama:llama3/8b @lab")
    endpoint = client.model_endpoints.create({"name": name, "kind": "Inference", "provider": "Ollama",
                                              "baseUrl": CLOSED, "model": "llama3", "maxRetries": 0, "timeoutSeconds": 5})
    try:
        assert endpoint.name == name and endpoint.health_status == "Unknown" and endpoint.provider == "Ollama"
        updated = client.model_endpoints.update(endpoint.id, {"name": endpoint.name, "kind": "Inference", "provider": "Ollama",
                                                              "baseUrl": CLOSED, "model": "llama3", "maxConcurrency": 2})
        assert updated.max_concurrency == 2
        assert any(e.id == endpoint.id for e in client.model_endpoints.list().objects)
        assert client.model_endpoints.health_history(endpoint.id) == []
        result = client.model_endpoints.validate(endpoint.id)
        assert result.success is False and result.error
        assert client.model_endpoints.get(endpoint.id).health_status == "Unhealthy"
        history = client.model_endpoints.health_history(endpoint.id, max_results=10)
        assert len(history) == 1
        check = history[0]
        assert check.model_endpoint_id == endpoint.id and check.status == "Unhealthy" and check.source == "Validate"
        assert check.checked_utc is not None and check.id
        assert len(client.model_endpoints.health_history(endpoint.id, max_results=5000)) == 1, "maxResults is clamped to 1..1000"
        with pytest.raises(HugoError) as err:
            client.model_endpoints.models(endpoint.id)
        assert err.value.status == 502 and err.value.code in ("ProviderUnreachable", "ProviderTimeout")
        # Filters.
        assert any(e.id == endpoint.id for e in client.model_endpoints.list(kind="Inference", provider="Ollama", health="Unhealthy").objects)
        assert not any(e.id == endpoint.id for e in client.model_endpoints.list(kind="Embedding").objects)
        assert not any(e.id == endpoint.id for e in client.model_endpoints.list(provider="OpenAI").objects)
        assert not any(e.id == endpoint.id for e in client.model_endpoints.list(health="Healthy").objects)
        with pytest.raises(HugoError) as err:
            client.model_endpoints.list(provider="Skynet")
        assert err.value.status == 400 and err.value.code == "ValidationFailed" and "OpenAICompatible" in err.value.message
    finally:
        client.model_endpoints.delete(endpoint.id)


def test_model_endpoint_draft_validation_stores_nothing(client):
    before = client.model_endpoints.list(max_results=1000).total_records
    draft = client.model_endpoints.validate_draft({"name": "draft:check", "provider": "OpenAICompatible", "baseUrl": CLOSED,
                                                   "model": "m", "maxRetries": 0, "timeoutSeconds": 5})
    assert draft.success is False and draft.error
    assert client.model_endpoints.list(max_results=1000).total_records == before
    with pytest.raises(HugoError) as err:
        client.model_endpoints.validate_draft({"model": "m"})
    assert err.value.status == 400


def test_tenant_override(admin, client, resource, harness):
    assert not any(r.id == resource.id for r in admin.resources.list(max_results=1000).objects)
    admin.tenant_override = harness.tenant_id
    try:
        assert any(r.id == resource.id for r in admin.resources.list(max_results=1000).objects)
        assert admin.tenants.get(ADMIN_TENANT).id == ADMIN_TENANT
    finally:
        admin.tenant_override = None


async def test_async_domain_dry_run(harness):
    async with new_async_client() as c:
        await c.login(harness.owner_email, harness.owner_password, harness.tenant_id)
        res = await c.resources.create({"name": unique("aapi"), "connectorType": "http", "settings": {"baseUrl": CLOSED}})
        prof = await c.agent_profiles.create({"name": unique("aprof")})
        try:
            await c.agent_profiles.create_binding(prof.id, {"resourceId": res.id, "alias": "api", "accessMode": "ReadOnly"})
            outcome = await c.tool_invocations.dry_run("http_request", **_call(prof.id, "GET"))
            assert outcome.decision == "Allowed"
        finally:
            await c.agent_profiles.delete(prof.id)
            await c.resources.delete(res.id)
