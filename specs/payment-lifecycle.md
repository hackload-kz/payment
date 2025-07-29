Based on the payment lifecycle flowchart you've provided, here's a technical specification of the internet acquiring process:

## Payment Lifecycle Technical Specification

### Overview
This specification describes the complete payment processing lifecycle in an internet acquiring system, from initial payment creation through final settlement or refund operations.

### Payment Flow States and Transitions

#### 1. Payment Initialization Phase
- **INIT**: Initial payment state upon creation
- **NEW**: Payment transitions to new state after initialization
- **CANCELLED**: Payment can be cancelled from NEW state (система отменяет платеж - system cancels payment)
- **DEADLINE_EXPIRED**: Payment expires if not processed within time limits
- **FORM_SHOWED**: Payment form is displayed to the customer via hosted HTML page

#### 2. Pre-Authorization Phase
- **ONECHOOSEVISION**: First verification step
- **FINISHAUTHORIZE**: Final authorization step
- **AUTHORIZING**: Active authorization process (платеж отправлен банку - payment sent to bank)

#### 3. Authorization Decision Point
From AUTHORIZING state, the system evaluates:
- **ОСТАЛИСЬ ЛИ ПОПЫТКИ?** (Are there attempts remaining?)
  - If no attempts remain → **REJECTED** (платеж отклонен банком - payment rejected by bank)
  - If attempts remain → Continue to card verification

#### 4. Final Authorization
- **ПЛАТЕЖ ПРОШЕЛ УСПЕШНО?** (Payment processed successfully?)
  - Success → **AUTHORIZED**
  - Failure → **AUTH_FAIL**

#### 9. Confirmation Phase
From **AUTHORIZED** state:
- **CONFIRM**: Confirmation process initiated
- **CONFIRMING**: Active confirmation state
- **CONFIRMED**: Payment successfully confirmed

#### 10. Cancellation Handling
- **CANCEL**: Cancellation request from confirmed state
- Cancellation can occur at multiple points in the flow

#### 11. Reversal Operations
- **REVERSING**: Reversal process initiated
- **REVERSED**: Full reversal completion

#### 12. Refund Operations
- **REFUNDING**: Active refund process
- **REFUNDED**: Full refund completion

### Key Technical Notes

1. **Retry Logic**: Built-in retry mechanisms for failed authorization attempts
2. **Full Operations**: Support for complete reversals and refunds only
3. **Deadline Management**: Automatic expiration handling for time-sensitive operations
4. **State Persistence**: Each state transition is tracked for audit and recovery purposes
5. **HTML Payment Interface**: Customers interact with payment forms through hosted HTML pages that handle card data collection and submission securely

### Error Handling
- **REJECTED**: Bank rejection handling
- **AUTH_FAIL**: Authorization failure management
- **DEADLINE_EXPIRED**: Timeout handling at multiple stages

This specification provides the foundation for implementing a robust payment processing system with comprehensive state management, security compliance, and support for various payment scenarios including cancellations, reversals, and refunds.