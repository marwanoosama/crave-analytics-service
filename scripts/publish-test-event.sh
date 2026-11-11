#!/usr/bin/env bash
# Publish a raw event envelope to the order.events exchange via the RabbitMQ
# management API — smoke test for the analytics consumer (plan.md Phase 2).
#
# Usage: scripts/publish-test-event.sh [routing_key]
#   routing_key defaults to order.placed. Same eventId ⇒ consumer ack-skips
#   (dedupe); a bad routing key still lands in the queue (bindings are wide).
set -euo pipefail

MGMT_URL="${RABBITMQ_MGMT_URL:-http://localhost:15672}"
USER="${RABBITMQ_USER:-guest}"
PASS="${RABBITMQ_PASS:-guest}"
EXCHANGE="${RABBITMQ_ORDER_EVENTS_EXCHANGE:-order.events}"
ROUTING_KEY="${1:-order.placed}"

EVENT_ID="$(cat /proc/sys/kernel/random/uuid)"
OCCURRED_AT="$(date -u +%Y-%m-%dT%H:%M:%S.000Z)"
AGGREGATE_ID="$(cat /proc/sys/kernel/random/uuid)"

PAYLOAD='{
    "orderId": "'"$AGGREGATE_ID"'",
    "region": "eg",
    "countryCode": "EG",
    "restaurantId": 42,
    "branchId": 7,
    "customerId": 1234,
    "status": "placed",
    "paymentMethod": "cod",
    "subtotal": 150000,
    "deliveryFee": 2000,
    "serviceFee": 1000,
    "total": 153000,
    "currency": "EGP",
    "items": [{ "productId": 9, "quantity": 2, "unitPrice": 5000, "lineTotal": 10000 }],
    "placedAt": "'"$OCCURRED_AT"'"
}'

ENVELOPE=$(cat <<EOF
{
    "eventId": "$EVENT_ID",
    "eventType": "$ROUTING_KEY",
    "occurredAt": "$OCCURRED_AT",
    "aggregateType": "order",
    "aggregateId": "$AGGREGATE_ID",
    "region": "eg",
    "payload": $PAYLOAD
}
EOF
)

BODY=$(jq -n --arg rk "$ROUTING_KEY" --arg payload "$ENVELOPE" \
    '{properties:{content_type:"application/json",delivery_mode:2},routing_key:$rk,payload:$payload,payload_encoding:"string"}')

RESULT=$(curl -s -u "$USER:$PASS" -H "Content-Type: application/json" \
    -X POST "$MGMT_URL/api/exchanges/%2F/$EXCHANGE/publish" -d "$BODY")

echo "eventId: $EVENT_ID"
echo "broker:  $RESULT"
