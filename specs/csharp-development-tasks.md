# C# Payment Gateway Development Task List

This document provides comprehensive tasks for developing the HackLoad 2025 payment gateway using C# ASP.NET Core, based on the payment specifications in this repository.

## Prerequisites and References

**Required Specifications:**
- `@specs/payment-lifecycle.md` - Complete payment state machine and transitions
- `@specs/payment-authentication.md` - SHA-256 token generation algorithm
- `@specs/payment-init.md` - Init endpoint specification and validation rules
- `@specs/payment-confirm.md` - Confirm endpoint for two-stage payments
- `@specs/payment-check.md` - CheckOrder endpoint for status tracking
- `@specs/payment-cancel.md` - Cancel endpoint with reversal/refund logic
- `@specs/payment-errors.md` - Comprehensive error codes and handling specifications

**Technology Stack:**

- C# 12.0+ with ASP.NET Core 8.0+ (using .NET 9.0 SDK)
- PostgreSQL with Entity Framework Core (for data persistence)
- Prometheus.AspNetCore (for metrics collection)
- Serilog with structured logging (for comprehensive event logging)
- System.Text.Json (for JSON serialization)
- xUnit with Moq (for testing)
- Swashbuckle (for OpenAPI documentation)
- Docker (for containerization)
- HTML5, CSS3, JavaScript (for payment form frontend)
- Bootstrap (for responsive UI design)

---

## Task 1: C# ASP.NET Core Project Foundation Setup ✅ COMPLETED

**Priority:** High  
**Dependencies:** None  
**Estimated Time:** 4-6 hours  
**Status:** ✅ **COMPLETED** - All deliverables implemented and tested

### Command for Claude:
```
Create a new C# ASP.NET Core Web API project for the payment gateway with the following requirements:

1. Project structure:
   - Controllers/ (API endpoints)
   - Models/ (DTOs and domain models)
   - Services/ (business logic)
   - Infrastructure/ (data access, external services)
   - Middleware/ (custom middleware components)
   - Extensions/ (service collection extensions)

2. Required NuGet packages:
   - Microsoft.AspNetCore.OpenApi
   - Swashbuckle.AspNetCore
   - Npgsql.EntityFrameworkCore.PostgreSQL
   - Microsoft.EntityFrameworkCore.Design
   - System.Security.Cryptography
   - FluentValidation.AspNetCore
   - Serilog.AspNetCore
   - Serilog.Sinks.PostgreSQL
   - prometheus-net.AspNetCore
   - prometheus-net.AspNetCore.HealthChecks

3. Configuration:
   - appsettings.json with PostgreSQL connection strings and payment gateway settings
   - Program.cs with PostgreSQL, Prometheus metrics, and Serilog registration
   - HTTPS redirection and security headers
   - CORS policy for development
   - Structured logging with Serilog to PostgreSQL
   - Prometheus metrics endpoint configuration

4. Database setup:
   - PostgreSQL connection configuration
   - Entity Framework Core migrations setup
   - Database schema for payments, merchants, and audit logs

5. Docker configuration:
   - Dockerfile for the application
   - docker-compose.yml with PostgreSQL and Prometheus
   - Environment variable configuration for containers

6. Static file serving setup:
   - Configure wwwroot folder for static assets
   - Static file middleware configuration
   - Content security policies for frontend assets

7. Base project structure following Clean Architecture principles
```

### Deliverables:

- ✅ ASP.NET Core 8.0+ Web API project with Clean Architecture
- ✅ PostgreSQL database configuration and connection setup
- ✅ Prometheus metrics integration with custom payment metrics
- ✅ Serilog structured logging configuration with PostgreSQL sink
- ✅ Docker containerization with multi-stage build
- ✅ docker-compose.yml for local development environment
- ✅ Required dependencies installed and configured
- ✅ Basic configuration and startup setup

---

## Task 2: SHA-256 Token Generation and Authentication System ✅ COMPLETED

**Priority:** High  
**Dependencies:** Task 1  
**Estimated Time:** 6-8 hours  
**Status:** ✅ **COMPLETED** - All components implemented with comprehensive tests (21 tests passing)

### Command for Claude:
```
Implement the SHA-256 token generation and authentication system based on @specs/payment-authentication.md:

1. Create TokenGenerationService class that implements the exact algorithm:
   - Extract root-level parameters (exclude nested objects/arrays)
   - Add merchant password to parameters
   - Sort parameters alphabetically by key
   - Concatenate values with UTF-8 encoding
   - Generate SHA-256 hash

2. Create AuthenticationMiddleware that:
   - Validates tokens on all protected endpoints
   - Extracts TerminalKey and validates merchant credentials
   - Handles authentication failures with proper error responses
   - Logs authentication attempts for security auditing

3. Create MerchantService for credential management:
   - Load merchant credentials from configuration/database
   - Validate TerminalKey and Password combinations
   - Support multiple merchants with different credentials

4. Implement merchant credential models:
   - Merchant class with TerminalKey, Password, IsActive properties
   - MerchantCredentials DTO for authentication
   - Secure credential storage (never log passwords)

5. Create comprehensive unit tests for token generation with examples from the spec
```

### Deliverables:
- ✅ `TokenGenerationService` with exact algorithm implementation (matches specification test vector)
- ✅ `AuthenticationMiddleware` for request validation with proper error codes (201, 202, 204, 205)
- ✅ `MerchantService` for credential management with logging and validation
- ✅ Merchant domain models and DTOs (Merchant, MerchantCredentials)
- ✅ Comprehensive unit tests with known test vectors (21 tests passing)
- ✅ Integration with ASP.NET Core authentication pipeline

---

## Task 3: Payment State Machine and Lifecycle Management ✅ COMPLETED

**Priority:** High  
**Dependencies:** Task 1  
**Estimated Time:** 8-10 hours  
**Status:** ✅ **COMPLETED** - Full state machine implemented with 22 status states and comprehensive validation (35 tests passing)

### Command for Claude:
```
Create the payment state machine based on @specs/payment-lifecycle.md:

1. Define PaymentStatus enum with all states from the lifecycle:
   - INIT, NEW, CANCELLED, DEADLINE_EXPIRED, FORM_SHOWED
   - ONECHOOSEVISION, FINISHAUTHORIZE, AUTHORIZING
   - 3DS_CHECKING, 3DS_CHECKED, SUBMITPASSIVIZATION, SUBMITPASSIVIZATION2
   - AUTHORIZED, AUTH_FAIL, REJECTED
   - CONFIRMING, CONFIRMED
   - REVERSING, REVERSED, PARTIAL_REVERSED
   - REFUNDING, REFUNDED, PARTIAL_REFUNDED

2. Create PaymentStateMachine service that:
   - Validates state transitions according to the flowchart
   - Implements transition logic with business rules
   - Tracks state change history with timestamps
   - Handles retry logic and attempt counting
   - Manages deadline expiration automatically

3. Create Payment entity/model:
   - PaymentId, OrderId, TerminalKey, Amount
   - CurrentStatus, StatusHistory with timestamps
   - AttemptCount, MaxAttempts configuration
   - CreatedDate, UpdatedDate, ExpirationDate
   - CustomerKey, PayType (O/T), Description

4. Implement PaymentRepository with PostgreSQL:
   - Entity Framework Core with PostgreSQL provider
   - CRUD operations for payments with transaction support
   - Query by OrderId, PaymentId, TerminalKey with indexing
   - Status change tracking and auditing with event logging
   - Database migrations for payment schema

5. Create comprehensive state transition tests
```

### Deliverables:
- ✅ `PaymentStatus` enum with all 22 lifecycle states from specification
- ✅ `PaymentStateMachine` service with comprehensive transition validation and business rules
- ✅ `PaymentEntity` domain model with full properties (PaymentId, OrderId, status tracking, attempt counting)
- ✅ `PaymentRepository` with PostgreSQL implementation and status history tracking
- ✅ State transition validation logic with attempt counting and deadline management
- ✅ Comprehensive payment lifecycle unit tests (35 tests covering all transitions and edge cases)

---

## Task 4: Init Payment Endpoint Implementation ✅ COMPLETED

**Priority:** High  
**Dependencies:** Tasks 2, 3  
**Estimated Time:** 10-12 hours  
**Status:** ✅ **COMPLETED** - Full endpoint implementation with comprehensive validation and testing (50+ tests passing)

### Command for Claude:
```
Implement the Init payment endpoint based on @specs/payment-init.md:

1. Create InitPaymentRequest DTO with all parameters:
   - Required: TerminalKey, Amount, OrderId, Token
   - Optional: PayType, Description, CustomerKey, Recurrent, Language
   - Optional: NotificationURL, SuccessURL, FailURL, RedirectDueDate
   - Complex objects: DATA (Dictionary<string,string>), Receipt, Shops array, Descriptor

2. Create InitPaymentResponse DTO:
   - Required: TerminalKey, Amount, OrderId, Success, Status, PaymentId, ErrorCode
   - Optional: PaymentURL, Message, Details

3. Implement FluentValidation validators:
   - TerminalKey ≤20 characters, required
   - Amount ≥1000 kopecks (10 RUB), ≤10 chars, required
   - OrderId ≤36 characters, required, unique per operation
   - Token validation using TokenGenerationService
   - DATA object: max 20 key-value pairs, key ≤20 chars, value ≤100 chars
   - All string length validations per specification

4. Create PaymentController with Init endpoint:
   - POST /init with JSON content type
   - Request validation using FluentValidation
   - Token authentication using AuthenticationMiddleware
   - Business logic delegation to PaymentService
   - Proper error handling with structured responses

5. Implement PaymentService.InitializePayment method:
   - Generate unique PaymentId with correlation tracking
   - Create Payment entity with INIT → NEW state transition
   - Handle PaymentURL generation for non-PCI merchants
   - Process DATA object special parameters
   - Save payment to PostgreSQL repository with comprehensive audit trail
   - Log payment initialization events with Prometheus metrics
   - Track request/response events for monitoring

6. Add comprehensive integration tests for all scenarios from the spec
```

### Deliverables:

- ✅ `InitPaymentRequest/Response` DTOs with full validation and all required/optional parameters
- ✅ `InitPaymentRequestValidator` with comprehensive FluentValidation rules (20+ validation scenarios)
- ✅ `PaymentController` with Init endpoint, proper error handling, and structured responses
- ✅ `PaymentService.InitializePaymentAsync` with full business logic, token validation, and state transitions
- ✅ PaymentId generation (20-character unique IDs) and PaymentURL creation for hosted payments
- ✅ PostgreSQL database integration with EF Core and state machine transitions
- ✅ Comprehensive error handling with specific error codes (202, 204, 335, 999)
- ✅ Extensive unit tests covering success/failure scenarios (8 PaymentService tests + 22 validator tests)

---

## Task 5: Confirm Payment Endpoint Implementation

**Priority:** Medium  
**Dependencies:** Task 4  
**Estimated Time:** 8-10 hours

### Command for Claude:
```
Implement the Confirm payment endpoint based on @specs/payment-confirm.md:

1. Create ConfirmPaymentRequest DTO:
   - Required: TerminalKey, PaymentId, Token
   - Optional: IP, Amount, Receipt, Shops, Route, Source

2. Create ConfirmPaymentResponse DTO:
   - Required: TerminalKey, OrderId, Success, Status, PaymentId, ErrorCode
   - Optional: Message, Details, Params (for installment payments)

3. Implement validation rules:
   - PaymentId ≤20 characters, must exist in system
   - Amount must be ≤ original authorized amount (if provided)
   - Payment must be in AUTHORIZED status for confirmation
   - Token validation using existing authentication system

4. Add PaymentService.ConfirmPayment method:
   - Validate payment exists and is in AUTHORIZED status
   - Handle partial confirmation (amount ≤ authorized amount)
   - Implement AUTHORIZED → CONFIRMING → CONFIRMED state transitions
   - Process Receipt object for fiscal compliance
   - Handle Shops array for marketplace payments
   - Update payment amount if partial confirmation

5. Create PaymentController.Confirm endpoint:
   - POST /confirm with authentication
   - Request validation and error handling
   - Business logic delegation to PaymentService
   - Structured error responses per specification

6. Add comprehensive tests for two-stage payment scenarios:
   - Full confirmation of authorized payment
   - Partial confirmation with amount validation
   - Error cases: invalid status, amount exceeded, payment not found
```

### Deliverables:
- `ConfirmPaymentRequest/Response` DTOs
- Payment confirmation validation logic
- `PaymentService.ConfirmPayment` implementation
- `PaymentController.Confirm` endpoint
- Two-stage payment state transitions
- Unit and integration tests for confirmation scenarios

---

## Task 6: CheckOrder Endpoint Implementation

**Priority:** Medium  
**Dependencies:** Task 4  
**Estimated Time:** 6-8 hours

### Command for Claude:
```
Implement the CheckOrder endpoint based on @specs/payment-check.md:

1. Create CheckOrderRequest DTO:
   - Required: TerminalKey, OrderId, Token

2. Create CheckOrderResponse DTO:
   - Required: TerminalKey, OrderId, Success, ErrorCode, Payments array
   - Optional: Message, Details

3. Create PaymentsCheckOrder DTO for payment details:
   - PaymentId, Amount, Status, CreatedDate, CompletedDate
   - ErrorCode (for failed payments), additional payment metadata

4. Implement PaymentService.CheckOrder method:
   - Query all payments by OrderId and TerminalKey
   - Return payment history with status progression
   - Include timestamps and error information
   - Handle multiple payment attempts per OrderId

5. Create PaymentController.CheckOrder endpoint:
   - POST /checkOrder with authentication
   - Request validation and token verification
   - Query payment repository for order history
   - Return structured response with payment attempts

6. Add repository methods for order querying:
   - GetPaymentsByOrderId method
   - Include status history and timestamps
   - Filter by TerminalKey for security

7. Create integration tests for various scenarios:
   - Single successful payment
   - Multiple payment attempts (failed + successful)
   - Order not found error case
   - Payment status progression tracking
```

### Deliverables:
- `CheckOrderRequest/Response` and `PaymentsCheckOrder` DTOs
- `PaymentService.CheckOrder` implementation
- `PaymentController.CheckOrder` endpoint
- Payment history querying logic
- Order tracking and status reporting
- Integration tests for order status scenarios

---

## Task 7: Cancel Payment Endpoint Implementation

**Priority:** Medium  
**Dependencies:** Tasks 4, 5  
**Estimated Time:** 10-12 hours

### Command for Claude:
```
Implement the Cancel endpoint based on @specs/payment-cancel.md:

1. Create CancelPaymentRequest DTO:
   - Required: TerminalKey, PaymentId, Token
   - Optional: IP, Amount, Receipt, Shops, QrMemberId, Route, Source, ExternalRequestId

2. Create CancelPaymentResponse DTO:
   - Required: TerminalKey, OrderId, Success, Status, OriginalAmount, NewAmount, PaymentId, ErrorCode
   - Optional: Message, Details, ExternalRequestId

3. Implement state-dependent cancellation logic:
   - NEW → CANCELLED (full cancellation)
   - AUTHORIZED → REVERSED/PARTIAL_REVERSED (reversal logic)
   - CONFIRMED → REFUNDED/PARTIAL_REFUNDED (refund logic)
   - Validate current status allows cancellation

4. Add PaymentService.CancelPayment method:
   - Load payment and validate current status
   - Determine cancellation type based on status and amount
   - Process partial vs full cancellations
   - Handle ExternalRequestId for idempotency
   - Update payment status and amounts accordingly
   - Process Receipt and Shops data for compliance

5. Create PaymentController.Cancel endpoint:
   - POST /cancel with authentication
   - Request validation and token verification
   - State transition validation
   - Amount validation for partial operations

6. Implement idempotency control:
   - Check ExternalRequestId for duplicate operations
   - Return existing operation state if duplicate found
   - Store ExternalRequestId for future duplicate detection

7. Add comprehensive tests for all cancellation scenarios:
   - Full cancellation from different states
   - Partial reversals and refunds
   - Idempotency validation
   - Error cases: invalid status, amount validation failures
```

### Deliverables:
- `CancelPaymentRequest/Response` DTOs
- State-dependent cancellation logic
- `PaymentService.CancelPayment` implementation
- `PaymentController.Cancel` endpoint
- Idempotency control system
- Comprehensive cancellation tests

---

## Task 8: DTOs and Request/Response Models

**Priority:** Medium  
**Dependencies:** Tasks 1-7  
**Estimated Time:** 6-8 hours

### Command for Claude:
```
Create comprehensive DTOs and models for the payment gateway:

1. Create base response classes:
   - BaseApiResponse with Success, ErrorCode, Message, Details
   - Generic ApiResponse<T> for typed responses
   - ErrorResponse for structured error information

2. Implement complex object DTOs:
   - DataObject DTO (Dictionary<string,string> with validation)
   - ReceiptObject DTO (Receipt_FFD_105, Receipt_FFD_12)
   - ShopsObject DTO for marketplace support
   - Items_Params DTO for installment payments

3. Create validation attributes:
   - MaxLengthAttribute extensions for specific field validations
   - CustomValidationAttributes for business rules
   - EnumValidationAttribute for PayType, Route, Source enums

4. Implement JSON serialization configuration:
   - Property naming policies (camelCase/PascalCase as needed)
   - Date/time format handling (ISO 8601)
   - Null value handling for optional fields
   - Custom converters for enums and complex types

5. Add model binding and validation:
   - FluentValidation rules for all DTOs
   - Custom validation messages matching specification
   - Cross-field validation (e.g., Amount consistency)
   - Conditional validation based on PayType, Route, etc.

6. Create AutoMapper profiles:
   - DTO to domain model mappings
   - Domain model to response DTO mappings
   - Handle complex property transformations
```

### Deliverables:
- Complete DTO hierarchy with base classes
- Complex object DTOs (DATA, Receipt, Shops)
- Validation attributes and FluentValidation rules
- JSON serialization configuration
- AutoMapper profiles for model mapping
- Comprehensive DTO validation tests

---

## Task 9: 3DS Authentication and Card Processing Simulation

**Priority:** Medium  
**Dependencies:** Task 3  
**Estimated Time:** 8-10 hours

### Command for Claude:
```
Implement 3DS authentication and card processing simulation based on @specs/payment-lifecycle.md:

1. Create 3DS authentication service:
   - Detect 3DS support for cards (simulation)
   - Handle 3DS version 1 and version 2 flows
   - Implement SUBMITPASSIVIZATION and SUBMITPASSIVIZATION2 states
   - 3DS_CHECKING → 3DS_CHECKED state transitions

2. Implement BankSimulatorService:
   - Simulate authorization attempts with configurable success/failure rates
   - Handle retry logic with attempt counting
   - Card validation simulation (number, expiry, CVV)
   - Generate authorization codes and reference numbers

3. Create CardProcessingService:
   - AUTHORIZING state processing with bank simulation
   - Handle authorization decision points (attempts remaining)
   - Process 3DS authentication results
   - Final authorization success/failure determination

4. Add payment processing workflow:
   - FORM_SHOWED → ONECHOOSEVISION → FINISHAUTHORIZE flow
   - Integration with state machine for proper transitions
   - Deadline management and expiration handling
   - Authorization failure (AUTH_FAIL) and rejection (REJECTED) handling

5. Create configurable failure modes:
   - Success/failure probability settings
   - Specific card number patterns for testing
   - 3DS authentication success rates
   - Authorization timeout simulation

6. Implement background processing:
   - Async payment processing workflows
   - Status polling and updates
   - Deadline expiration monitoring
   - Notification callbacks (if implemented)
```

### Deliverables:
- `ThreeDSAuthenticationService` with version support
- `BankSimulatorService` for authorization simulation
- `CardProcessingService` for payment workflow
- Configurable failure modes for testing
- Background processing infrastructure
- 3DS and authorization flow tests

---

## Task 10: Comprehensive Error Handling and Validation System

**Priority:** High  
**Dependencies:** All previous tasks  
**Estimated Time:** 10-12 hours

### Command for Claude:
```
Implement comprehensive error handling system based on @specs/payment-errors.md:

1. Create PaymentErrorCode enum with all error codes from specification:
   - System errors (1-99): Parameter mapping, internal errors, card binding
   - Payment processing errors (100-199): 3DS failures, insufficient funds
   - Validation errors (200-399): Required fields, authentication, data size
   - Receipt and fiscal errors (300-399): Receipt validation, marketplace
   - Service errors (400-699): Internal service errors, limits, certificates
   - Bank response errors (1000-1999): Bank communication, card validation
   - SBP errors (3000-5999): Configuration, transaction limits, customer banks
   - BNPL/Installment errors (8000-8999): Credit broker, operation restrictions
   - System errors (9000-9999): Critical system failures

2. Create PaymentErrorService with categorization:
   - Critical errors (immediate action required): 3, 202, 204, 205, 401, 9999
   - User action required: 103, 116, 252, 1014, 1015, 1051, 1082
   - Configuration issues: 3001, 3019, 13, 411-417, 1099
   - Temporary issues (retry recommended): 55, 120, 123, 125, 402, 99
   - Business logic errors: 4, 8, 251, 308, 334, 119, 403-406

3. Implement GlobalExceptionMiddleware with error mapping:
   - Map exceptions to specific error codes from specification
   - Generate structured error responses with Success, ErrorCode, Message, Details
   - Add ErrorContext with category, severity, retry_possible, user_action_required
   - Log exceptions with correlation IDs and security context
   - Never expose sensitive information (PCI DSS compliance)

4. Create comprehensive custom exception hierarchy:
   - PaymentNotFoundException (ErrorCode: 335, 255)
   - InvalidPaymentStatusException (ErrorCode: 4, 8)
   - TokenValidationException (ErrorCode: 204, 205)
   - InsufficientAmountException (ErrorCode: 103, 116, 1051)
   - MerchantAuthenticationException (ErrorCode: 204, 205, 501)
   - CardValidationException (ErrorCode: 508, 1014, 1015, 1082)
   - PaymentLimitExceededException (ErrorCode: 119, 403-406, 1013, 1061)
   - ThreeDSAuthenticationException (ErrorCode: 101, 106, 108, 511)
   - ReceiptValidationException (ErrorCode: 308-320)
   - SBPConfigurationException (ErrorCode: 3001, 3019)

5. Implement ValidationMiddleware with specific error mappings:
   - Required field validation (201): PaymentId, paymentMethod, paymentObject
   - Terminal validation (202): Terminal blocked
   - Authentication validation (204, 205): Invalid token, terminal not found
   - Data size validation (207-209): DATA parameter size limits
   - Field size validation (210-243): TerminalKey, OrderId, Description limits
   - Format validation (211, 224): IP format, email format
   - Amount validation (251): Minimum amount 1000 kopecks
   - Currency validation (253): Terminal currency restrictions

6. Create ErrorResponseFactory:
   - Standard error response structure matching specification
   - Localization support (Russian/English error messages)
   - User-friendly error mapping with titles, messages, actions, icons
   - Error context enhancement for monitoring and analytics
   - Client-side error handling guidance (critical, retry, user action)

7. Add comprehensive error monitoring:
   - Error rate thresholds: >5% critical, >10% auth failures, >15% payment failures
   - Error categorization for Prometheus metrics
   - Alert configurations for different error types
   - Error correlation analysis (3DS failures, foreign cards, receipt validation)
   - Geographic and temporal error pattern tracking

8. Implement security and compliance features:
   - PCI DSS compliant error logging (no sensitive data)
   - Audit trail for all error occurrences
   - Suspicious activity pattern detection (fraud-related errors)
   - Rate limiting per TerminalKey with error code responses
   - Request size limitations with appropriate error codes

9. Create retry logic and circuit breaker patterns:
   - Exponential backoff for retryable errors (55, 120, 123, 125, 402)
   - Circuit breaker for external service errors (96, 97, 98)
   - Dead letter queue for failed payment processing
   - Automatic retry limits with error code 119 (attempt limit exceeded)

10. Add client-side error handling support:
    - JavaScript error categorization constants
    - Error handling strategy by API method (Init, Confirm, Cancel, CheckOrder)
    - User experience guidelines for different error types
    - Error message templates for frontend integration
```

### Deliverables:

- `PaymentErrorCode` enum with all 600+ error codes from specification
- `PaymentErrorService` with error categorization and severity mapping
- `GlobalExceptionMiddleware` with comprehensive error code mapping
- Custom exception hierarchy with specific error code assignments
- `ValidationMiddleware` with field-specific validation error codes
- `ErrorResponseFactory` with localization and user-friendly messaging
- Prometheus metrics integration for error monitoring and alerting
- Client-side error handling utilities and JavaScript constants
- PCI DSS compliant error logging and audit trail system
- Retry logic and circuit breaker implementations for resilient error handling

---

## Task 11: Unit and Integration Tests

**Priority:** Medium  
**Dependencies:** All previous tasks  
**Estimated Time:** 12-15 hours

### Command for Claude:
```
Create comprehensive test suite using NUnit or xUnit:

1. Unit tests for TokenGenerationService:
   - Test with exact examples from @specs/payment-authentication.md
   - Verify SHA-256 hash generation matches specification
   - Test parameter extraction and sorting logic
   - Edge cases: special characters, empty values, different data types

2. Payment state machine tests:
   - All valid state transitions from @specs/payment-lifecycle.md
   - Invalid transition rejection
   - Deadline expiration handling
   - Retry logic and attempt counting

3. Controller integration tests:
   - Full request/response cycles for all endpoints
   - Authentication and token validation
   - Error scenarios and edge cases
   - JSON serialization/deserialization

4. PaymentService unit tests:
   - InitializePayment with all parameter combinations
   - ConfirmPayment partial and full scenarios
   - CancelPayment state-dependent logic
   - CheckOrder multiple payment attempts

5. Validation tests:
   - FluentValidation rules for all DTOs
   - Cross-field validation scenarios
   - Complex object validation (DATA, Receipt, Shops)
   - Error message formatting and localization

6. Create test data factories:
   - Payment test data builders
   - Valid/invalid request generators
   - Mock merchant credentials
   - Test payment scenarios with different states

7. Integration test infrastructure:
   - Test server setup with in-memory database
   - Test authentication and authorization
   - End-to-end payment workflows
   - Performance and load testing basics
```

### Deliverables:
- Complete unit test suite (80%+ code coverage)
- Integration tests for all endpoints
- Payment workflow end-to-end tests
- Test data factories and builders
- Mock services and test infrastructure
- Performance and load testing framework

---

## Task 12: Swagger Documentation and Health Checks

**Priority:** Low  
**Dependencies:** All previous tasks  
**Estimated Time:** 4-6 hours

### Command for Claude:
```
Add OpenAPI documentation and monitoring capabilities:

1. Configure Swashbuckle for OpenAPI generation:
   - Detailed endpoint documentation with examples
   - Request/response schema documentation
   - Authentication requirements documentation
   - Error response documentation with all error codes

2. Add XML documentation comments:
   - Controller action documentation
   - DTO property descriptions
   - Example values from specifications
   - Error code explanations

3. Create interactive API documentation:
   - Swagger UI configuration
   - Try-it-out functionality with authentication
   - Example requests for all endpoints
   - Error scenario examples

4. Implement health checks:
   - Basic application health endpoint
   - Database connectivity verification
   - External service dependencies
   - Memory and performance metrics

5. Add monitoring endpoints:
   - Application metrics (request count, response times)
   - Payment processing statistics
   - Error rate monitoring
   - System resource utilization

6. Create deployment documentation:
   - Configuration requirements
   - Environment variable documentation
   - Security configuration guidelines
   - Monitoring and alerting setup
```

### Deliverables:
- Complete OpenAPI documentation
- Interactive Swagger UI
- Health check endpoints
- Monitoring and metrics endpoints
- Deployment and configuration documentation
- API usage examples and tutorials

---

## Development Guidelines

### Code Quality Standards
- Follow C# coding conventions and naming standards
- Use async/await for all I/O operations
- Implement proper logging and error handling
- Maintain high test coverage (80%+)
- Use dependency injection consistently

### Security Requirements
- Never log sensitive data (passwords, tokens, card data)
- Implement proper input validation and sanitization
- Use HTTPS for all communications
- Follow OWASP security guidelines
- Implement rate limiting and request throttling

### Performance Considerations
- Use efficient data structures and algorithms
- Implement proper caching strategies
- Optimize database queries and data access
- Monitor memory usage and garbage collection
- Implement connection pooling for external services

### Testing Strategy
- Unit tests for all business logic
- Integration tests for API endpoints
- End-to-end tests for payment workflows
- Performance tests for critical paths
- Security tests for authentication and authorization

---

## Task 13: Comprehensive Logging and Monitoring System

**Priority:** High  
**Dependencies:** Task 1  
**Estimated Time:** 8-10 hours

### Command for Claude:

```
Implement comprehensive logging and monitoring system with the following requirements:

1. Create PaymentEventLogger service for comprehensive coverage:
   - API Method Events: Log all request/response/error events for Init, CheckOrder, Cancel, Confirm
   - Payment Lifecycle Events: Log all state transitions (NEW→AUTHORIZING→3DS_CHECKING→CONFIRMED, etc.)
   - Security Events: Log authentication failures, fraud detection, 3DS authentication attempts
   - System Health Events: Log performance metrics, compliance monitoring, error rates

2. Implement Prometheus metrics collection:
   - Custom payment metrics: payment_requests_total, payment_success_rate, payment_processing_duration
   - HTTP metrics: request count, response times, error rates by endpoint
   - Business metrics: payments by status, revenue metrics, failure reasons
   - System metrics: database connection pool, memory usage, GC metrics

3. Create structured logging models:
   - PaymentEventLog: PaymentId, EventType, Status, Timestamp, Duration, ErrorCode
   - SecurityEventLog: EventType, TerminalKey, IPAddress, UserAgent, Success, Timestamp
   - APIEventLog: Endpoint, Method, StatusCode, ResponseTime, RequestSize, ResponseSize
   - HealthEventLog: MetricName, Value, Threshold, Status, Timestamp

4. Implement event correlation:
   - Generate correlation IDs for request tracing
   - Link payment events across the lifecycle
   - Track user sessions and payment flows
   - Correlation with external system calls

5. Add performance monitoring:
   - Database query performance logging
   - External service call monitoring
   - Memory and CPU usage tracking
   - Custom business KPI monitoring

6. Create log aggregation and alerting:
   - Structured JSON logging to PostgreSQL
   - Error rate and performance threshold monitoring
   - Security event alerting (multiple auth failures, suspicious patterns)
   - Business metric alerting (payment failure spike, revenue drops)

7. Implement compliance and audit logging:
   - PCI DSS compliance event logging
   - Financial transaction audit trails
   - Data access and modification logging
   - Regulatory compliance reporting data

8. Add comprehensive dashboards:
   - Grafana dashboard configuration for Prometheus metrics
   - Real-time payment processing monitoring
   - Security event visualization
   - Business intelligence dashboards for payment analytics
```

### Deliverables:

- `PaymentEventLogger` service with comprehensive event tracking
- Prometheus metrics integration with custom payment business metrics
- Structured logging models for all event types
- Event correlation system with request tracing
- Performance monitoring and alerting system
- Compliance and audit logging infrastructure
- Grafana dashboard configurations
- Alert rules for system health and security monitoring

---

## Task 14: Simple Payment HTML Page and Frontend Integration

**Priority:** Medium  
**Dependencies:** Tasks 4, 13  
**Estimated Time:** 6-8 hours

### Command for Claude:

```
Create a simple payment HTML page that allows users to input card details and interact with the payment gateway API:

1. Create payment form HTML page:
   - Clean, responsive design with Bootstrap or similar CSS framework
   - Card input fields: Card Number, Expiry Date (MM/YY), CVV, Cardholder Name
   - Payment amount display and order information
   - Submit button to initiate payment
   - Real-time form validation with JavaScript
   - Loading states and progress indicators

2. Implement JavaScript API integration with comprehensive error handling:
   - Generate payment tokens using the SHA-256 algorithm from the specifications
   - Call Init API endpoint to create payment session
   - Implement error handling based on @specs/payment-errors.md categorization
   - Handle critical errors (stop processing): 3, 202, 204, 205, 501, 648
   - Implement retry logic for temporary errors: 55, 120, 123, 125, 402, 999
   - Show user-friendly messages for actionable errors: 103, 116, 252, 1014, 1015, 1051, 1082
   - Display payment status and results with error context
   - Implement 3DS authentication flow with 3DS-specific error handling (101, 106, 108, 511)
   - Handle payment confirmation for two-stage payments with validation errors

3. Create payment flow demonstration:
   - Sample merchant credentials for testing
   - Different payment scenarios (success, failure, 3DS authentication)
   - Order status checking functionality
   - Payment cancellation demonstration
   - Different payment types (single-stage vs two-stage)

4. Add client-side security features:
   - Input sanitization and validation
   - HTTPS enforcement
   - CSP (Content Security Policy) headers
   - Card number masking and secure input handling
   - Token generation client-side implementation

5. Implement payment status tracking:
   - Real-time payment status updates
   - Order tracking by OrderId
   - Payment history display
   - Error message handling and user feedback
   - Success/failure callback handling

6. Create demo scenarios:
   - Test cards for different outcomes (approved, declined, 3DS required)
   - Merchant dashboard mockup for payment management
   - Different payment amounts and currencies
   - Webhook simulation for payment notifications

7. Add monitoring integration:
   - Client-side error logging
   - Payment attempt tracking
   - User interaction analytics
   - Performance monitoring for payment flow

8. Serve static files from ASP.NET Core:
   - Configure static file middleware
   - Serve HTML, CSS, and JavaScript files
   - Development vs production asset handling
   - Content security and caching headers
```

### Deliverables:

- Responsive HTML payment form with card input fields
- JavaScript implementation of SHA-256 token generation
- Complete payment flow integration (Init → Process → Confirm/Cancel)
- 3DS authentication handling and redirect flow
- Payment status tracking and order management interface
- Demo scenarios with test cards and different outcomes
- Client-side monitoring and error handling
- ASP.NET Core static file serving configuration

---

## Task 15: Docker Containerization and Deployment

**Priority:** Medium  
**Dependencies:** All previous tasks  
**Estimated Time:** 6-8 hours

### Command for Claude:

```
Create Docker containerization and deployment configuration:

1. Create multi-stage Dockerfile:
   - Build stage with SDK for compilation
   - Runtime stage with ASP.NET Core runtime
   - Optimized image size and security
   - Non-root user for security
   - Health check configuration

2. Create docker-compose.yml for development:
   - Payment gateway service with environment variables
   - PostgreSQL database with initialization scripts
   - Prometheus server with configuration
   - Grafana with pre-configured dashboards
   - Volume mounts for persistent data

3. Add production deployment configuration:
   - docker-compose.prod.yml for production
   - Environment-specific configurations
   - SSL/TLS certificate management
   - Load balancer configuration
   - Database backup and recovery setup

4. Implement database migrations:
   - Entity Framework migration scripts
   - Database initialization with seed data
   - Migration rollback procedures
   - Schema versioning strategy

5. Add monitoring and logging configuration:
   - Prometheus configuration files
   - Grafana dashboard definitions
   - Log aggregation setup
   - Alert manager configuration

6. Create deployment scripts:
   - Build and deployment automation
   - Environment validation scripts
   - Database migration automation
   - Health check validation

7. Add security hardening:
   - Container security scanning
   - Secrets management
   - Network security configuration
   - Runtime security policies
```

### Deliverables:

- Multi-stage Dockerfile optimized for production
- docker-compose.yml for development environment
- Production deployment configuration
- Database migration and initialization scripts
- Monitoring and logging container configuration
- Deployment automation scripts
- Security hardening and secrets management
- Documentation for deployment procedures

---

Each task should be completed with full implementation, comprehensive tests, and proper documentation before moving to the next task. The implementation should follow the exact specifications provided in the referenced files.

## Enhanced Requirements Summary

### Logging Requirements (Comprehensive Coverage)

**All API Methods:** Request/response/error events for Init, CheckOrder, Cancel, Confirm  
**Payment Lifecycle:** State transitions from flowchart (NEW→AUTHORIZING→3DS_CHECKING→CONFIRMED, etc.)  
**Security Events:** Authentication failures, fraud detection, 3DS authentication  
**System Health:** Performance metrics, compliance monitoring

### Infrastructure Requirements

**Database:** PostgreSQL with Entity Framework Core  
**Metrics:** Prometheus with custom payment business metrics  
**Logging:** Serilog with structured logging to PostgreSQL  
**Containerization:** Docker with docker-compose for development and production  
**Monitoring:** Grafana dashboards with alerting and health checks

### Frontend Requirements

**Payment Form:** Simple HTML page with card input fields and payment processing  
**API Integration:** JavaScript implementation of token generation and API calls  
**Payment Flow:** Complete user experience from card input to payment completion  
**Security:** Client-side validation, HTTPS enforcement, and secure card handling  
**Monitoring:** Client-side error logging and payment attempt tracking

### Error Handling Requirements

**Comprehensive Error Codes:** 600+ error codes from specification covering all scenarios  
**Error Categorization:** Critical, User Action, Configuration, Temporary, Business Logic  
**Localization:** Russian/English error messages with user-friendly translations  
**Client Integration:** JavaScript error handling with retry logic and user guidance  
**Monitoring:** Error rate thresholds, categorization, and correlation analysis  
**Compliance:** PCI DSS compliant error logging with audit trails and security patterns