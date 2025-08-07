#\!/bin/bash

# Test token generation according to payment-authentication.md specification
# This should match the server-side implementation after my fix

team_slug="demo-team"
password="Demo123@Pass"
amount="250000"
order_id="test-123"
currency="RUB"

echo "Generating token for minimal request:"
echo "TeamSlug: $team_slug"
echo "Amount: $amount"
echo "OrderId: $order_id"
echo "Currency: $currency"
echo "Password: $password"

# Step 1: Only include root-level scalar parameters that are actually provided
# Step 2: Add password
# Step 3: Sort alphabetically
# Step 4: Concatenate values
# Step 5: SHA-256 hash

# For request: {"teamSlug":"demo-team","token":"dummy-token","amount":250000,"orderId":"test-123","currency":"RUB"}
# Parameters after adding password and sorting alphabetically:
# Amount, Currency, OrderId, Password, TeamSlug

concatenated_values="${amount}${currency}${order_id}${password}${team_slug}"
echo "Concatenated values: $concatenated_values"

token=$(echo -n "$concatenated_values" | shasum -a 256 | cut -d' ' -f1)
echo "Generated token: $token"

echo ""
echo "Testing with generated token:"
curl -s -X POST http://localhost:5162/api/v1/PaymentInit/init \
  -H "Content-Type: application/json" \
  -d "{
    \"teamSlug\": \"$team_slug\",
    \"token\": \"$token\",
    \"amount\": $amount,
    \"orderId\": \"$order_id\",
    \"currency\": \"$currency\"
  }" | jq '.'
