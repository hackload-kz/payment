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
| `AUTHORIZED` | `PARTIAL_REVERSED` | Partial reversal | Hold partially released (Amount < Original) |
| `AUTHORIZED` | `REVERSED` | Full reversal | Hold completely released (Amount = Original) |
| `CONFIRMED` | `PARTIAL_REFUNDED` | Partial refund | Money partially returned (Amount < Original) |
| `CONFIRMED` | `REFUNDED` | Full refund | Money completely returned (Amount = Original) |

### Special Payment Method Rules
- **Installment payments**: Can only be cancelled in `AUTHORIZED` status
- **BNPL ("Долями") payments**: Partial or full refunds available for `CONFIRMED` or `PARTIAL_REFUNDED` status

## Request Parameters

### Required Parameters
- **TerminalKey** (string, required)
  - Terminal identifier issued to merchant in I-Business
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
  - **Note**: Ignored for `NEW` status cancellations (always full amount)
  - Determines partial vs full cancellation

### Receipt Management
- **Receipt** (Receipt_FFD_12 | Receipt_FFD_105 object)
  - Fiscal receipt data for online cash register
  - **Required** when online cash register is connected
  - **Full cancellation**: Receipt structure not required
  - **Partial cancellation**: Must specify items being cancelled
  - Can differ from original Init receipt for partial operations

### Marketplace Support
- **Shops** (Array of ShopsCancel objects)
  - **Required for marketplace implementations**
  - Contains shop-specific cancellation data
  - Enables proper fund distribution in marketplace scenarios

### SBP (Faster Payment System) Parameters
- **QrMemberId** (string)
  - Bank code in SBP classification for refund routing
  - See QrMembersList method for available codes
  - Ensures refund goes to correct bank

### Payment Method Routing
- **Route** (enum: "TCB" | "BNPL")
  - Payment method identifier
  - TCB: Standard card payments
  - BNPL: "Buy Now, Pay Later" payments

- **Source** (enum: "installment" | "BNPL")
  - Payment source identification
  - Affects available cancellation operations

### Idempotency Control
- **ExternalRequestId** (string, ≤256 chars)
  - Merchant-side operation identifier
  - **Not supported for SBP operations**
  - **Required for BNPL and installment operations**
  - **BNPL format**: UUID v4
  - **Installment format**: String (≤100 characters)
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
- **TerminalKey** (string, required)
  - Terminal identifier echoed from request

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
  - 0 for full cancellations, >0 for partial cancellations

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
  "TerminalKey": "1234567890",
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

### Successful Partial Refund
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-12345",
  "Success": true,
  "Status": "PARTIAL_REFUNDED",
  "OriginalAmount": 150000,
  "NewAmount": 75000,
  "PaymentId": 987654321,
  "ErrorCode": "0"
}
```

### Cancellation Error
```json
{
  "TerminalKey": "1234567890",
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
- **AUTHORIZED → REVERSED/PARTIAL_REVERSED**: Releases card hold, no money movement
- **CONFIRMED → REFUNDED/PARTIAL_REFUNDED**: Actual money transfer back to customer

### State Machine Compliance
- Method enforces proper state transitions per payment lifecycle flowchart
- Invalid state transitions return appropriate error codes
- Maintains payment integrity throughout cancellation process

### Integration Considerations
- Use ExternalRequestId for idempotency in production systems
- Implement proper receipt handling for fiscal compliance
- Monitor OriginalAmount vs NewAmount for reconciliation
- Handle marketplace shop data for multi-vendor scenarios

### Error Handling
- Validate payment status before attempting cancellation
- Check available cancellation amount for partial operations
- Ensure proper Route/Source combination for specialized payment methods

### Security Requirements
- SHA-256 token validation mandatory
- HTTPS required for all communications
- Merchant can only cancel own terminal's payments

This method provides comprehensive cancellation capabilities while maintaining payment lifecycle integrity and supporting various payment scenarios including marketplace, installment, and BNPL operations.