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
- **FORM_SHOWED**: Payment form is displayed to the customer

#### 2. Pre-Authorization Phase
- **ONECHOOSEVISION**: First verification step (PSI DSS compliant)
- **FINISHAUTHORIZE**: Final authorization step (PSI DSS compliant)
- **AUTHORIZING**: Active authorization process (платеж отправлен банку - payment sent to bank)

#### 3. Authorization Decision Point
From AUTHORIZING state, the system evaluates:
- **ОСТАЛИСЬ ЛИ ПОПЫТКИ?** (Are there attempts remaining?)
  - If no attempts remain → **REJECTED** (платеж отклонен банком - payment rejected by bank)
  - If attempts remain → Continue to card verification

#### 4. Card Verification Phase
- **КАРТА ПОДДЕРЖИВАЕТ 3DS?** (Does card support 3DS?)
  - If yes → **3DS_CHECKING** process
  - If no → Skip 3DS verification

#### 5. 3DS Processing
- **3DS_CHECKING**: Performing 3DS verification
- **3DS ВТОРОЙ ВЕРСИИ?** (3DS version 2?)
  - Version 1: **SUBMITPASSIVIZATION** (PSI DSS)
  - Version 2: **SUBMITPASSIVIZATION2** (PSI DSS)

#### 6. Authentication Flow
- **Была попытка аутентификации?** (Was there an authentication attempt?)
  - Success path → **3DS_CHECKED**
  - Failure path → **DEADLINE_EXPIRED** (свои попытки прохождения 3ds аутентификации закончились)

#### 7. Post-Authorization States
- **3DS_CHECKED**: 3DS verification completed
- **ПРОБЛЕМА 3DS ПРОШЛА УСПЕШНО?** (3DS problem resolved successfully?)
  - Success → **AUTH_FAIL**
  - Failure → Return to authorization retry loop

#### 8. Final Authorization
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
- **ПЕРВАЯ ОТМЕНА?** (First cancellation?)
  - If yes → **REVERSING**
  - If no → Alternative flow
- **REVERSED**: Successful reversal completion
- **ПЛАТЕЖ ОТМЕНЕН ПОЛНОСТЬЮ?** (Payment completely cancelled?)
- **PARTIAL_REVERSED**: Partial reversal state

#### 12. Refund Operations
- **REFUNDING**: Active refund process
- **REFINED**: Refund processing state  
- **ПЕРВЫЙ ВОЗВРАТ?** (First refund?)
- **ПЛАТЕЖ ВОЗВРАЩЕН ПОЛНОСТЬЮ?** (Payment completely refunded?)
- **PARTIAL_REFUNDED**: Partial refund completion (частичный возврат)

### Key Technical Notes

1. **PSI DSS Compliance**: Multiple states explicitly maintain PSI DSS compliance standards
2. **3DS Support**: Full support for both 3DS version 1 and version 2 protocols
3. **Retry Logic**: Built-in retry mechanisms for failed authorization attempts
4. **Partial Operations**: Support for both partial reversals and partial refunds
5. **Deadline Management**: Automatic expiration handling for time-sensitive operations
6. **State Persistence**: Each state transition is tracked for audit and recovery purposes

### Error Handling
- **REJECTED**: Bank rejection handling
- **AUTH_FAIL**: Authorization failure management
- **DEADLINE_EXPIRED**: Timeout handling at multiple stages

This specification provides the foundation for implementing a robust payment processing system with comprehensive state management, security compliance, and support for various payment scenarios including cancellations, reversals, and refunds.