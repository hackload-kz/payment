# Init Function Technical Specification

## Overview
The Init function initializes a payment session in the internet acquiring system, creating the entry point for payment processing workflow as shown in the lifecycle diagram (transitioning from INIT to NEW state).

## Function Signature
```
POST /init
Content-Type: application/json
```

## Required Parameters

### Core Payment Parameters
- **TerminalKey** (string, ≤20 chars, required)
  - Terminal identifier issued to merchant in I-Business
  - Used for authentication and routing

- **Amount** (number, ≤10 chars, required)
  - Payment amount in kopecks (e.g., 312 for 3.12 RUB)
  - Must equal sum of all Items.Amount parameters
  - Minimum: 1000 kopecks (10 RUB)

- **OrderId** (string, ≤36 chars, required)
  - Unique order identifier in merchant system
  - Must be unique per operation for idempotency

- **Token** (string, required)
  - Request signature for security validation
  - Generated using merchant's secret key and request parameters

## Optional Parameters

### Payment Configuration
- **PayType** (enum: "O"|"T")
  - O: Single-stage payment (immediate capture)
  - T: Two-stage payment (authorization + capture)
  - Defaults to terminal configuration if not specified

- **Description** (string, ≤140 chars)
  - Order description displayed on payment form
  - Required for SBP binding and simultaneous payment
  - Shown in customer's mobile banking app for SBP

### Customer Management
- **CustomerKey** (string, ≤36 chars)
  - Customer identifier in merchant system
  - Required if Recurrent = "Y"
  - Enables card saving functionality for one-click payments
  - Optional for SBP recurring payments

- **Recurrent** (string, ≤1 char)
  - "Y": Registers payment as recurring parent
  - Generates RebillId in AUTHORIZED notification for future Charge operations
  - Required for SBP binding and simultaneous payment

### Localization
- **Language** (string, ≤2 chars)
  - "ru": Russian (default)
  - "en": English
  - Controls payment form language

### URL Configuration
- **NotificationURL** (string, URI format)
  - Webhook endpoint for payment status notifications
  - Overrides terminal default configuration

- **SuccessURL** (string, URI format)
  - Redirect URL for successful payments
  - Overrides terminal default configuration

- **FailURL** (string, URI format)
  - Redirect URL for failed payments
  - Overrides terminal default configuration

### Session Management
- **RedirectDueDate** (datetime, ISO 8601 format)
  - Payment link/QR code expiration time
  - Format: YYYY-MM-DDTHH24:MI:SS+GMT
  - Range: 1 minute to 90 days from current time
  - Default: 24 hours for payments, 30 days for invoices
  - Falls back to terminal REDIRECT_TIMEOUT setting

## Complex Parameters

### DATA Object
JSON object for additional operation parameters and settings (max 20 key-value pairs):

**Key-Value Constraints:**
- Key: ≤20 characters
- Value: ≤100 characters

**Special Parameters:**
- **Phone** (required for MCC 4814): 7-20 characters, digits only, optional leading +
- **account** (required for MCC 6051/6050): Electronic wallet number, ≤30 characters
- **DefaultCard**: Controls default card selection
  - "none": Empty payment form
  - CardId: Specific saved card
  - Default: Last saved card
- **QR**: Set to "true" for SBP binding and simultaneous payment

**iPay Device Parameters:**
```json
{
  "iPayWeb": "true",
  "Device": "Desktop|Mobile",
  "DeviceOs": "iOS|Android|macOS|Windows|Linux",
  "DeviceWebView": "true|false",
  "DeviceBrowser": "Chrome|Firefox|Safari|..."
}
```

**Notification Control:**
- **notificationEnableSource**: Comma-separated list of payment sources for notifications (e.g., "iPay,sbpqr")

**Operation Initiator:**
- **OperationInitiatorType**: Must align with Recurrent and RebillId values per specification table

### Receipt Object
JSON object containing fiscal receipt data:
- **Receipt_FFD_105**: For FFD 1.05 format
- **Receipt_FFD_12**: For FFD 1.2 format
- Required when online cash register is connected

### Shops Array
JSON array with marketplace data:
- Required for marketplace implementations
- Contains shop-specific transaction details

### Descriptor
- **Descriptor** (string): Dynamic merchant descriptor for payment display

## Technical Implementation Notes

### State Transition
- Creates payment in INIT state
- Immediately transitions to NEW state upon successful initialization
- Enables transition to FORM_SHOWED when payment form is displayed

### Security Requirements
- Token validation using merchant secret key
- HTTPS required for all communications
- PCI DSS compliance for card data handling

### Error Handling
- Returns structured error responses with specific error codes
- Error 1126: Incompatible OperationInitiatorType with Recurrent/RebillId values

### Integration Considerations
- Supports both embedded and redirect payment flows
- Compatible with 3DS v1 and v2 authentication
- Enables SBP (Faster Payment System) integration
- Supports recurring payment setup
- Facilitates marketplace transaction processing

This function serves as the foundation for all payment operations, establishing the session context and configuration that governs the entire payment lifecycle flow.

## Response Format

### Response Schema
```json
Content-Type: application/json
```

### Response Parameters

#### Required Fields
- **TerminalKey** (string, ≤20 chars, required)
  - Terminal identifier issued to merchant in I-Business
  - Echoed from request for validation

- **Amount** (number, ≤20 chars, required)
  - Payment amount in kopecks
  - Echoed from request for confirmation

- **OrderId** (string, ≤36 chars, required)
  - Order identifier from merchant system
  - Echoed from request for correlation

- **Success** (boolean, required)
  - Request processing result
  - `true`: Successful initialization
  - `false`: Initialization failed

- **Status** (string, ≤20 chars, required)
  - Current transaction status
  - Typically "NEW" for successful initialization
  - Maps to payment lifecycle states from the flowchart

- **PaymentId** (string, ≤20 chars, required)
  - Unique payment identifier in I-Business system
  - Used for all subsequent payment operations
  - Generated by the acquiring system

- **ErrorCode** (string, ≤20 chars, required)
  - Error code indicator
  - "0": Success
  - Non-zero: Specific error code for debugging

#### Optional Fields
- **PaymentURL** (string, URI format, ≤100 chars)
  - Direct link to payment form
  - **Only returned for merchants without PCI DSS certification**
  - Used for redirect-based payment flows
  - Enables customer redirection to hosted payment page

- **Message** (string, ≤255 chars)
  - Brief error description
  - Human-readable error summary
  - Present when Success = false

- **Details** (string)
  - Detailed error description
  - Technical error information for debugging
  - Additional context for error resolution

### Response Examples

#### Successful Initialization
```json
{
  "TerminalKey": "1234567890",
  "Amount": 150000,
  "OrderId": "order-12345",
  "Success": true,
  "Status": "NEW",
  "PaymentId": "987654321",
  "ErrorCode": "0",
  "PaymentURL": "https://securepay.ibank.com/pay/form/987654321"
}
```

#### Failed Initialization
```json
{
  "TerminalKey": "1234567890",
  "Amount": 150000,
  "OrderId": "order-12345",
  "Success": false,
  "Status": "ERROR",
  "PaymentId": "",
  "ErrorCode": "1001",
  "Message": "Invalid terminal key",
  "Details": "Terminal key '1234567890' is not found or inactive"
}
```

### Integration Notes
- PaymentId must be stored for tracking payment status changes
- PaymentURL should be used immediately for non-PCI DSS merchants
- ErrorCode "0" always indicates successful operation
- Status field corresponds to payment lifecycle states in the flowchart

This function serves as the foundation for all payment operations, establishing the session context and configuration that governs the entire payment lifecycle flow.