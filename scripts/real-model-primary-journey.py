#!/usr/bin/env python3
"""Drive an isolated primary-journey fixture. Model URL comes only from private environment/state."""
import argparse
import json
import os
import pathlib
import urllib.error
import urllib.parse
import urllib.request

parser = argparse.ArgumentParser(description=__doc__)
parser.add_argument('--state', type=pathlib.Path, required=True)
parser.add_argument('phase', choices=['seed', 'turn', 'inspect'])
parser.add_argument('--label', default='investigation')
parser.add_argument('--message')
args = parser.parse_args()
state = json.loads(args.state.read_text())
work = pathlib.Path(state['work'])
base = 'http://127.0.0.1:' + str(state['ports']['server'])


def sanitized(value):
    text = json.dumps(value, ensure_ascii=False).replace(chr(0x2014), ' - ')
    private = state.get('model_url', '')
    for item in [private, urllib.parse.urlsplit(private).hostname if private else '', state.get('token', '')]:
        if item:
            text = text.replace(item, '[redacted]')
    return json.loads(text)


def save():
    args.state.write_text(json.dumps(state))


def call(method, path, body=None, auth=True):
    headers = {'Content-Type': 'application/json'}
    if auth:
        headers['Authorization'] = 'Bearer ' + state['token']
    request = urllib.request.Request(base + path, data=None if body is None else json.dumps(body).encode(), headers=headers, method=method)
    try:
        with urllib.request.urlopen(request, timeout=360) as response:
            raw = response.read()
            return json.loads(raw) if raw else None
    except urllib.error.HTTPError as error:
        raise RuntimeError(str(error.code) + ' ' + str(sanitized(error.read().decode()))) from None


state['token'] = call('POST', '/v1.0/token', {'tenantId': 'default', 'email': 'anderson@hugo.local', 'password': 'password'}, auth=False)['token']
save()
if args.phase == 'seed':
    state['model_url'] = os.environ['HUGO_REAL_MODEL_URL'].rstrip('/')
    model = call('POST', '/v1.0/api/model-endpoints', {'name': 'journey-private-ollama', 'provider': 'Ollama', 'baseUrl': state['model_url'], 'model': os.environ.get('HUGO_REAL_MODEL_NAME', 'qwen3.8:27b'), 'maxOutputTokens': 1800, 'contextWindowTokens': 32768, 'timeoutSeconds': 240, 'maxRetries': 0})
    profile = next(x for x in call('GET', '/v1.0/api/agent-profiles')['objects'] if x['name'] == 'Hugo')
    state['profile'] = profile['id']
    call('PUT', '/v1.0/api/agent-profiles/' + state['profile'], {'inferenceEndpointId': model['id'], 'instructionDetail': 'Full', 'temperature': 0.1, 'maxIterations': 8, 'maxToolCallsPerTurn': 12, 'instructionsAppend': 'This is a disposable acceptance fixture. For checkout investigations use logs_search on papertrail, sql_query on prod-db to inspect pg_stat_activity, and separate ssh_exec calls for uptime and df -h on web-01. Read assets with storage_list when useful. Keep answers brief, evidence-based, address Mr. Anderson, and never claim unavailable data. For requested nginx service restarts use ssh_service_control with unit nginx, action restart, useSudo true; wait for human approval. Do not replace a requested real operation with a mock.'})
    keys = pathlib.Path(state['work']) / 'sshkeys/id_ed25519'
    definitions = [
        ('journey-ssh-key', 'PrivateKey', {'privateKey': keys.read_text()}),
        ('journey-postgres-password', 'Password', {'username': 'hugo', 'password': 'journey-postgres-password'}),
        ('journey-assets-key', 'AccessKeyPair', {'accessKeyId': 'journey-access-key', 'secretAccessKey': 'journey-secret-access-key'}),
        ('journey-papertrail-token', 'ApiToken', {'token': 'journey-papertrail-test-token'}),
    ]
    secret_ids = {name: call('POST', '/v1.0/api/secrets', {'name': name, 'kind': kind, 'values': values})['id'] for name, kind, values in definitions}
    p = state['ports']
    specs = [
        ('web-01', 'ssh', 'ReadWriteWithApproval', {'host': '127.0.0.1', 'port': p['ssh'], 'username': 'hugo', 'authMethod': 'PrivateKey', 'allowSudo': True}, 'privateKey', 'journey-ssh-key'),
        ('prod-db', 'postgresql', 'ReadOnly', {'host': '127.0.0.1', 'port': p['postgres'], 'database': 'shop', 'sslMode': 'Disable'}, 'password', 'journey-postgres-password'),
        ('assets', 's3', 'ReadOnly', {'bucket': 'hugo-testbed', 'region': 'us-east-1', 'endpoint': 'http://127.0.0.1:' + str(p['s3']), 'useSsl': False}, 'accessKeyPair', 'journey-assets-key'),
        ('papertrail', 'papertrail', 'ReadOnly', {'baseUrl': 'http://127.0.0.1:' + str(p['papertrail']) + '/api/v1'}, 'apiToken', 'journey-papertrail-token'),
    ]
    state['resources'] = {}
    checks = {}
    for name, kind, mode, settings, slot, secret in specs:
        resource = call('POST', '/v1.0/api/resources', {'name': name, 'connectorType': kind, 'environment': 'acceptance', 'settings': settings, 'secretBindings': [{'slotName': slot, 'secretId': secret_ids[secret]}]})
        state['resources'][name] = resource['id']
        checks[name] = call('POST', '/v1.0/api/resources/' + resource['id'] + '/test', {})
        call('POST', '/v1.0/api/agent-profiles/' + state['profile'] + '/bindings', {'resourceId': resource['id'], 'alias': name, 'accessMode': mode})
    compiled_path = '/v1.0/api/agent-profiles/' + state['profile'] + '/compiled-instructions?surface=Chat'
    compiled = call('GET', compiled_path)
    assert compiled['bindingCount'] == 4
    for text in ['Agent Smith', 'Mr. Anderson', 'web-01', 'prod-db', 'assets', 'papertrail']:
        assert text in compiled['text'], text
    template_path = '/v1.0/api/prompt-templates/connector.ssh.instructions'
    original = call('GET', template_path)['effectiveContent']
    marker = 'PRIMARY-JOURNEY-TENANT-INSTRUCTION-CHECK'
    call('PUT', template_path + '?layer=Tenant', {'content': original + '\n' + marker})
    assert marker in call('GET', compiled_path)['text']
    call('POST', template_path + '/reset?layer=Tenant', {})
    assert marker not in call('GET', compiled_path)['text']
    call('PUT', '/v1.0/api/agent-profiles/' + state['profile'], {'instructionDetail': 'Auto'})
    state['conversation'] = call('POST', '/v1.0/api/conversations', {'agentProfileId': state['profile'], 'title': 'Real-model primary journey'})['id']
    save()
    (work / 'checks.json').write_text(json.dumps(sanitized({'resourceChecks': checks, 'compiled': compiled, 'templateOverrideAndReset': True}), indent=2))
    print('Seeded exact four resources; compiled persona/service sections; SSH instruction edit/reset verified')
elif args.phase == 'turn':
    assert args.message
    events = []
    request = urllib.request.Request(base + '/v1.0/api/conversations/' + state['conversation'] + '/messages', data=json.dumps({'message': args.message}).encode(), headers={'Authorization': 'Bearer ' + state['token'], 'Content-Type': 'application/json', 'Accept': 'text/event-stream'})
    event = ''
    with urllib.request.urlopen(request, timeout=600) as response:
        for raw in response:
            line = raw.decode().rstrip('\r\n')
            if line.startswith('event:'):
                event = line[6:].strip()
            elif line.startswith('data:'):
                value = json.loads(line[5:].strip())
                events.append({'event': event, 'data': sanitized(value)})
                (work / (args.label + '-events.json')).write_text(json.dumps({'user': args.message, 'events': events}, indent=2))
                if event in ['approval.required', 'turn.completed', 'turn.error']:
                    print(event, str(sanitized(value))[:1000], flush=True)
    print('Turn completed with', len(events), 'events')
else:
    record = {}
    for key, path in {
        'invocations': '/tool-invocations?maxResults=1000',
        'approvals': '/approvals?maxResults=1000',
        'audit': '/tenants/default/audit?pageSize=1000',
        'requests': '/request-history?pageSize=1000',
        'messages': '/conversations/' + state['conversation'] + '/messages',
    }.items():
        record[key] = call('GET', '/v1.0/api' + path)
    (work / 'inspection.json').write_text(json.dumps(sanitized(record), indent=2))
    print('Inspected persisted messages, invocations, approvals, request history, and audit')
