"""Real-server prompt document contract, revisions and attachment delivery."""
import pytest
from hugo_sdk import HugoError
from conftest import unique


def test_prompt_document_lifecycle(client):
    c = client
    profile = c.agent_profiles.create({"name": unique("document-agent")})
    document = c.prompt_documents.create({"name": unique("LOG_STRUCTURE") + ".md", "description": "Read before interpreting logs.", "markdown": "# Log fields\nSynthetic event_code field.\n", "visibility": "Tenant"})
    try:
        assert "event_code" in c.prompt_documents.revision(document.id, 1).markdown
        state = c.agent_prompt_documents.list(profile.id)
        attachment = c.agent_prompt_documents.create(profile.id, {"expectedDocumentSetVersion": state.document_set_version, "documentId": document.id, "deliveryMode": "Reference", "revisionPolicy": "Latest", "enabled": True})
        reference = c.agent_prompt_documents.preview(profile.id)
        assert document.name in reference.compiled_text
        assert "Synthetic event_code field." not in reference.compiled_text
        inline = c.agent_prompt_documents.update(profile.id, attachment.id, {"expectedVersion": attachment.version, "expectedDocumentSetVersion": attachment.document_set_version, "deliveryMode": "Inline", "revisionPolicy": "Pinned", "pinnedRevision": 1})
        assert "Synthetic event_code field." in c.agent_prompt_documents.preview(profile.id).compiled_text
        with pytest.raises(HugoError) as stale:
            c.prompt_documents.update(document.id, {"expectedVersion": 999999, "markdown": "stale"})
        assert stale.value.status_code == 409
        revised = c.prompt_documents.update(document.id, {"expectedVersion": document.version, "markdown": "# Revision two\nNew fields.\n"})
        assert revised.current_revision == 2
        assert "New fields." not in c.agent_prompt_documents.preview(profile.id).compiled_text
        with pytest.raises(HugoError):
            c.prompt_documents.delete(document.id, revised.version)
        order = c.agent_prompt_documents.reorder(profile.id, [attachment.id], inline.document_set_version)
        refreshed = c.agent_prompt_documents.list(profile.id)
        c.agent_prompt_documents.delete(profile.id, attachment.id, refreshed.items[0].version, refreshed.document_set_version)
        assert not c.prompt_documents.referrers(document.id).items
    finally:
        c.agent_profiles.delete(profile.id)
        current = c.prompt_documents.get(document.id)
        c.prompt_documents.delete(document.id, current.version)


async def test_async_document_management(harness):
    from conftest import new_async_client
    async with new_async_client() as c:
        await c.login(harness.owner_email, harness.owner_password, harness.tenant_id)
        document = await c.prompt_documents.create({"name": unique("ASYNC_LOG") + ".md", "description": "Async document fixture.", "markdown": "# Async schema\n"})
        try:
            actual = await c.prompt_documents.revision(document.id, document.current_revision)
            assert actual.markdown == "# Async schema\n"
            page = await c.prompt_documents.list(search=document.name)
            assert any(item.id == document.id for item in page.objects)
        finally:
            await c.prompt_documents.delete(document.id, document.version)
