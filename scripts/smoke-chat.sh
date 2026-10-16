#!/usr/bin/env bash
# Live smoke test of Hugo's own agent loop (plan P6.22). Not part of CI.
#
# It signs in as Thomas Anderson, creates a model endpoint, points the Hugo profile at it, chats over SSE
# (expecting Agent Smith to address Mr. Anderson), fetches the A2A agent card, sends an A2A message, and calls the
# OpenAI-compatible endpoint.
#
# Against a real local Ollama (the default):
#   ollama pull llama3.1:8b
#   scripts/smoke-chat.sh
#
# Against the deterministic stub LLM from the test project:
#   dotnet run --project src/Test.Automated -f net10.0 -- --stub-llm     # prints "stub-llm http://127.0.0.1:PORT model stub-model"
#   HUGO_SMOKE_PROVIDER=OpenAICompatible HUGO_SMOKE_BASE_URL=http://127.0.0.1:PORT HUGO_SMOKE_MODEL=stub-model scripts/smoke-chat.sh
#
# Environment:
#   HUGO_URL                 Hugo base URL (default http://127.0.0.1:8800)
#   HUGO_TENANT              Tenant (default default)
#   HUGO_EMAIL               User (default anderson@hugo.local)
#   HUGO_PASSWORD            Password (default password, the seeded value)
#   HUGO_NEW_PASSWORD        When set, the script changes HUGO_PASSWORD to this value first (optional; D-048 blocks nothing)
#   HUGO_SMOKE_PROVIDER      Model provider (default Ollama)
#   HUGO_SMOKE_BASE_URL      Model base URL (default http://127.0.0.1:11434)
#   HUGO_SMOKE_MODEL         Model name (default llama3.1:8b)
#   HUGO_SMOKE_MAX_OUTPUT_TOKENS  Per-call output cap (default 1024)
#   HUGO_SMOKE_TIMEOUT_SECONDS   Model timeout (default 300; curl allows 30 extra seconds)
#
# Requires curl and python3. Exits non-zero on the first failed step.

set +x
set -euo pipefail

HUGO_URL="${HUGO_URL:-http://127.0.0.1:8800}"
HUGO_TENANT="${HUGO_TENANT:-default}"
HUGO_EMAIL="${HUGO_EMAIL:-anderson@hugo.local}"
HUGO_PASSWORD="${HUGO_PASSWORD:-password}"
PROVIDER="${HUGO_SMOKE_PROVIDER:-Ollama}"
BASE_URL="${HUGO_SMOKE_BASE_URL:-http://127.0.0.1:11434}"
MODEL="${HUGO_SMOKE_MODEL:-llama3.1:8b}"
MAX_OUTPUT_TOKENS="${HUGO_SMOKE_MAX_OUTPUT_TOKENS:-1024}"
MODEL_TIMEOUT="${HUGO_SMOKE_TIMEOUT_SECONDS:-300}"
if ! [[ "$MAX_OUTPUT_TOKENS" =~ ^[0-9]+$ && "$MODEL_TIMEOUT" =~ ^[0-9]+$ ]] || (( MAX_OUTPUT_TOKENS < 16 || MAX_OUTPUT_TOKENS > 1000000 || MODEL_TIMEOUT < 1 || MODEL_TIMEOUT > 3600 )); then
  echo "FAIL: output cap must be 16..1000000 and model timeout must be 1..3600 seconds" >&2
  exit 1
fi
REQUEST_TIMEOUT=$((MODEL_TIMEOUT + 30))

request() {
  curl --max-time "$REQUEST_TIMEOUT" -fsS "$@"
}

json() {
  python3 -c 'import json,sys; d=json.load(sys.stdin); scope={"d":d}; valid=eval(sys.argv[2],scope); valid or sys.exit("FAIL: response did not satisfy smoke assertions"); print(eval(sys.argv[1],scope))' "$1" "${2:-True}"
}

step() {
  echo
  echo "== $1"
}

login() {
  request -X POST "$HUGO_URL/v1.0/token" -H 'Content-Type: application/json' \
    -d "$(python3 -c 'import json,sys; print(json.dumps(dict(tenantId=sys.argv[1],email=sys.argv[2],password=sys.argv[3])))' "$HUGO_TENANT" "$HUGO_EMAIL" "$1")" | json 'd["token"]'
}

step "Sign in as $HUGO_EMAIL"
if [ -n "${HUGO_NEW_PASSWORD:-}" ]; then
  FIRST=$(login "$HUGO_PASSWORD")
  request -X POST "$HUGO_URL/v1.0/api/users/me/password" -H "Authorization: Bearer $FIRST" -H 'Content-Type: application/json' \
    -d "$(python3 -c 'import json,sys; print(json.dumps(dict(currentPassword=sys.argv[1],newPassword=sys.argv[2])))' "$HUGO_PASSWORD" "$HUGO_NEW_PASSWORD")" > /dev/null
  HUGO_PASSWORD="$HUGO_NEW_PASSWORD"
fi
TOKEN=$(login "$HUGO_PASSWORD")
AUTH=(-H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json')
echo "signed in"

step "Model endpoint ($PROVIDER $MODEL)"
ENDPOINT=$(request "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/api/model-endpoints" \
  -d "$(python3 -c 'import json,sys,time; print(json.dumps(dict(name="smoke-"+str(time.time_ns()),provider=sys.argv[1],baseUrl=sys.argv[2],model=sys.argv[3],maxRetries=0,timeoutSeconds=int(sys.argv[4]),maxOutputTokens=int(sys.argv[5]),maxConcurrency=1)))' "$PROVIDER" "$BASE_URL" "$MODEL" "$MODEL_TIMEOUT" "$MAX_OUTPUT_TOKENS")" | json 'd["id"]')
echo "endpoint $ENDPOINT"
PROFILE=$(request "${AUTH[@]}" "$HUGO_URL/v1.0/api/agent-profiles" | json '[p["id"] for p in d["objects"] if p["name"]=="Hugo"][0]')
request "${AUTH[@]}" -X PUT "$HUGO_URL/v1.0/api/agent-profiles/$PROFILE" -d "{\"inferenceEndpointId\":\"$ENDPOINT\"}" > /dev/null
echo "profile Hugo ($PROFILE) now uses it"

step "Dashboard chat over SSE"
CONVERSATION=$(request "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/api/conversations" -d '{"title":"Live smoke"}' | json 'd["id"]')
echo "conversation $CONVERSATION"
STREAM=$(request -N "${AUTH[@]}" -H 'Accept: text/event-stream' -X POST "$HUGO_URL/v1.0/api/conversations/$CONVERSATION/messages" \
  -d '{"message":"Introduce yourself in one sentence and address me properly."}')
echo "$STREAM" | grep '^event:' | sort | uniq -c
ANSWER=$(echo "$STREAM" | grep -A1 '^event: turn.completed' | grep '^data:' | sed 's/^data: //' | json 'd["finalText"]' 'bool(d.get("finalText", "").strip())')
echo "answer: $ANSWER"
echo "$ANSWER" | grep -qi "anderson" || { echo "FAIL: the answer does not address Mr. Anderson"; exit 1; }

step "A2A agent card (anonymous)"
request "$HUGO_URL/v1.0/a2a/$HUGO_TENANT/Hugo/.well-known/agent-card.json" | json '(d["name"], [s["id"] for s in d["skills"]], d["supportedInterfaces"][0]["url"])' 'd.get("name")=="Hugo"'

step "A2A SendMessage"
request "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/a2a/$HUGO_TENANT/Hugo" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","messageId":"smoke-1","parts":[{"text":"Say hello in five words."}]}}}' \
  | json '(d["result"]["task"]["id"], d["result"]["task"]["status"]["state"], d["result"]["task"]["artifacts"][0]["parts"][0]["text"])' 'd["result"]["task"]["status"]["state"]=="TASK_STATE_COMPLETED" and bool(d["result"]["task"]["artifacts"][0]["parts"][0]["text"].strip())'

step "OpenAI-compatible chat completion"
request "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/openai/v1/chat/completions" \
  -d '{"model":"Hugo","messages":[{"role":"user","content":"Who are you?"}]}' \
  | json '(d["choices"][0]["message"]["content"], d["usage"], d["hugo"]["conversation_id"])' 'bool(d["choices"][0]["message"]["content"].strip()) and bool(d["hugo"]["conversation_id"])'
request "${AUTH[@]}" "$HUGO_URL/v1.0/openai/v1/models" | json '[m["id"] for m in d["data"]]' 'any(m["id"]=="Hugo" for m in d["data"])'

step "Done"
