# Cancel Method Technical Specification

## Overview
The Cancel method cancels a payment session and transitions it to appropriate final states based on the current payment status. This method handles both authorization reversals and confirmed payment refunds, implementing the cancellation logic shown in the payment lifecycle flowchart.

## Function Signature
```
POST /cancel
Content-Type: application/json
```

## State Transition Logic
The method performs different operations based on current payment status:

| Current Status | Target Status | Operation Type | Description |
|---------------|---------------|----------------|-------------|
| `NEW` | `CANCELLED` | Full cancellation | Payment session terminated |
| `AUTHORIZED` | `REVERSED` | Full reversal | Hold completely released |
| `CONFIRMED` | `REFUNDED` | Full refund | Money completely returned |


## Request Parameters

### Required Parameters
- **TeamSlug** (string, required)
  - Team identifier issued to merchant in I-Business
  - Used for authentication and routing

- **PaymentId** (string, required)
  - Payment identifier in I-Business system
  - Obtained from Init method response
  - Targets specific payment for cancellation

- **Token** (string, required)
  - Request signature using SHA-256 hash
  - Generated using merchant's secret key and request parameters

### Optional Parameters
- **IP** (string)
  - Client IP address
  - Used for fraud prevention and logging

- **Amount** (number)
  - Cancellation amount in kopecks
  - If not provided, uses original amount from Init method
  - **Note**: Always full amount cancellation




### Payment Method Routing
- **Route** (enum: "TCB")
  - Payment method identifier
  - TCB: Standard card payments

### Idempotency Control
- **ExternalRequestId** (string, ≤256 chars)
  - Merchant-side operation identifier
  - **Behavior**:
    - Empty/missing: No duplicate check performed
    - Provided: Checks for existing cancellation with same ID
    - Duplicate found: Returns current operation state
    - No duplicate: Processes new cancellation

## Response Format

### Response Schema
```json
Content-Type: application/json
```

### Response Parameters

#### Required Fields
- **TeamSlug** (string, required)
  - Team identifier echoed from request

- **OrderId** (string, required)
  - Original order identifier from merchant system

- **Success** (boolean, required)
  - Operation success indicator
  - `true`: Cancellation processed successfully
  - `false`: Cancellation failed

- **Status** (string, required)
  - New payment status after cancellation
  - Maps to final states in payment lifecycle flowchart

- **OriginalAmount** (number, required)
  - Payment amount before cancellation (in kopecks)
  - Shows initial payment value

- **NewAmount** (number, required)
  - Remaining payment amount after cancellation (in kopecks)
  - Always 0 for full cancellations

- **PaymentId** (number, required)
  - Payment identifier in I-Business system
  - Note: Type changed to number in response (vs string in request)

- **ErrorCode** (string, required)
  - Error code indicator
  - "0": Success
  - Non-zero: Specific error code

#### Optional Fields
- **Message** (string, ≤255 chars)
  - Brief error description when Success = false

- **Details** (string)
  - Detailed error information for debugging

- **ExternalRequestId** (string)
  - Echoed merchant operation identifier

## Response Examples

### Successful Full Reversal
```json
{
  "TeamSlug": "1234567890",
  "OrderId": "order-12345",
  "Success": true,
  "Status": "REVERSED",
  "OriginalAmount": 150000,
  "NewAmount": 0,
  "PaymentId": 987654321,
  "ErrorCode": "0",
  "ExternalRequestId": "merchant-cancel-001"
}
```


### Cancellation Error
```json
{
  "TeamSlug": "1234567890",
  "OrderId": "order-12345",
  "Success": false,
  "Status": "CONFIRMED",
  "OriginalAmount": 150000,
  "NewAmount": 150000,
  "PaymentId": 987654321,
  "ErrorCode": "1005",
  "Message": "Payment cannot be cancelled",
  "Details": "Payment in CONFIRMED status requires refund operation, not cancellation"
}
```

## Technical Implementation Notes

### Financial Impact
- **AUTHORIZED → REVERSED**: Releases card hold, no money movement
- **CONFIRMED → REFUNDED**: Actual money transfer back to customer

### State Machine Compliance
- Method enforces proper state transitions per payment lifecycle flowchart
- Invalid state transitions return appropriate error codes
- Maintains payment integrity throughout cancellation process

### Integration Considerations
- Use ExternalRequestId for idempotency in production systems
- Monitor OriginalAmount vs NewAmount for reconciliation

### Error Handling
- Validate payment status before attempting cancellation
- Ensure proper Route specification for standard payment methods

### Security Requirements
- SHA-256 token validation mandatory
- HTTPS required for all communications
- Merchant can only cancel own team's payments

This method provides comprehensive cancellation capabilities while maintaining payment lifecycle integrity for standard card payment operations.