"""Preview domain routes (Phase 3, 5, and 6). Each test probes its list route first and skips with a clear reason while
the server answers 404 (route not implemented yet)."""

import pytest

from hugo_sdk import HugoError

from conftest import domain_call, unique


@pytest.fixture(scope="module")
def secret_for_domain(client):
    secret = client.secrets.create({"name": unique("domain"), "kind": "Password", "values": {"password": "pw-domain-sdk"}})
    yield secret
    try:
        client.secrets.delete(secret.id)
    except HugoError:
        pass


def test_connectors(client):
    connectors = domain_call(lambda: client.connectors.list())
    assert connectors
    first = client.connectors.get(connectors[0].type)
    assert first.type == connectors[0].type and first.settings is not None


def _make_resource(client):
    connectors = client.connectors.list()
    http = next((c for c in connectors if c.type == "http"), connectors[0])
    settings = {}
    for s in http.settings:
        if s.required:
            settings[s.key] = s.default_value or ("https://127.0.0.1:9" if "url" in s.key.lower() else "x")
    return client.resources.create({"name": unique("res"), "connectorType": http.type, "environment": "test", "settings": settings})


def test_resources_crud(client):
    domain_call(lambda: client.resources.list())
    resource = _make_resource(client)
    try:
        assert client.resources.get(resource.id).name == resource.name
        updated = client.resources.update(resource.id, {"name": resource.name, "connectorType": resource.connector_type, "notes": "sdk"})
        assert updated.notes == "sdk"
        assert any(r.id == resource.id for r in client.resources.list().objects)
        assert isinstance(client.resources.referrers(resource.id), list)
        tested = client.resources.test(resource.id)
        assert tested.success in (True, False)
    finally:
        client.resources.delete(resource.id)


def test_resource_knowledge(client):
    domain_call(lambda: client.resources.list())
    resource = _make_resource(client)
    try:
        domain_call(lambda: client.resources.knowledge(resource.id))
        entry = client.resources.put_knowledge(resource.id, "general", "SDK knowledge note", key="note")
        assert entry.content == "SDK knowledge note"
        assert client.resources.get_knowledge(resource.id, "general", "note").version >= 1
        assert client.resources.knowledge_versions(resource.id, "general", "note")
        client.resources.delete_knowledge(resource.id, "general", "note")
    finally:
        client.resources.delete(resource.id)


def test_agent_profiles_bindings_instructions_tools(client):
    domain_call(lambda: client.agent_profiles.list())
    profile = client.agent_profiles.create({"name": unique("profile"), "displayName": "SDK profile"})
    try:
        assert client.agent_profiles.get(profile.id).name == profile.name
        assert client.agent_profiles.update(profile.id, {"description": "sdk"}).description == "sdk"
        compiled = client.agent_profiles.compiled_instructions(profile.id, surface="Mcp")
        assert compiled.text is not None
        catalog = client.agent_profiles.tools(profile.id)
        assert catalog.profile_id == profile.id
        resource = _make_resource(client)
        try:
            binding = client.agent_profiles.create_binding(profile.id, {"resourceId": resource.id, "alias": "sdk", "accessMode": "ReadOnly"})
            assert binding.access_mode == "ReadOnly"
            assert any(b.id == binding.id for b in client.agent_profiles.list_bindings(profile.id))
            rules = client.agent_profiles.get_tool_rules(profile.id, binding.id)
            assert isinstance(rules, list)
            client.agent_profiles.delete_binding(profile.id, binding.id)
        finally:
            client.resources.delete(resource.id)
    finally:
        client.agent_profiles.delete(profile.id)


def test_model_endpoints(client):
    domain_call(lambda: client.model_endpoints.list())
    endpoint = client.model_endpoints.create({"name": unique("model"), "kind": "Inference", "provider": "Ollama",
                                              "baseUrl": "http://127.0.0.1:11434", "model": "llama3"})
    try:
        assert client.model_endpoints.get(endpoint.id).provider == "Ollama"
    finally:
        client.model_endpoints.delete(endpoint.id)


def test_tool_invocations_dry_run(client):
    domain_call(lambda: client.tool_invocations.list())
    summary = client.tool_invocations.summary()
    assert summary.total >= 0
    with pytest.raises(HugoError):
        client.tool_invocations.dry_run("ssh_exec", {"command": "uptime"}, profile="no-such-profile")


def test_approvals(client):
    page = domain_call(lambda: client.approvals.list(status="Pending"))
    assert page.success


def test_lockdown(client):
    state = domain_call(lambda: client.lockdown.get())
    assert state.lockdown is False
    on = client.lockdown.enable("sdk harness")
    try:
        assert on.lockdown
    finally:
        off = client.lockdown.disable()
        assert not off.lockdown


def test_conversations(client):
    page = domain_call(lambda: client.conversations.list())
    assert page.success
