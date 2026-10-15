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
#
# Requires curl and python3. Exits non-zero on the first failed step.

set -euo pipefail

HUGO_URL="${HUGO_URL:-http://127.0.0.1:8800}"
HUGO_TENANT="${HUGO_TENANT:-default}"
HUGO_EMAIL="${HUGO_EMAIL:-anderson@hugo.local}"
HUGO_PASSWORD="${HUGO_PASSWORD:-password}"
PROVIDER="${HUGO_SMOKE_PROVIDER:-Ollama}"
BASE_URL="${HUGO_SMOKE_BASE_URL:-http://127.0.0.1:11434}"
MODEL="${HUGO_SMOKE_MODEL:-llama3.1:8b}"

json() {
  python3 -c 'import json,sys; d=json.load(sys.stdin); print(eval(sys.argv[1], {"d": d}))' "$1"
}

step() {
  echo
  echo "== $1"
}

login() {
  curl -sf -X POST "$HUGO_URL/v1.0/token" -H 'Content-Type: application/json' \
    -d "{\"tenantId\":\"$HUGO_TENANT\",\"email\":\"$HUGO_EMAIL\",\"password\":\"$1\"}" | json 'd["token"]'
}

step "Sign in as $HUGO_EMAIL"
if [ -n "${HUGO_NEW_PASSWORD:-}" ]; then
  FIRST=$(login "$HUGO_PASSWORD")
  curl -sf -X POST "$HUGO_URL/v1.0/api/users/me/password" -H "Authorization: Bearer $FIRST" -H 'Content-Type: application/json' \
    -d "{\"currentPassword\":\"$HUGO_PASSWORD\",\"newPassword\":\"$HUGO_NEW_PASSWORD\"}" > /dev/null
  HUGO_PASSWORD="$HUGO_NEW_PASSWORD"
fi
TOKEN=$(login "$HUGO_PASSWORD")
AUTH=(-H "Authorization: Bearer $TOKEN" -H 'Content-Type: application/json')
echo "signed in"

step "Model endpoint ($PROVIDER $MODEL at $BASE_URL)"
ENDPOINT=$(curl -sf "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/api/model-endpoints" \
  -d "{\"name\":\"smoke-$(date +%s)\",\"provider\":\"$PROVIDER\",\"baseUrl\":\"$BASE_URL\",\"model\":\"$MODEL\",\"maxRetries\":0,\"timeoutSeconds\":120}" | json 'd["id"]')
echo "endpoint $ENDPOINT"
PROFILE=$(curl -sf "${AUTH[@]}" "$HUGO_URL/v1.0/api/agent-profiles" | json '[p["id"] for p in d["objects"] if p["name"]=="Hugo"][0]')
curl -sf "${AUTH[@]}" -X PUT "$HUGO_URL/v1.0/api/agent-profiles/$PROFILE" -d "{\"inferenceEndpointId\":\"$ENDPOINT\"}" > /dev/null
echo "profile Hugo ($PROFILE) now uses it"

step "Dashboard chat over SSE"
CONVERSATION=$(curl -sf "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/api/conversations" -d '{}' | json 'd["id"]')
echo "conversation $CONVERSATION"
STREAM=$(curl -sfN "${AUTH[@]}" -H 'Accept: text/event-stream' -X POST "$HUGO_URL/v1.0/api/conversations/$CONVERSATION/messages" \
  -d '{"message":"Introduce yourself in one sentence and address me properly."}')
echo "$STREAM" | grep '^event:' | sort | uniq -c
ANSWER=$(echo "$STREAM" | grep -A1 '^event: turn.completed' | grep '^data:' | sed 's/^data: //' | json 'd["finalText"]')
echo "answer: $ANSWER"
echo "$ANSWER" | grep -qi "anderson" || { echo "FAIL: the answer does not address Mr. Anderson"; exit 1; }

step "A2A agent card (anonymous)"
curl -sf "$HUGO_URL/v1.0/a2a/$HUGO_TENANT/Hugo/.well-known/agent-card.json" | json '(d["name"], [s["id"] for s in d["skills"]], d["supportedInterfaces"][0]["url"])'

step "A2A SendMessage"
curl -sf "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/a2a/$HUGO_TENANT/Hugo" \
  -d '{"jsonrpc":"2.0","id":1,"method":"SendMessage","params":{"message":{"role":"ROLE_USER","messageId":"smoke-1","parts":[{"text":"Say hello in five words."}]}}}' \
  | json '(d["result"]["task"]["id"], d["result"]["task"]["status"]["state"], d["result"]["task"]["artifacts"][0]["parts"][0]["text"])'

step "OpenAI-compatible chat completion"
curl -sf "${AUTH[@]}" -X POST "$HUGO_URL/v1.0/openai/v1/chat/completions" \
  -d '{"model":"Hugo","messages":[{"role":"user","content":"Who are you?"}]}' \
  | json '(d["choices"][0]["message"]["content"], d["usage"], d["hugo"]["conversation_id"])'
curl -sf "${AUTH[@]}" "$HUGO_URL/v1.0/openai/v1/models" | json '[m["id"] for m in d["data"]]'

step "Done"
