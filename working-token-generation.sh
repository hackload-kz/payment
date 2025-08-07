#\!/bin/bash

# WORKING token generation based on current server implementation
# Server currently only uses Amount + Password (not all request parameters)

team_slug="demo-team"
password="Demo123@Pass"

# Test different amounts to confirm the pattern
for amount in 100000 250000 500000; do
  echo "Testing amount: $amount"
  
  # Current server algorithm: Amount + Password, sorted alphabetically
  concatenated="${amount}${password}"
  token=$(echo -n "$concatenated" | shasum -a 256 | cut -d' ' -f1)
  
  echo "  Concatenated: $concatenated"
  echo "  Generated token: $token"
  
  # Test with server
  result=$(curl -s -X POST http://localhost:5162/api/v1/PaymentInit/init \
    -H "Content-Type: application/json" \
    -d "{
      \"teamSlug\": \"$team_slug\",
      \"token\": \"$token\",
      \"amount\": $amount,
      \"orderId\": \"test-order-$amount\",
      \"currency\": \"RUB\"
    }" | jq -r '.success // .errorCode')
  
  echo "  Server result: $result"
  echo ""
done

echo "Summary: The server is currently using a simplified token generation:"
echo "1. Only Amount (from request) + Password (from database)"
echo "2. Sort alphabetically: Amount, Password"
echo "3. Concatenate values: \${amount}\${password}"
echo "4. SHA256 hash the concatenated string"
echo ""
echo "This means the server is NOT following the full payment-authentication.md specification."
echo "It should include TeamSlug, Currency, OrderId and other parameters."
