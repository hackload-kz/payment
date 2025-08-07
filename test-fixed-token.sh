#\!/bin/bash

# Test the fixed token generation - now should only use Amount and Password

team_slug="demo-team"
password="Demo123@Pass" 
amount="250000"

echo "Testing new token generation with only Amount and Password:"
echo "Amount: $amount"
echo "Password: $password"

# According to debug logs, server now only uses: Amount, Password
# Sorted alphabetically: Amount, Password
concatenated_values="${amount}${password}"
echo "Concatenated: $concatenated_values"

token=$(echo -n "$concatenated_values" | shasum -a 256 | cut -d' ' -f1)
echo "Generated token: $token"
echo "Expected token: 50812a7ee7fbac93c7e64fa4b9abdf799d97448fceb9e24f8a7fc56db9d71f07"

if [ "$token" = "50812a7ee7fbac93c7e64fa4b9abdf799d97448fceb9e24f8a7fc56db9d71f07" ]; then
    echo "✅ Token matches\!"
else
    echo "❌ Token mismatch"
fi

echo ""
echo "Testing with server:"
curl -s -X POST http://localhost:5162/api/v1/PaymentInit/init \
  -H "Content-Type: application/json" \
  -d "{
    \"teamSlug\": \"$team_slug\",
    \"token\": \"$token\",
    \"amount\": $amount,
    \"orderId\": \"test-123\",
    \"currency\": \"RUB\"
  }" | jq -r '.errorCode // .success'
