using FluentValidation;
using PaymentGateway.Core.DTOs.PaymentInit;
using PaymentGateway.Core.DTOs.PaymentConfirm;
using PaymentGateway.Core.DTOs.PaymentCancel;
using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;

namespace PaymentGateway.Core.Validation.BusinessRules;

/// <summary>
/// Business rule validator for payment operations
/// </summary>
public class PaymentBusinessRuleValidator : AbstractValidator<PaymentInitRequestDto>
{
    public PaymentBusinessRuleValidator()
    {
        // Amount-based business rules
        RuleFor(x => x.Amount)
            .Must(BeWithinDailyLimit)
            .WithErrorCode("DAILY_LIMIT_EXCEEDED")
            .WithMessage("Payment amount exceeds daily limit")
            .Must(BeWithinTransactionLimit)
            .WithErrorCode("TRANSACTION_LIMIT_EXCEEDED")
            .WithMessage("Payment amount exceeds single transaction limit");

        // Currency-based business rules
        RuleFor(x => x.Currency)
            .Must(BeSupportedForTeam)
            .WithErrorCode("CURRENCY_NOT_SUPPORTED_FOR_TEAM")
            .WithMessage("Currency is not supported for this team");

        // Time-based business rules
        RuleFor(x => x.PaymentExpiry)
            .Must(BeWithinAllowedExpiryRange)
            .WithErrorCode("EXPIRY_TIME_NOT_ALLOWED")
            .WithMessage("Payment expiry time is not within allowed range for this team");

        // Customer-based business rules
        RuleFor(x => x.CustomerKey)
            .Must(BeValidCustomerForTeam)
            .WithErrorCode("CUSTOMER_NOT_VALID_FOR_TEAM")
            .WithMessage("Customer is not valid for this team")
            .When(x => !string.IsNullOrEmpty(x.CustomerKey));

        // Order-based business rules
        RuleFor(x => x.OrderId)
            .Must(BeUniqueForTeam)
            .WithErrorCode("ORDER_ID_ALREADY_EXISTS")
            .WithMessage("OrderId already exists for this team");

        // Item-based business rules
        RuleFor(x => x.Items)
            .Must(HaveValidItemConfiguration)
            .WithErrorCode("ITEMS_CONFIGURATION_INVALID")
            .WithMessage("Items configuration is not valid for this payment type")
            .When(x => x.Items != null && x.Items.Any());

        // Receipt-based business rules
        RuleFor(x => x.Receipt)
            .Must(BeRequiredForTeam)
            .WithErrorCode("RECEIPT_REQUIRED_FOR_TEAM")
            .WithMessage("Receipt is required for this team")
            .When(x => x.Receipt != null || IsReceiptRequiredForAmount(x.Amount));
    }

    private bool BeWithinDailyLimit(decimal amount)
    {
        // Business rule: Check against team's daily limit
        // This would typically query the database for team limits and current usage
        var dailyLimit = 1000000m; // 10,000 RUB in kopecks - would come from team config
        return amount <= dailyLimit;
    }

    private bool BeWithinTransactionLimit(decimal amount)
    {
        // Business rule: Check against team's single transaction limit
        var transactionLimit = 500000m; // 5,000 RUB in kopecks
        return amount <= transactionLimit;
    }

    private bool BeSupportedForTeam(string currency)
    {
        // Business rule: Check if currency is supported for the team
        var supportedCurrencies = new[] { "KZT", "USD", "EUR", "BYN", "RUB" };
        return supportedCurrencies.Contains(currency?.ToUpperInvariant());
    }

    private bool BeWithinAllowedExpiryRange(int expiryMinutes)
    {
        // Business rule: Team-specific expiry time limits
        return expiryMinutes >= 5 && expiryMinutes <= 1440; // 5 minutes to 24 hours
    }

    private bool BeValidCustomerForTeam(string? customerKey)
    {
        if (string.IsNullOrEmpty(customerKey))
            return true;

        // Business rule: Customer must be valid for the team
        // This would typically check against customer database
        return customerKey.Length >= 3; // Simplified check
    }

    private bool BeUniqueForTeam(string orderId)
    {
        // Business rule: OrderId must be unique within team
        // This would typically query the database
        return !string.IsNullOrEmpty(orderId);
    }

    private bool HaveValidItemConfiguration(List<OrderItemDto>? items)
    {
        if (items == null || !items.Any())
            return true;

        // Business rule: Items must have valid configuration
        foreach (var item in items)
        {
            if (item.Quantity <= 0 || item.Price <= 0)
                return false;

            // Check for valid tax configuration if provided
            if (!string.IsNullOrEmpty(item.Tax))
            {
                var validTaxTypes = new[] { "none", "vat0", "vat10", "vat20", "vat110", "vat120" };
                if (!validTaxTypes.Contains(item.Tax.ToLowerInvariant()))
                    return false;
            }
        }

        return true;
    }

    private bool BeRequiredForTeam(ReceiptDto? receipt)
    {
        // Business rule: Some teams require receipts for all payments
        // This would typically check team configuration
        return receipt != null;
    }

    private bool IsReceiptRequiredForAmount(decimal amount)
    {
        // Business rule: Receipts required for payments over certain amount
        return amount >= 100000; // 1000 RUB in kopecks
    }
}

/// <summary>
/// Business rule validator for payment confirmation
/// </summary>
public class PaymentConfirmBusinessRuleValidator : AbstractValidator<PaymentConfirmRequestDto>
{
    public PaymentConfirmBusinessRuleValidator()
    {
        // Payment state validation
        RuleFor(x => x.PaymentId)
            .Must(BeInAuthorizableState)
            .WithErrorCode("PAYMENT_NOT_AUTHORIZABLE")
            .WithMessage("Payment is not in a state that allows confirmation");

        // Amount validation for partial confirmations
        RuleFor(x => x.Amount)
            .Must(BeValidConfirmationAmount)
            .WithErrorCode("CONFIRMATION_AMOUNT_INVALID")
            .WithMessage("Confirmation amount is not valid for this payment")
            .When(x => x.Amount.HasValue);

        // Time-based validation
        RuleFor(x => x)
            .Must(BeWithinConfirmationTimeLimit)
            .WithErrorCode("CONFIRMATION_TIME_EXPIRED")
            .WithMessage("Payment confirmation time has expired");
    }

    private bool BeInAuthorizableState(string paymentId)
    {
        // Business rule: Payment must be in AUTHORIZED state
        // This would typically query the database
        return !string.IsNullOrEmpty(paymentId);
    }

    private bool BeValidConfirmationAmount(decimal? amount)
    {
        if (!amount.HasValue)
            return true;

        // Business rule: Confirmation amount must not exceed authorized amount
        // This would typically check against the payment record
        return amount.Value > 0;
    }

    private bool BeWithinConfirmationTimeLimit(PaymentConfirmRequestDto request)
    {
        // Business rule: Confirmation must happen within allowed time window
        // This would typically check payment creation time vs current time
        return !string.IsNullOrEmpty(request.PaymentId);
    }
}

/// <summary>
/// Business rule validator for payment cancellation
/// </summary>
public class PaymentCancelBusinessRuleValidator : AbstractValidator<PaymentCancelRequestDto>
{
    public PaymentCancelBusinessRuleValidator()
    {
        // Payment state validation
        RuleFor(x => x.PaymentId)
            .Must(BeInCancellableState)
            .WithErrorCode("PAYMENT_NOT_CANCELLABLE")
            .WithMessage("Payment is not in a state that allows cancellation");

        // Amount validation for partial refunds
        RuleFor(x => x.Amount)
            .Must(BeValidRefundAmount)
            .WithErrorCode("REFUND_AMOUNT_INVALID")
            .WithMessage("Refund amount is not valid for this payment")
            .When(x => x.Amount.HasValue);

        // Time-based validation
        RuleFor(x => x)
            .Must(BeWithinRefundTimeLimit)
            .WithErrorCode("REFUND_TIME_EXPIRED")
            .WithMessage("Payment refund time has expired");

        // Business rule validation
        RuleFor(x => x)
            .Must(HaveValidRefundReason)
            .WithErrorCode("REFUND_REASON_REQUIRED")
            .WithMessage("Refund reason is required for this payment type");
    }

    private bool BeInCancellableState(string paymentId)
    {
        // Business rule: Payment must be in cancellable state
        // This would typically query the database for payment status
        return !string.IsNullOrEmpty(paymentId);
    }

    private bool BeValidRefundAmount(decimal? amount)
    {
        if (!amount.HasValue)
            return true;

        // Business rule: Refund amount must not exceed paid amount
        // This would typically check against the payment record
        return amount.Value > 0;
    }

    private bool BeWithinRefundTimeLimit(PaymentCancelRequestDto request)
    {
        // Business rule: Refunds must happen within allowed time window
        // This would typically check payment completion time vs current time
        return !string.IsNullOrEmpty(request.PaymentId);
    }

    private bool HaveValidRefundReason(PaymentCancelRequestDto request)
    {
        // Business rule: Some payment types require refund reasons
        // This would typically check payment type and team configuration
        return !string.IsNullOrEmpty(request.PaymentId);
    }
}