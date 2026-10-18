#!/usr/bin/env python3
"""Managed local-file import, Reference/Inline delivery, preview and cleanup.

From sdk/python: python examples/documents.py [local-file.md]
Set HUGO_EMAIL and HUGO_PASSWORD for an isolated tenant. Optional:
HUGO_SDK_SERVER (default http://127.0.0.1:8800) and HUGO_TENANT.
"""
import os
from pathlib import Path
import sys
import time
from hugo_sdk import HugoClient


def main() -> None:
    source = Path(sys.argv[1]) if len(sys.argv) > 1 else Path(__file__).resolve().parents[2] / "fixtures" / "LOG_MESSAGE_STRUCTURE.md"
    # Strict decoding rejects malformed UTF-8; this reads the client's file, never a server path.
    markdown = source.read_bytes().decode("utf-8-sig", errors="strict")
    suffix = str(time.time_ns())
    name = f"{source.stem}-{suffix}{source.suffix}"
    with HugoClient(os.environ.get("HUGO_SDK_SERVER", "http://127.0.0.1:8800")) as client:
        profile = None
        document = None
        try:
            client.login(os.environ["HUGO_EMAIL"], os.environ["HUGO_PASSWORD"], os.environ.get("HUGO_TENANT", "default"))
            profile = client.agent_profiles.create({"name": f"document-example-{suffix}"})
            document = client.prompt_documents.create({"name": name, "description": "Read before analyzing synthetic application logs.", "markdown": markdown, "visibility": "Private", "active": True})
            initial = client.agent_prompt_documents.list(profile.id)
            attachment = client.agent_prompt_documents.create(profile.id, {"documentId": document.id, "deliveryMode": "Reference", "revisionPolicy": "Latest", "enabled": True, "allowPublicAccess": False, "expectedDocumentSetVersion": initial.document_set_version})
            print("Attached:", [item.name for item in client.agent_prompt_documents.list(profile.id).items])
            reference = client.agent_prompt_documents.preview(profile.id)
            print("Reference manifest:", reference.manifest, "readiness:", reference.issues)
            current = client.agent_prompt_documents.list(profile.id)
            attachment = client.agent_prompt_documents.update(profile.id, attachment.id, {"expectedVersion": attachment.version, "expectedDocumentSetVersion": current.document_set_version, "deliveryMode": "Inline"})
            inline = client.agent_prompt_documents.preview(profile.id)
            print("Inline bytes:", inline.inline_bytes, "estimated tokens:", inline.estimated_tokens)
            if source.name == "LOG_MESSAGE_STRUCTURE.md" and "Synthetic application log schema" not in inline.compiled_text:
                raise RuntimeError("Inline preview omitted the example content")
            final = client.agent_prompt_documents.list(profile.id)
            client.agent_prompt_documents.delete(profile.id, attachment.id, attachment.version, final.document_set_version)
        finally:
            try:
                if profile:
                    client.agent_profiles.delete(profile.id)
            finally:
                if document:
                    current = client.prompt_documents.get(document.id)
                    client.prompt_documents.delete(document.id, current.version)


if __name__ == "__main__":
    main()
