#!/usr/bin/env python3
"""Hugo Python SDK quick start (mirrors the README).

Usage:
    HUGO_SDK_SERVER=http://127.0.0.1:8800 HUGO_EMAIL=anderson@hugo.local HUGO_PASSWORD=... python examples/quickstart.py

Signs in, stores a secret, creates a resource that uses it, binds the resource to a new agent profile, previews the
compiled instructions, dry-runs a tool call, and cleans up. The resource, profile, and tool steps use preview routes;
they are skipped with a message when the server does not have them yet.
"""

import os

from hugo_sdk import HugoClient, HugoError

SERVER = os.environ.get("HUGO_SDK_SERVER", "http://127.0.0.1:8800")
EMAIL = os.environ.get("HUGO_EMAIL", "anderson@hugo.local")
PASSWORD = os.environ.get("HUGO_PASSWORD", "password")
TENANT = os.environ.get("HUGO_TENANT")


def main() -> None:
    with HugoClient(SERVER) as client:
        issued = client.login(EMAIL, PASSWORD, TENANT)
        if issued.must_change_password:
            raise SystemExit("Change the password first: client.change_password(current, new)")
        print(f"Signed in to tenant {client.tenant_id} as {client.token_details().principal_name}")

        secret = client.secrets.create({"name": "quickstart-web-01-key", "kind": "Password", "values": {"password": "change-me"}})
        print(f"Stored secret {secret.id} with fields {secret.fields}")
        try:
            try:
                resource = client.resources.create({
                    "name": "web-01",
                    "connectorType": "ssh",
                    "environment": "prod",
                    "settings": {"host": "web-01.example.com", "port": 22, "username": "deploy"},
                    "secretBindings": [{"slotName": "password", "secretId": secret.id}],
                })
            except HugoError as e:
                if e.is_route_not_found:
                    print("Resources are not available on this server yet (preview).")
                    return
                raise
            profile = client.agent_profiles.create({"name": "quickstart", "displayName": "Quickstart"})
            try:
                client.agent_profiles.create_binding(profile.id, {"resourceId": resource.id, "alias": "web-01", "accessMode": "ReadWriteWithApproval"})
                compiled = client.agent_profiles.compiled_instructions(profile.id, surface="Mcp")
                print(compiled.text)
                outcome = client.tool_invocations.dry_run("ssh_exec", {"resource": "web-01", "command": "uptime"}, profile_id=profile.id)
                print(f"Dry run decision: {outcome.decision} ({outcome.effect}: {outcome.effect_reason})")
            finally:
                client.agent_profiles.delete(profile.id)
                client.resources.delete(resource.id)
        finally:
            client.secrets.delete(secret.id)


if __name__ == "__main__":
    main()
