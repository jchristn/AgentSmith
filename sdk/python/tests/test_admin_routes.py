"""Prompt templates, audit, request history, settings, and system routes."""

import time
from datetime import datetime, timedelta, timezone

import pytest

from hugo_sdk import HugoError

from conftest import unique


def test_prompt_templates_crud_versions_render_clone(client):
    tpl = client.prompt_templates.create({"name": unique("tpl"), "description": "sdk", "kind": "System",
                                          "content": "Hello {{input.name}}", "changeNote": "first"})
    try:
        assert tpl.current_version == 1 and not tpl.is_built_in
        updated = client.prompt_templates.update(tpl.id, {"name": tpl.name, "kind": "System", "content": "Hi {{input.name}}", "changeNote": "second"})
        assert updated.current_version == 2
        versions = client.prompt_templates.versions(tpl.id)
        assert [v.version for v in versions][:2] == [2, 1]
        assert client.prompt_templates.version(tpl.id, 1).content == "Hello {{input.name}}"
        restored = client.prompt_templates.restore(tpl.id, 1)
        assert restored.content == "Hello {{input.name}}" and restored.current_version == 3
        rendered = client.prompt_templates.render(tpl.id, {"input": {"name": "Mr. Anderson"}})
        assert rendered.success and rendered.rendered == "Hello Mr. Anderson"
        adhoc = client.prompt_templates.render_ad_hoc("Agent {{input.who}}", {"input": {"who": "Smith"}})
        assert adhoc.rendered == "Agent Smith"
        clone = client.prompt_templates.clone(tpl.id, unique("tplclone"))
        try:
            assert clone.content == restored.content
        finally:
            client.prompt_templates.delete(clone.id)
        assert any(t.id == tpl.id for t in client.prompt_templates.list(max_results=1000).objects)
    finally:
        client.prompt_templates.delete(tpl.id)


def test_audit_list_get(client):
    page = client.audit.list(page_size=5)
    assert page.page_number == 1 and page.total_count >= 1 and page.objects
    record = client.audit.get(page.objects[0].id)
    assert record.id == page.objects[0].id and record.event_type
    issued = client.audit.list(event_type="SessionIssued", page_size=50)
    assert all(r.event_type == "SessionIssued" for r in issued.objects)
    summary = client.audit.summary(event_type="SessionIssued")
    assert summary.total_count == issued.total_count
    assert summary.event_counts == {"SessionIssued": summary.total_count}


def test_audit_delete(client):
    page = client.audit.list(page_size=100)
    target = page.objects[-1]
    client.audit.delete(target.id)
    with pytest.raises(HugoError) as err:
        client.audit.get(target.id)
    assert err.value.status == 404
    deleted = client.audit.delete_many(to_utc=datetime.now(timezone.utc) - timedelta(days=3650))
    assert deleted.deleted_count == 0


def test_request_history_list_get_summary_delete(client):
    marker = unique("rhmarker")
    with pytest.raises(HugoError):
        client.secrets.get("sec_" + marker)
    entry = None
    for _ in range(20):
        page = client.request_history.list(path_contains=marker, page_size=10)
        if page.objects:
            entry = page.objects[0]
            break
        time.sleep(0.25)
    assert entry is not None, "the request was not captured"
    assert entry.status_code == 404 and entry.method == "GET"
    full = client.request_history.get(entry.id)
    assert full.id == entry.id and marker in full.path
    summary = client.request_history.summary(from_utc=datetime.now(timezone.utc) - timedelta(hours=1), bucket_minutes=5)
    assert summary.total_count >= 1 and summary.buckets
    client.request_history.delete(entry.id)
    with pytest.raises(HugoError) as err:
        client.request_history.get(entry.id)
    assert err.value.status == 404
    removed = client.request_history.delete_many(path_contains=unique("nothing-matches"))
    assert removed.deleted_count == 0


def test_request_history_filters(client):
    page = client.request_history.list(method="GET", status_code=200, page_size=5)
    assert all(e.method == "GET" and e.status_code == 200 for e in page.objects)


def test_settings_read_admin(admin):
    view = admin.settings.get()
    assert view.fields and any(f.path.startswith("Auth.") for f in view.fields)
    secret_fields = [f for f in view.fields if f.is_secret and f.is_set]
    for f in secret_fields:
        assert not f.value or "*" in f.value or f.value != "hugo-sdk-test-admin-key-0001"


def test_settings_forbidden_for_tenant_admin(client):
    with pytest.raises(HugoError) as err:
        client.settings.get()
    assert err.value.status == 403


def test_system_admin_routes(admin):
    status = admin.system.master_key_status()
    assert status.target_key_version is not None or status.running is False
    assert isinstance(admin.system.observability(), list)
    assert admin.system.server_info().product == "Hugo"
