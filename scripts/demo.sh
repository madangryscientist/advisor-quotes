#!/usr/bin/env bash
# Walks the quote flow against a running API with curl, one line per call: status and trace id.
# Needs only curl and jq. API_URL defaults to the local API. Runs on top of the seeded demo data.
set -euo pipefail

API_URL="${API_URL:-http://localhost:5180}"
ADVISER="ADV-001"
OTHER_ADVISER="ADV-002"
# Unique per run, so re-running creates a new quote instead of replaying the last one.
KEY="demo-$(date +%s)-$$"

# call <label> <method> <path> <adviser> [curl args...]
# Sets STATUS and BODY for the caller.
call() {
  local label=$1 method=$2 path=$3 adviser=$4
  shift 4
  local headers body
  headers=$(mktemp)
  body=$(mktemp)
  STATUS=$(curl -sS -o "$body" -D "$headers" -w '%{http_code}' -X "$method" "$API_URL$path" \
    -H "X-Adviser-Id: $adviser" -H 'Content-Type: application/json' "$@")
  BODY=$(cat "$body")
  local trace
  trace=$(tr -d '\r' <"$headers" | awk -F': ' 'tolower($1) == "x-trace-id" { print $2 }')
  printf '%-34s %s  trace=%s\n' "$label" "$STATUS" "${trace:-none}"
  rm -f "$headers" "$body"
}

# The create command: a client aged 18 to 65 and a cover and term the Pricing library accepts.
create_json() {
  jq -n --arg name "$1" --arg notes "$2" '{
    clientName: $name, clientDateOfBirth: "1990-05-14", clientIsSmoker: false,
    productCode: "LIFE", coverAmount: 500000, termYears: 20, notes: $notes }'
}

echo "API: $API_URL"
echo

call "list-advisers" GET "/v1/queries/list-advisers" "$ADVISER"

CREATE=$(create_json "Demo Client" "Created by scripts/demo.sh.")
call "save-quote, no Idempotency-Key" POST "/v1/commands/save-quote" "$ADVISER" -d "$CREATE"

call "save-quote, create with key" POST "/v1/commands/save-quote" "$ADVISER" \
  -H "Idempotency-Key: $KEY" -d "$CREATE"
QUOTE_ID=$(jq -r '.quoteId' <<<"$BODY")

call "save-quote, same key and body" POST "/v1/commands/save-quote" "$ADVISER" \
  -H "Idempotency-Key: $KEY" -d "$CREATE"

CHANGED=$(create_json "Demo Client, other name" "Same key, different body.")
call "save-quote, same key, new body" POST "/v1/commands/save-quote" "$ADVISER" \
  -H "Idempotency-Key: $KEY" -d "$CHANGED"

UPDATE=$(jq --arg id "$QUOTE_ID" '. + {quoteId: $id, notes: "Revised while still a draft."}' <<<"$CREATE")
call "save-quote, update the draft" POST "/v1/commands/save-quote" "$ADVISER" -d "$UPDATE"

call "send-quote-to-client" POST "/v1/commands/send-quote-to-client" "$ADVISER" \
  -d "$(jq -n --arg id "$QUOTE_ID" '{quoteId: $id}')"

call "accept-quote" POST "/v1/commands/accept-quote" "$ADVISER" \
  -d "$(jq -n --arg id "$QUOTE_ID" '{quoteId: $id}')"

call "send-quote-to-client, accepted" POST "/v1/commands/send-quote-to-client" "$ADVISER" \
  -d "$(jq -n --arg id "$QUOTE_ID" '{quoteId: $id}')"

# A seeded quote of the other adviser's. The demo data seeds one for ADV-002.
call "list-quotes as $OTHER_ADVISER" GET "/v1/queries/list-quotes" "$OTHER_ADVISER"
OTHER_ID=$(jq -r '.[0].quoteId' <<<"$BODY")
call "send-quote, another adviser's" POST "/v1/commands/send-quote-to-client" "$ADVISER" \
  -d "$(jq -n --arg id "$OTHER_ID" '{quoteId: $id}')"

# The reason is checked before the status, so this is a 400 even though the quote could not be declined anyway.
call "decline-quote, no reason" POST "/v1/commands/decline-quote" "$ADVISER" \
  -d "$(jq -n --arg id "$QUOTE_ID" '{quoteId: $id}')"
