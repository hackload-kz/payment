# Confirm Method Technical Specification

## Overview
The Confirm method performs the capture phase of a two-stage payment process, converting authorized funds from hold status to actual charge. This method implements the AUTHORIZED → CONFIRMING → CONFIRMED transition shown in the payment lifecycle flowchart.

## Function Signature
```
POST /confirm
Content-Type: application/json
```

## Two-Stage Payment Process
This method is exclusively used for two-stage payments (PayType = "T"):

1. **Stage 1 (Authorization)**: Funds are placed on hold (AUTHORIZED status)
2. **Stage 2 (Confirmation)**: Held funds are captured/charged (CONFIRMED status)

### State Transition Flow
- **Input State**: `AUTHORIZED` (required)
- **Intermediate State**: `CONFIRMING` (temporary processing state)
- **Final State**: `CONFIRMED` (successful capture)

### Amount Flexibility
- Confirmation amount can be **less than or equal to** authorized amount
- Enables partial captures for scenarios like:
  - Order modifications before fulfillment
  - Partial shipments
  - Price adjustments
  - Tax recalculations

## Request Parameters

### Required Parameters
- **TerminalKey** (string, required)
  - Terminal identifier issued to merchant in I-Business
  - Used for authentication and routing

- **PaymentId** (string, ≤20 chars, required)
  - Payment identifier in I-Business system
  - Must reference payment in `AUTHORIZED` status
  - Obtained from Init method response

- **Token** (string, required)
  - Request signature using SHA-256 hash
  - Generated using merchant's secret key and request parameters

### Optional Parameters
- **IP** (string)
  - Client IP address
  - Used for fraud prevention and audit logging

- **Amount** (number)
  - Confirmation amount in kopecks
  - If not provided, uses full authorized amount from Init
  - Must be ≤ original authorized amount
  - Enables partial capture functionality

### Fiscal Compliance
- **Receipt** (Receipt_FFD_12 | Receipt_FFD_105 object)
  - Fiscal receipt data for online cash register
  - **Required** when online cash register is connected
  - Must reflect actual items/amounts being confirmed
  - May differ from Init receipt for partial confirmations

### Marketplace Support
- **Shops** (Array of Shops objects)
  - **Required for marketplace implementations**
  - Contains shop-specific confirmation data
  - Enables proper fund distribution to vendors

### Payment Method Routing
- **Route** (enum: "TCB" | "BNPL")
  - Payment method identifier
  - TCB: Standard card payments
  - BNPL: "Buy Now, Pay Later" payments

- **Source** (enum: "installment" | "BNPL")
  - Payment source identification
  - Affects confirmation processing logic

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
  - `true`: Confirmation processed successfully
  - `false`: Confirmation failed

- **Status** (string, required)
  - Current payment status after confirmation attempt
  - **Possible values**: NEW, AUTHORIZING, AUTHORIZED, AUTH_FAIL, CANCELED, CHECKING, CHECKED, COMPLETING, COMPLETED, CONFIRMING, CONFIRMED, DEADLINE_EXPIRED, FORM_SHOWED, PARTIAL_REFUNDED, PREAUTHORIZING, PROCESSING, 3DS_CHECKING, 3DS_CHECKED, REVERSING, REVERSED, REFUNDING, REFUNDED, REJECTED, UNKNOWN
  - **Success case**: `CONFIRMED`
  - **Processing case**: `CONFIRMING`

- **PaymentId** (string, required)
  - Payment identifier in I-Business system
  - Same as request PaymentId for correlation

- **ErrorCode** (string, required)
  - Error code indicator
  - "0": Success
  - Non-zero: Specific error code for debugging

#### Optional Fields
- **Message** (string, ≤255 chars)
  - Brief error description when Success = false
  - Human-readable error summary

- **Details** (string)
  - Detailed error information for debugging
  - Technical context for error resolution

- **Params** (Array of Items_Params objects)
  - Additional details for installment payments
  - Contains installment-specific processing information

## Response Examples

### Successful Full Confirmation
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-12345",
  "Success": true,
  "Status": "CONFIRMED",
  "PaymentId": "987654321",
  "ErrorCode": "0"
}
```

### Successful Partial Confirmation
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-12345",
  "Success": true,
  "Status": "CONFIRMED",
  "PaymentId": "987654321",
  "ErrorCode": "0"
}
```

### Confirmation in Progress
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-12345",
  "Success": true,
  "Status": "CONFIRMING",
  "PaymentId": "987654321",
  "ErrorCode": "0"
}
```

### Confirmation Error - Invalid Status
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-12345",
  "Success": false,
  "Status": "CONFIRMED",
  "PaymentId": "987654321",
  "ErrorCode": "1003",
  "Message": "Payment not in valid status for confirmation",
  "Details": "Payment must be in AUTHORIZED status to perform confirmation. Current status: CONFIRMED"
}
```

### Confirmation Error - Amount Exceeded
```json
{
  "TerminalKey": "1234567890",
  "OrderId": "order-12345",
  "Success": false,
  "Status": "AUTHORIZED",
  "PaymentId": "987654321",
  "ErrorCode": "1007",
  "Message": "Confirmation amount exceeds authorized amount",
  "Details": "Requested: 200000 kopecks, Authorized: 150000 kopecks"
}
```

## Technical Implementation Notes

### State Machine Compliance
- **Pre-condition**: Payment must be in `AUTHORIZED` status
- **Processing**: Temporarily moves to `CONFIRMING` status
- **Success**: Final `CONFIRMED` status
- **Failure**: Returns to `AUTHORIZED` status

### Financial Impact
- **Authorization phase**: Funds placed on hold (no charge)
- **Confirmation phase**: Actual money transfer from customer account
- **Partial confirmation**: Remaining authorized amount stays on hold

### Integration Considerations

#### Timing Requirements
- Authorized payments typically have time limits (usually 7-30 days)
- Confirm before authorization expires to avoid automatic reversal
- Monitor payment status to track confirmation progress

#### Amount Management
- Track authorized vs confirmed amounts for partial captures
- Handle remaining authorization after partial confirmation
- Consider automatic reversal of unconfirmed amounts

#### Error Handling Strategies
- Validate payment status before confirmation attempts
- Implement retry logic for temporary processing failures
- Handle amount validation errors gracefully
- Monitor for authorization expiration

### Business Use Cases
1. **Full Capture**: Standard completion of two-stage payment
2. **Partial Capture**: Order modifications, partial shipments
3. **Delayed Capture**: Confirm after goods preparation/shipment
4. **Split Fulfillment**: Multiple partial confirmations over time

### Security Requirements
- SHA-256 token validation mandatory
- HTTPS required for all communications
- Merchant can only confirm own terminal's payments
- Amount validation prevents overcharging

### Marketplace Considerations
- Shops array enables proper vendor fund distribution
- Each shop confirmation handled independently
- Supports complex marketplace settlement scenarios

This method is essential for completing two-stage payment flows, providing merchants with flexible capture capabilities while maintaining financial security and compliance requirements.