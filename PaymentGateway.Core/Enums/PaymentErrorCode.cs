namespace PaymentGateway.Core.Enums;

public enum PaymentErrorCode
{
    // Success Code
    Success = 2000,

    // System and Configuration Errors (1001-1038)
    InvalidParameterFormat = 1001,
    MissingRequiredParameters = 1002,
    CriticalSystemError = 1003,
    InvalidStateTransition = 1004,
    TechnicalSupportRequired = 1005,
    CardBindingFailed = 1006,
    InvalidCustomerStatus = 1007,
    InvalidTransactionStatus = 1008,
    RedirectUrlMissing = 1009,
    ChargeMethodBlocked = 1010,
    PaymentExecutionImpossible = 1011,
    InvalidRedirectExpiration = 1012,
    MobilePaymentUnavailable = 1013,
    WebMoneyUnavailable = 1014,
    InvalidPaymentData = 1015,
    EinvPaymentFailed = 1016,
    InvoiceRejected = 1017,
    InvalidInputData = 1018,
    MasterCardPaymentFailed = 1019,
    WebMoneyPaymentFailed = 1020,
    DuplicateOrderId = 1021,
    AcqApiServiceError = 1022,
    CashRegisterReactivationUnavailable = 1023,
    NotificationSendingFailed = 1024,
    EmailSendingFailed = 1025,
    SmsSendingFailed = 1026,
    ContactMerchant = 1027,
    Repeat3dsNotAllowed = 1028,
    ServiceTemporarilyUnavailable = 1029,
    DocumentUrlAccessForbidden = 1030,
    EmailOrUrlRequired = 1031,
    SystemIdDocumentAccessForbidden = 1032,
    OperationNotFound = 1033,
    InvalidRequestData = 1034,
    DocumentGenerationFailed = 1035,
    DocumentGenerationRetryRequired = 1036,
    DocumentGenerationTemporaryFailure = 1037,
    ExternalServiceUnavailable = 1038,

    // Foreign Card Restrictions (131, 222, 313, 404)
    ForeignCardNotAccepted = 131,
    ForeignCardPaymentBlocked = 222,
    ForeignCardPayoutProhibited = 313,
    ForeignCardRefundUnavailable = 404,

    // Payment Processing Errors (140-908)
    ThreeDsAuthenticationFailed = 140,
    SupportContactRequired = 251,
    InsufficientAccountFunds = 342,
    RecurringPaymentError = 433,
    MaestroAutopaymentSetupRequired = 524,
    CardDoesNotSupport3ds = 615,
    InvalidCardId = 706,
    ThreeDsOnlyPaymentRequired = 817,
    ThreeDsSessionIdNotFound = 908,
    ThreeDsChallengeResponseMissing = 199,
    Invalid3dsChallengeResponse = 280,
    InsufficientCardFunds = 371,
    AuthorizationAttemptLimitExceeded = 462,
    TemporaryProcessingIssue = 553,
    TemporaryProcessingIssue2 = 644,
    TemporaryProcessingIssue3 = 735,
    InvalidContractStatus = 826,
    RepeatOperationLater = 859,

    // Validation Errors (117-917)
    PaymentIdRequired = 917,
    PaymentMethodRequired = 117,
    PaymentObjectRequired = 208,
    MeasurementUnitRequired = 359,
    TerminalBlocked = 440,
    RequestParametersRequired = 531,
    InvalidToken = 622,
    TerminalNotFound = 713,
    EmailRequired = 804,

    // Data Size Validation (186-895)
    DataParameterTooLarge = 895,
    DataKeyNameTooLong = 186,
    DataKeyValueTooLong = 277,

    // Field Size Validation (368-904)
    InvalidTerminalKeyLength = 368,
    InvalidIpFormat = 459,
    InvalidOrderIdLength = 540,
    InvalidDescriptionLength = 631,
    InvalidCurrencyValue = 722,
    InvalidPayFormLength = 813,
    InvalidCustomerKeyLength = 904,

    // Numeric and Format Validation (195-913)
    InvalidPaymentIdFormat = 195,
    CardNumberDigitsOnly = 286,
    InvalidCardExpiry = 377,
    InvalidCardHolderLength = 468,
    CvvDigitsOnly = 559,
    InvalidEmailFormat = 640,
    AmountTooSmall = 731,
    CardExpired = 822,
    CurrencyNotSupported = 913,

    // Receipt and Fiscal Errors (114-921)
    ReceiptTotalMismatch = 114,
    ReceiptRequired = 205,
    QuantityPrecisionExceeded = 356,
    ReceiptRegistrationError = 447,
    ReceiptRetrievalError = 538,
    OrganizationCreationError = 629,
    CashRegisterCreationError = 720,
    CashRegisterNotFound = 811,
    InvalidAgentSign = 902,
    AgentSignRequired = 193,
    SupplierInfoRequired = 284,
    SupplierInnRequired = 375,
    ShopFeeValidation = 466,
    ShopAmountValidation = 557,
    ReceiptPaymentAmountMismatch = 648,
    OrderNotFound = 739,
    MarkProcessingModeRequired = 830,
    MarkCodeRequired = 921,
    MarkQuantityRequired = 112,
    MarkQuantityForMarkedGoodsOnly = 203,
    MarkQuantityForFractionalCalculation = 354,

    // Service and Limit Errors (2001-2048)
    CriticalInternalSystemError = 2001,
    ServiceTemporarilyUnavailable2 = 2002,
    MonthlyTopupLimitExceeded = 2003,
    ContactlessTopupLimitExceeded = 2004,
    VirtualCardTopupLimitExceeded = 2005,
    MobileAppMonthlyLimitExceeded = 2006,
    CertificateNotFound = 2011,
    CertificateExpired = 2012,
    CertificateBlocked = 2013,
    CertificateAlreadySaved = 2014,
    CertificateNotYetValid = 2015,
    CertificateProcessingError = 2017,
    CardBindingForbidden = 2020,
    TerminalNotFound2 = 2021,
    CardByRequestKeyNotFound = 2022,
    CustomerKeyNotFound = 2023,
    PaymentDuringCardBindingFailed = 2024,
    CardBindingInternalError = 2025,
    CardBlacklisted = 2026,
    CardDoesNotSupport3ds2 = 2027,
    InvalidCardNumber = 2028,
    CardAlreadyBound = 2030,
    ThreeDsVerificationFailed = 2031,
    InvalidHoldAmount = 2034,
    CardBlacklistedByBank = 2040,
    MerchantRejectedCard = 2041,
    MasterCardOnly = 2042,
    CardAttemptLimitExceeded = 2043,
    SenderDataRequired = 2044,
    AmountCannotBeZero = 2045,
    OperationAmountLimitExceeded = 2046,
    CardFailedLuhnCheck = 2047,
    ShopBlockedOrNotActivated = 2048,

    // Bank Response Errors (3001-3029)
    ContactIssuingBank = 3001,
    InvalidMerchantId = 3002,
    SuspiciousPayment = 3003,
    BankRejectedPayment = 3004,
    InvalidCard = 3005,
    InvalidCardNumber2 = 3006,
    CardExpired2 = 3007,
    CardExpiredNotUsable = 3008,
    IncorrectExpiryDateEntered = 3009,
    InvalidCvv = 3010,
    AmountExceedsCardLimit = 3011,
    InsufficientCardFunds2 = 3012,
    CustomerPaymentLimitExceeded = 3013,
    CustomerPaymentLimitExceeded2 = 3014,
    DailyPaymentLimitExceeded = 3015,
    CardReportedLost = 3016,
    CustomerRestrictedOperations = 3017,
    CustomerRestrictedOperations2 = 3018,
    SuspiciousPayment2 = 3019,
    SuspiciousPayment3 = 3020,
    UnknownPaymentStatus = 3021,
    TokenExpired = 3022,
    OperationSuccessful = 3023,
    SystemError = 3024,
    PaymentMethodDisabled = 3025,
    SingleOperationLimitExceeded = 3026,
    OperationLimitsExceeded = 3027,
    DailyTurnoverLimitReached = 3028,
    CountryCardRestriction = 3029,

    // BNPL and Installment Errors (7000-7999)
    InstallmentOperationForbidden = 7149,
    CreditBrokerUnavailable = 7332,
    BnplOperationForbidden = 7581,
    BnplServiceUnavailable = 7798,
    InstallmentCreditLimitExceeded = 7023,
    BnplScoringCheckFailed = 7456,
    ProductNotEligibleForInstallments = 7687,
    BnplMinimumAmountNotMet = 7291,
    ActiveInstallmentLimitExceeded = 7834,
    PhoneVerificationRequiredForBnpl = 7512,
    CustomerDocumentsNeedVerification = 7643,
    CustomerAgeDoesNotMeetCreditRequirements = 7178,
    CustomerRegionNotSupportedForInstallments = 7925,
    PartnerBankDeclinedInstallmentApplication = 7367,
    TimeoutWaitingForCreditSystemResponse = 7754,

    // System Errors (9001-9009)
    TemporarySystemIssue = 9001,
    InternalSystemError = 9999,

    // Custom Payment Gateway Errors (9002-9009)
    TerminalAccessDenied = 9002,
    TokenAuthenticationFailed = 9003,
    PaymentNotFound = 9004,
    RequestValidationFailed = 9005,
    DuplicateOrderOperation = 9006,
    InternalRequestProcessingError = 9007,
    InvalidPaymentStatusForOperation = 9008,
    OperationAmountLimitExceeded2 = 9009
}

public enum ErrorCategory
{
    System,
    Authentication,
    Validation,
    BankRejection,
    InsufficientFunds,
    CardIssues,
    LimitExceeded,
    TemporaryIssues,
    BusinessLogic,
    Configuration,
    Critical,
    UserAction
}

public enum ErrorSeverity
{
    Critical,
    High,
    Medium,
    Low,
    Info
}

public static class PaymentErrorCodeExtensions
{
    private static readonly Dictionary<PaymentErrorCode, ErrorCategory> ErrorCategoryMap = new()
    {
        // System Errors
        { PaymentErrorCode.CriticalSystemError, ErrorCategory.Critical },
        { PaymentErrorCode.InternalSystemError, ErrorCategory.Critical },
        { PaymentErrorCode.CriticalInternalSystemError, ErrorCategory.Critical },
        { PaymentErrorCode.SystemError, ErrorCategory.Critical },

        // Authentication Errors
        { PaymentErrorCode.InvalidToken, ErrorCategory.Authentication },
        { PaymentErrorCode.TokenAuthenticationFailed, ErrorCategory.Authentication },
        { PaymentErrorCode.TerminalNotFound, ErrorCategory.Authentication },
        { PaymentErrorCode.TerminalNotFound2, ErrorCategory.Authentication },
        { PaymentErrorCode.TerminalAccessDenied, ErrorCategory.Authentication },

        // Validation Errors
        { PaymentErrorCode.InvalidParameterFormat, ErrorCategory.Validation },
        { PaymentErrorCode.MissingRequiredParameters, ErrorCategory.Validation },
        { PaymentErrorCode.RequestValidationFailed, ErrorCategory.Validation },
        { PaymentErrorCode.InvalidRequestData, ErrorCategory.Validation },

        // Bank Rejections
        { PaymentErrorCode.BankRejectedPayment, ErrorCategory.BankRejection },
        { PaymentErrorCode.ContactIssuingBank, ErrorCategory.BankRejection },
        { PaymentErrorCode.MerchantRejectedCard, ErrorCategory.BankRejection },

        // Insufficient Funds
        { PaymentErrorCode.InsufficientCardFunds, ErrorCategory.InsufficientFunds },
        { PaymentErrorCode.InsufficientCardFunds2, ErrorCategory.InsufficientFunds },
        { PaymentErrorCode.InsufficientAccountFunds, ErrorCategory.InsufficientFunds },

        // Card Issues
        { PaymentErrorCode.CardExpired, ErrorCategory.CardIssues },
        { PaymentErrorCode.CardExpired2, ErrorCategory.CardIssues },
        { PaymentErrorCode.InvalidCardNumber, ErrorCategory.CardIssues },
        { PaymentErrorCode.InvalidCardNumber2, ErrorCategory.CardIssues },
        { PaymentErrorCode.InvalidCvv, ErrorCategory.CardIssues },

        // Limit Exceeded
        { PaymentErrorCode.AmountExceedsCardLimit, ErrorCategory.LimitExceeded },
        { PaymentErrorCode.CustomerPaymentLimitExceeded, ErrorCategory.LimitExceeded },
        { PaymentErrorCode.DailyPaymentLimitExceeded, ErrorCategory.LimitExceeded },
        { PaymentErrorCode.OperationLimitsExceeded, ErrorCategory.LimitExceeded },

        // Temporary Issues
        { PaymentErrorCode.ServiceTemporarilyUnavailable, ErrorCategory.TemporaryIssues },
        { PaymentErrorCode.ServiceTemporarilyUnavailable2, ErrorCategory.TemporaryIssues },
        { PaymentErrorCode.TemporaryProcessingIssue, ErrorCategory.TemporaryIssues },
        { PaymentErrorCode.TemporarySystemIssue, ErrorCategory.TemporaryIssues },

        // Business Logic
        { PaymentErrorCode.InvalidStateTransition, ErrorCategory.BusinessLogic },
        { PaymentErrorCode.InvalidPaymentStatusForOperation, ErrorCategory.BusinessLogic },
        { PaymentErrorCode.DuplicateOrderId, ErrorCategory.BusinessLogic },
        { PaymentErrorCode.DuplicateOrderOperation, ErrorCategory.BusinessLogic },

        // Configuration
        { PaymentErrorCode.TerminalBlocked, ErrorCategory.Configuration },
        { PaymentErrorCode.PaymentMethodDisabled, ErrorCategory.Configuration },
        { PaymentErrorCode.ChargeMethodBlocked, ErrorCategory.Configuration }
    };

    private static readonly Dictionary<PaymentErrorCode, ErrorSeverity> ErrorSeverityMap = new()
    {
        // Critical
        { PaymentErrorCode.CriticalSystemError, ErrorSeverity.Critical },
        { PaymentErrorCode.InternalSystemError, ErrorSeverity.Critical },
        { PaymentErrorCode.CriticalInternalSystemError, ErrorSeverity.Critical },
        { PaymentErrorCode.SystemError, ErrorSeverity.Critical },

        // High
        { PaymentErrorCode.InvalidToken, ErrorSeverity.High },
        { PaymentErrorCode.TokenAuthenticationFailed, ErrorSeverity.High },
        { PaymentErrorCode.TerminalAccessDenied, ErrorSeverity.High },
        { PaymentErrorCode.PaymentNotFound, ErrorSeverity.High },

        // Medium
        { PaymentErrorCode.InvalidParameterFormat, ErrorSeverity.Medium },
        { PaymentErrorCode.RequestValidationFailed, ErrorSeverity.Medium },
        { PaymentErrorCode.InsufficientCardFunds, ErrorSeverity.Medium },
        { PaymentErrorCode.CardExpired, ErrorSeverity.Medium },

        // Low
        { PaymentErrorCode.ServiceTemporarilyUnavailable, ErrorSeverity.Low },
        { PaymentErrorCode.TemporaryProcessingIssue, ErrorSeverity.Low }
    };

    private static readonly HashSet<PaymentErrorCode> RetryableErrors = new()
    {
        PaymentErrorCode.ServiceTemporarilyUnavailable,
        PaymentErrorCode.ServiceTemporarilyUnavailable2,
        PaymentErrorCode.TemporaryProcessingIssue,
        PaymentErrorCode.TemporaryProcessingIssue2,
        PaymentErrorCode.TemporaryProcessingIssue3,
        PaymentErrorCode.TemporarySystemIssue,
        PaymentErrorCode.ExternalServiceUnavailable,
        PaymentErrorCode.RepeatOperationLater
    };

    public static ErrorCategory GetCategory(this PaymentErrorCode errorCode)
    {
        return ErrorCategoryMap.TryGetValue(errorCode, out var category) ? category : ErrorCategory.System;
    }

    public static ErrorSeverity GetSeverity(this PaymentErrorCode errorCode)
    {
        return ErrorSeverityMap.TryGetValue(errorCode, out var severity) ? severity : ErrorSeverity.Medium;
    }

    public static bool IsRetryable(this PaymentErrorCode errorCode)
    {
        return RetryableErrors.Contains(errorCode);
    }

    public static bool IsCritical(this PaymentErrorCode errorCode)
    {
        return errorCode.GetSeverity() == ErrorSeverity.Critical;
    }

    public static bool RequiresUserAction(this PaymentErrorCode errorCode)
    {
        var userActionCodes = new[]
        {
            PaymentErrorCode.InsufficientCardFunds,
            PaymentErrorCode.InsufficientCardFunds2,
            PaymentErrorCode.CardExpired,
            PaymentErrorCode.CardExpired2,
            PaymentErrorCode.InvalidCardNumber,
            PaymentErrorCode.InvalidCardNumber2,
            PaymentErrorCode.InvalidCvv,
            PaymentErrorCode.ContactIssuingBank,
            PaymentErrorCode.CardReportedLost
        };

        return userActionCodes.Contains(errorCode);
    }

    public static bool RequiresSupportContact(this PaymentErrorCode errorCode)
    {
        var supportContactCodes = new[]
        {
            PaymentErrorCode.TechnicalSupportRequired,
            PaymentErrorCode.CriticalSystemError,
            PaymentErrorCode.InternalSystemError,
            PaymentErrorCode.SupportContactRequired,
            PaymentErrorCode.ContactMerchant
        };

        return supportContactCodes.Contains(errorCode);
    }
}