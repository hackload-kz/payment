// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using PaymentGateway.Core.Entities;
using PaymentGateway.Core.Enums;
using PaymentGateway.Core.Interfaces;
using PaymentGateway.Core.Repositories;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Prometheus;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Card payment processing service with validation, tokenization, and BIN detection
/// </summary>
public interface ICardPaymentProcessingService
{
    Task<CardValidationResult> ValidateCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default);
    Task<CardTokenizationResult> TokenizeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default);
    Task<CardProcessingResult> ProcessCardPaymentAsync(CardPaymentRequest request, CancellationToken cancellationToken = default);
    Task<BinDetectionResult> DetectCardBinAsync(string cardNumber, CancellationToken cancellationToken = default);
    Task<IEnumerable<CardTransaction>> GetCardTransactionsAsync(Guid teamId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default);
    Task<CardProcessingStatistics> GetCardProcessingStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default);
}

public class CardPaymentRequest
{
    public string CardNumber { get; set; } = string.Empty;
    public string ExpiryMonth { get; set; } = string.Empty;
    public string ExpiryYear { get; set; } = string.Empty;
    public string CVV { get; set; } = string.Empty;
    public string CardholderName { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = "KZT";
    public Guid PaymentId { get; set; }
    public Guid TeamId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public Dictionary<string, object> Metadata { get; set; } = new();
}

public class CardValidationResult
{
    public bool IsValid { get; set; }
    public List<string> ValidationErrors { get; set; } = new();
    public List<string> ValidationWarnings { get; set; } = new();
    public CardType DetectedCardType { get; set; }
    public string MaskedCardNumber { get; set; } = string.Empty;
    public bool IsLuhnValid { get; set; }
    public bool IsExpiryValid { get; set; }
    public bool IsCvvValid { get; set; }
    public Dictionary<string, object> ValidationMetadata { get; set; } = new();
}

public class CardTokenizationResult
{
    public bool IsSuccess { get; set; }
    public string CardToken { get; set; } = string.Empty;
    public string MaskedCardNumber { get; set; } = string.Empty;
    public CardType CardType { get; set; }
    public DateTime ExpiryDate { get; set; }
    public string TokenExpiryDate { get; set; } = string.Empty;
    public List<string> Errors { get; set; } = new();
    public Dictionary<string, object> TokenMetadata { get; set; } = new();
}

public class CardProcessingResult
{
    public bool IsSuccess { get; set; }
    public string TransactionId { get; set; } = string.Empty;
    public PaymentStatus PaymentStatus { get; set; }
    public string AuthorizationCode { get; set; } = string.Empty;
    public string ProcessorResponse { get; set; } = string.Empty;
    public decimal ProcessedAmount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public List<string> ProcessingErrors { get; set; } = new();
    public TimeSpan ProcessingDuration { get; set; }
    public Dictionary<string, object> ProcessingMetadata { get; set; } = new();
    
    // Additional properties needed by controllers
    public string? ErrorMessage => ProcessingErrors.FirstOrDefault();
    public string? ErrorCode { get; set; }
    public string? MaskedCardNumber { get; set; }
}

public class BinDetectionResult
{
    public string BIN { get; set; } = string.Empty;
    public CardType CardType { get; set; }
    public string CardBrand { get; set; } = string.Empty;
    public string IssuingBank { get; set; } = string.Empty;
    public string IssuingCountry { get; set; } = string.Empty;
    public CardLevel CardLevel { get; set; }
    public bool IsDebit { get; set; }
    public bool IsCommercial { get; set; }
    public Dictionary<string, object> BinMetadata { get; set; } = new();
}

public enum CardType
{
    Unknown = 0,
    Visa = 1,
    MasterCard = 2,
    AmericanExpress = 3,
    Discover = 4,
    JCB = 5,
    DinersClub = 6,
    UnionPay = 7,
    Maestro = 8,
    Mir = 9
}

public enum CardLevel
{
    Unknown = 0,
    Classic = 1,
    Gold = 2,
    Platinum = 3,
    Signature = 4,
    Infinite = 5,
    Corporate = 6
}

public class CardTransaction : BaseEntity
{
    public Guid PaymentId { get; set; }
    public Guid TeamId { get; set; }
    public string OrderId { get; set; } = string.Empty;
    public string MaskedCardNumber { get; set; } = string.Empty;
    public CardType CardType { get; set; }
    public string CardToken { get; set; } = string.Empty;
    public decimal Amount { get; set; }
    public string Currency { get; set; } = string.Empty;
    public PaymentStatus Status { get; set; }
    public string AuthorizationCode { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
    public string ProcessorResponse { get; set; } = string.Empty;
    public DateTime ProcessedAt { get; set; }
    public TimeSpan ProcessingDuration { get; set; }
    public Dictionary<string, object> TransactionMetadata { get; set; } = new();
    
    // Navigation
    public Payment Payment { get; set; } = null!;
}

public class CardProcessingStatistics
{
    public TimeSpan Period { get; set; }
    public int TotalTransactions { get; set; }
    public int SuccessfulTransactions { get; set; }
    public int FailedTransactions { get; set; }
    public double SuccessRate { get; set; }
    public TimeSpan AverageProcessingTime { get; set; }
    public decimal TotalProcessedAmount { get; set; }
    public Dictionary<CardType, int> TransactionsByCardType { get; set; } = new();
    public Dictionary<string, int> TransactionsByTeam { get; set; } = new();
    public Dictionary<string, int> ErrorsByType { get; set; } = new();
    public Dictionary<string, int> TransactionsByCountry { get; set; } = new();
}

public class CardPaymentProcessingService : ICardPaymentProcessingService
{
    private readonly IPaymentRepository _paymentRepository;
    private readonly ILogger<CardPaymentProcessingService> _logger;
    
    // Card tokenization key (in production, this should come from secure key management)
    private readonly byte[] _tokenizationKey = Convert.FromBase64String("QWJjZGVmZ2hpams9PQ=="); // "Abcdefghijk==" as placeholder
    
    // Metrics
    private static readonly Counter CardValidationOperations = Metrics
        .CreateCounter("card_validation_operations_total", "Total card validation operations", new[] { "team_id", "result", "card_type" });
    
    private static readonly Counter CardProcessingOperations = Metrics
        .CreateCounter("card_processing_operations_total", "Total card processing operations", new[] { "team_id", "result", "card_type" });
    
    private static readonly Histogram CardProcessingDuration = Metrics
        .CreateHistogram("card_processing_duration_seconds", "Card processing operation duration");
    
    private static readonly Counter CardTokenizations = Metrics
        .CreateCounter("card_tokenization_operations_total", "Total card tokenization operations", new[] { "team_id", "result" });
    
    private static readonly Gauge ActiveCardTransactions = Metrics
        .CreateGauge("active_card_transactions_total", "Total active card transactions", new[] { "team_id", "card_type" });

    // Card BIN detection patterns
    private static readonly Dictionary<CardType, Regex> CardTypePatterns = new()
    {
        [CardType.Visa] = new Regex(@"^4\d{12}(?:\d{3})?$", RegexOptions.Compiled),
        [CardType.MasterCard] = new Regex(@"^5[1-5]\d{14}$|^2(?:2(?:2[1-9]|[3-9]\d)|[3-6]\d{2}|7(?:[01]\d|20))\d{12}$", RegexOptions.Compiled),
        [CardType.AmericanExpress] = new Regex(@"^3[47]\d{13}$", RegexOptions.Compiled),
        [CardType.Discover] = new Regex(@"^6(?:011|5\d{2})\d{12}$", RegexOptions.Compiled),
        [CardType.JCB] = new Regex(@"^35(?:2[89]|[3-8]\d)\d{12}$", RegexOptions.Compiled),
        [CardType.DinersClub] = new Regex(@"^3(?:0[0-5]|[68]\d)\d{11}$", RegexOptions.Compiled),
        [CardType.UnionPay] = new Regex(@"^62\d{14,17}$", RegexOptions.Compiled),
        [CardType.Maestro] = new Regex(@"^(?:5[0678]\d{2}|6304|6390|67\d{2})\d{8,15}$", RegexOptions.Compiled),
        [CardType.Mir] = new Regex(@"^220[0-4]\d{12}$", RegexOptions.Compiled)
    };

    // Simulated BIN database for demonstration
    private static readonly Dictionary<string, BinDetectionResult> BinDatabase = new()
    {
        ["411111"] = new BinDetectionResult { BIN = "411111", CardType = CardType.Visa, CardBrand = "Visa", IssuingBank = "Test Bank", IssuingCountry = "US", CardLevel = CardLevel.Classic, IsDebit = false },
        ["555555"] = new BinDetectionResult { BIN = "555555", CardType = CardType.MasterCard, CardBrand = "MasterCard", IssuingBank = "Test Bank", IssuingCountry = "US", CardLevel = CardLevel.Gold, IsDebit = false },
        ["378282"] = new BinDetectionResult { BIN = "378282", CardType = CardType.AmericanExpress, CardBrand = "American Express", IssuingBank = "Amex", IssuingCountry = "US", CardLevel = CardLevel.Platinum, IsDebit = false },
        ["220000"] = new BinDetectionResult { BIN = "220000", CardType = CardType.Mir, CardBrand = "Mir", IssuingBank = "Sberbank", IssuingCountry = "RU", CardLevel = CardLevel.Classic, IsDebit = true }
    };

    public CardPaymentProcessingService(
        IPaymentRepository paymentRepository,
        ILogger<CardPaymentProcessingService> logger)
    {
        _paymentRepository = paymentRepository;
        _logger = logger;
    }

    public async Task<CardValidationResult> ValidateCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = CardProcessingDuration.NewTimer();
        
        try
        {
            var result = new CardValidationResult();
            
            // Basic input validation
            if (string.IsNullOrWhiteSpace(request.CardNumber))
            {
                result.ValidationErrors.Add("Card number is required");
            }
            
            if (string.IsNullOrWhiteSpace(request.ExpiryMonth))
            {
                result.ValidationErrors.Add("Expiry month is required");
            }
            
            if (string.IsNullOrWhiteSpace(request.ExpiryYear))
            {
                result.ValidationErrors.Add("Expiry year is required");
            }
            
            if (string.IsNullOrWhiteSpace(request.CVV))
            {
                result.ValidationErrors.Add("CVV is required");
            }

            if (result.ValidationErrors.Any())
            {
                CardValidationOperations.WithLabels(request.TeamId.ToString(), "invalid_input", "unknown").Inc();
                return result;
            }

            // Clean card number (remove spaces and dashes)
            var cleanCardNumber = CleanCardNumber(request.CardNumber);
            
            // Validate card number length
            if (cleanCardNumber.Length < 13 || cleanCardNumber.Length > 19)
            {
                result.ValidationErrors.Add("Card number must be between 13 and 19 digits");
            }
            
            // Validate card number contains only digits
            if (!cleanCardNumber.All(char.IsDigit))
            {
                result.ValidationErrors.Add("Card number must contain only digits");
            }

            if (result.ValidationErrors.Any())
            {
                CardValidationOperations.WithLabels(request.TeamId.ToString(), "invalid_format", "unknown").Inc();
                return result;
            }

            // Detect card type
            result.DetectedCardType = DetectCardType(cleanCardNumber);
            result.MaskedCardNumber = MaskCardNumber(cleanCardNumber);

            // Luhn algorithm validation
            result.IsLuhnValid = ValidateLuhnAlgorithm(cleanCardNumber);
            if (!result.IsLuhnValid)
            {
                result.ValidationErrors.Add("Card number fails Luhn algorithm validation");
                
                // Log helpful test card suggestions for development
                _logger.LogWarning("Luhn validation failed for card ending in {CardSuffix}. " +
                    "Valid test cards: 4532123456789012 (Visa), 5555555555554444 (MasterCard), 378282246310005 (Amex)", 
                    cleanCardNumber.Length >= 4 ? cleanCardNumber[^4..] : "****");
            }

            // Expiry date validation
            result.IsExpiryValid = ValidateExpiryDate(request.ExpiryMonth, request.ExpiryYear, out var expiryValidationError);
            if (!result.IsExpiryValid && !string.IsNullOrEmpty(expiryValidationError))
            {
                result.ValidationErrors.Add(expiryValidationError);
            }

            // CVV validation
            result.IsCvvValid = ValidateCVV(request.CVV, result.DetectedCardType);
            if (!result.IsCvvValid)
            {
                result.ValidationErrors.Add("Invalid CVV format for detected card type");
            }

            // Additional validations
            await ValidateAdditionalRules(request, result, cleanCardNumber, cancellationToken);

            result.IsValid = !result.ValidationErrors.Any();
            
            // Add metadata
            result.ValidationMetadata["card_length"] = cleanCardNumber.Length;
            result.ValidationMetadata["validation_timestamp"] = DateTime.UtcNow;
            result.ValidationMetadata["detected_card_type"] = result.DetectedCardType.ToString();

            CardValidationOperations.WithLabels(request.TeamId.ToString(), result.IsValid ? "valid" : "invalid", result.DetectedCardType.ToString()).Inc();
            
            _logger.LogInformation("Card validation completed: TeamId: {TeamId}, Valid: {IsValid}, CardType: {CardType}, Errors: {ErrorCount}", 
                request.TeamId, result.IsValid, result.DetectedCardType, result.ValidationErrors.Count);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Card validation failed: TeamId: {TeamId}", request.TeamId);
            CardValidationOperations.WithLabels(request.TeamId.ToString(), "error", "unknown").Inc();
            
            return new CardValidationResult
            {
                IsValid = false,
                ValidationErrors = new List<string> { "Internal validation error" }
            };
        }
    }

    public async Task<CardTokenizationResult> TokenizeCardAsync(CardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        try
        {
            // First validate the card
            var validationResult = await ValidateCardAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                CardTokenizations.WithLabels(request.TeamId.ToString(), "validation_failed").Inc();
                return new CardTokenizationResult
                {
                    IsSuccess = false,
                    Errors = validationResult.ValidationErrors,
                    MaskedCardNumber = validationResult.MaskedCardNumber,
                    CardType = validationResult.DetectedCardType
                };
            }

            var cleanCardNumber = CleanCardNumber(request.CardNumber);
            
            // Generate secure token
            var cardToken = GenerateCardToken(cleanCardNumber, request.ExpiryMonth, request.ExpiryYear);
            var tokenExpiryDate = DateTime.UtcNow.AddYears(2).ToString("yyyy-MM-dd");
            
            // Parse expiry date
            if (!int.TryParse(request.ExpiryMonth, out var month) || !int.TryParse(request.ExpiryYear, out var year))
            {
                CardTokenizations.WithLabels(request.TeamId.ToString(), "expiry_parse_failed").Inc();
                return new CardTokenizationResult
                {
                    IsSuccess = false,
                    Errors = new List<string> { "Invalid expiry date format" }
                };
            }

            var expiryDate = new DateTime(year < 100 ? 2000 + year : year, month, 1).AddMonths(1).AddDays(-1);

            var result = new CardTokenizationResult
            {
                IsSuccess = true,
                CardToken = cardToken,
                MaskedCardNumber = validationResult.MaskedCardNumber,
                CardType = validationResult.DetectedCardType,
                ExpiryDate = expiryDate,
                TokenExpiryDate = tokenExpiryDate,
                TokenMetadata = new Dictionary<string, object>
                {
                    ["tokenization_timestamp"] = DateTime.UtcNow,
                    ["token_version"] = "1.0",
                    ["tokenization_method"] = "AES-256-GCM"
                }
            };

            CardTokenizations.WithLabels(request.TeamId.ToString(), "success").Inc();
            
            _logger.LogInformation("Card tokenization successful: TeamId: {TeamId}, CardType: {CardType}, Token: {TokenPrefix}***", 
                request.TeamId, validationResult.DetectedCardType, cardToken[..8]);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Card tokenization failed: TeamId: {TeamId}", request.TeamId);
            CardTokenizations.WithLabels(request.TeamId.ToString(), "error").Inc();
            
            return new CardTokenizationResult
            {
                IsSuccess = false,
                Errors = new List<string> { "Internal tokenization error" }
            };
        }
    }

    public async Task<CardProcessingResult> ProcessCardPaymentAsync(CardPaymentRequest request, CancellationToken cancellationToken = default)
    {
        using var activity = CardProcessingDuration.NewTimer();
        var startTime = DateTime.UtcNow;
        
        try
        {
            // Validate card first
            var validationResult = await ValidateCardAsync(request, cancellationToken);
            if (!validationResult.IsValid)
            {
                CardProcessingOperations.WithLabels(request.TeamId.ToString(), "validation_failed", validationResult.DetectedCardType.ToString()).Inc();
                return new CardProcessingResult
                {
                    IsSuccess = false,
                    ProcessingErrors = validationResult.ValidationErrors,
                    PaymentStatus = PaymentStatus.NEW, // Keep original status
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
            }

            // Tokenize card for security
            var tokenizationResult = await TokenizeCardAsync(request, cancellationToken);
            if (!tokenizationResult.IsSuccess)
            {
                CardProcessingOperations.WithLabels(request.TeamId.ToString(), "tokenization_failed", validationResult.DetectedCardType.ToString()).Inc();
                return new CardProcessingResult
                {
                    IsSuccess = false,
                    ProcessingErrors = tokenizationResult.Errors,
                    PaymentStatus = PaymentStatus.NEW,
                    ProcessingDuration = DateTime.UtcNow - startTime
                };
            }

            // Simulate card processing (in real implementation, this would call payment processor)
            var processingResult = await SimulateCardProcessing(request, validationResult, tokenizationResult, cancellationToken);
            
            // Record transaction
            await RecordCardTransaction(request, validationResult, tokenizationResult, processingResult, cancellationToken);
            
            processingResult.ProcessingDuration = DateTime.UtcNow - startTime;
            
            CardProcessingOperations.WithLabels(request.TeamId.ToString(), 
                processingResult.IsSuccess ? "success" : "failed", 
                validationResult.DetectedCardType.ToString()).Inc();
            
            _logger.LogInformation("Card processing completed: TeamId: {TeamId}, PaymentId: {PaymentId}, Success: {IsSuccess}, Duration: {Duration}ms", 
                request.TeamId, request.PaymentId, processingResult.IsSuccess, processingResult.ProcessingDuration.TotalMilliseconds);
            
            return processingResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Card processing failed: TeamId: {TeamId}, PaymentId: {PaymentId}", request.TeamId, request.PaymentId);
            CardProcessingOperations.WithLabels(request.TeamId.ToString(), "error", "unknown").Inc();
            
            return new CardProcessingResult
            {
                IsSuccess = false,
                ProcessingErrors = new List<string> { "Internal processing error" },
                PaymentStatus = PaymentStatus.NEW,
                ProcessingDuration = DateTime.UtcNow - startTime
            };
        }
    }

    public async Task<BinDetectionResult> DetectCardBinAsync(string cardNumber, CancellationToken cancellationToken = default)
    {
        try
        {
            var cleanCardNumber = CleanCardNumber(cardNumber);
            if (cleanCardNumber.Length < 6)
            {
                return new BinDetectionResult { CardType = CardType.Unknown };
            }

            var bin = cleanCardNumber[..6];
            
            // Check our BIN database
            if (BinDatabase.TryGetValue(bin, out var binResult))
            {
                return binResult;
            }

            // Fallback to card type detection
            var cardType = DetectCardType(cleanCardNumber);
            return new BinDetectionResult
            {
                BIN = bin,
                CardType = cardType,
                CardBrand = cardType.ToString(),
                IssuingBank = "Unknown",
                IssuingCountry = "Unknown",
                CardLevel = CardLevel.Unknown,
                BinMetadata = new Dictionary<string, object>
                {
                    ["detection_method"] = "pattern_matching",
                    ["detection_timestamp"] = DateTime.UtcNow
                }
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "BIN detection failed for card number");
            return new BinDetectionResult { CardType = CardType.Unknown };
        }
    }

    public async Task<IEnumerable<CardTransaction>> GetCardTransactionsAsync(Guid teamId, DateTime? fromDate = null, DateTime? toDate = null, CancellationToken cancellationToken = default)
    {
        try
        {
            // This would typically query a card transactions table
            // For now, return empty collection as this requires additional repository setup
            return new List<CardTransaction>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get card transactions for team: {TeamId}", teamId);
            return new List<CardTransaction>();
        }
    }

    public async Task<CardProcessingStatistics> GetCardProcessingStatisticsAsync(Guid? teamId = null, TimeSpan? period = null, CancellationToken cancellationToken = default)
    {
        try
        {
            period ??= TimeSpan.FromDays(7);
            
            // This would typically query card transaction logs or dedicated statistics table
            // For now, return simulated statistics
            var stats = new CardProcessingStatistics
            {
                Period = period.Value,
                TotalTransactions = 2500,
                SuccessfulTransactions = 2350,
                FailedTransactions = 150,
                SuccessRate = 0.94,
                AverageProcessingTime = TimeSpan.FromSeconds(1.8),
                TotalProcessedAmount = 25750000, // 257,500 RUB
                TransactionsByCardType = new Dictionary<CardType, int>
                {
                    [CardType.Visa] = 1200,
                    [CardType.MasterCard] = 950,
                    [CardType.Mir] = 300,
                    [CardType.AmericanExpress] = 50
                },
                TransactionsByTeam = new Dictionary<string, int>
                {
                    ["1"] = 800,
                    ["2"] = 650,
                    ["3"] = 550,
                    ["4"] = 500
                },
                ErrorsByType = new Dictionary<string, int>
                {
                    ["invalid_card_number"] = 60,
                    ["expired_card"] = 40,
                    ["insufficient_funds"] = 30,
                    ["invalid_cvv"] = 20
                },
                TransactionsByCountry = new Dictionary<string, int>
                {
                    ["RU"] = 2000,
                    ["US"] = 300,
                    ["DE"] = 100,
                    ["GB"] = 100
                }
            };

            return stats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get card processing statistics");
            return new CardProcessingStatistics();
        }
    }

    #region Private Helper Methods

    private static string CleanCardNumber(string cardNumber)
    {
        return Regex.Replace(cardNumber, @"[\s\-]", "");
    }

    private static string MaskCardNumber(string cardNumber)
    {
        if (cardNumber.Length < 4) return cardNumber;
        
        return cardNumber.Length switch
        {
            <= 8 => $"{cardNumber[..4]}****",
            <= 12 => $"{cardNumber[..4]}****{cardNumber[^4..]}",
            _ => $"{cardNumber[..6]}******{cardNumber[^4..]}"
        };
    }

    private static CardType DetectCardType(string cardNumber)
    {
        foreach (var (cardType, pattern) in CardTypePatterns)
        {
            if (pattern.IsMatch(cardNumber))
            {
                return cardType;
            }
        }
        return CardType.Unknown;
    }

    private static bool ValidateLuhnAlgorithm(string cardNumber)
    {
        if (string.IsNullOrEmpty(cardNumber) || !cardNumber.All(char.IsDigit))
            return false;

        var sum = 0;
        var alternate = false;

        for (var i = cardNumber.Length - 1; i >= 0; i--)
        {
            var digit = int.Parse(cardNumber[i].ToString());

            if (alternate)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }

            sum += digit;
            alternate = !alternate;
        }

        return sum % 10 == 0;
    }

    private static bool ValidateExpiryDate(string month, string year, out string? error)
    {
        error = null;

        if (!int.TryParse(month, out var expMonth) || expMonth < 1 || expMonth > 12)
        {
            error = "Invalid expiry month (must be 01-12)";
            return false;
        }

        if (!int.TryParse(year, out var expYear))
        {
            error = "Invalid expiry year format";
            return false;
        }

        // Handle 2-digit and 4-digit years
        if (expYear < 100)
            expYear += 2000;

        var expiryDate = new DateTime(expYear, expMonth, 1).AddMonths(1).AddDays(-1);
        
        if (expiryDate < DateTime.UtcNow.Date)
        {
            error = "Card has expired";
            return false;
        }

        return true;
    }

    private static bool ValidateCVV(string cvv, CardType cardType)
    {
        if (string.IsNullOrEmpty(cvv) || !cvv.All(char.IsDigit))
            return false;

        return cardType switch
        {
            CardType.AmericanExpress => cvv.Length == 4,
            _ => cvv.Length == 3
        };
    }

    private async Task ValidateAdditionalRules(CardPaymentRequest request, CardValidationResult result, string cleanCardNumber, CancellationToken cancellationToken)
    {
        // Additional business rule validations can be added here
        
        // Example: Check if card is not blocked
        // Example: Validate against fraud detection rules
        // Example: Check daily/monthly limits
        
        if (request.Amount <= 0)
        {
            result.ValidationErrors.Add("Amount must be greater than zero");
        }

        if (request.Amount > 1000000) // 10,000 RUB limit for demo
        {
            result.ValidationWarnings.Add("High amount transaction - may require additional verification");
        }

        // Validate cardholder name if provided
        if (!string.IsNullOrWhiteSpace(request.CardholderName))
        {
            if (request.CardholderName.Length < 2 || request.CardholderName.Length > 50)
            {
                result.ValidationWarnings.Add("Cardholder name should be between 2 and 50 characters");
            }
        }
    }

    private string GenerateCardToken(string cardNumber, string expiryMonth, string expiryYear)
    {
        var tokenData = $"{cardNumber}:{expiryMonth}:{expiryYear}:{DateTime.UtcNow:yyyyMMddHHmmss}:{Guid.NewGuid()}";
        
        using var aes = Aes.Create();
        aes.Key = _tokenizationKey.Length == 32 ? _tokenizationKey : SHA256.HashData(Encoding.UTF8.GetBytes("DefaultTokenizationKey"))[..32];
        aes.GenerateIV();
        
        var encryptor = aes.CreateEncryptor();
        var tokenBytes = Encoding.UTF8.GetBytes(tokenData);
        var encryptedToken = encryptor.TransformFinalBlock(tokenBytes, 0, tokenBytes.Length);
        
        // Combine IV and encrypted data
        var result = new byte[aes.IV.Length + encryptedToken.Length];
        Array.Copy(aes.IV, 0, result, 0, aes.IV.Length);
        Array.Copy(encryptedToken, 0, result, aes.IV.Length, encryptedToken.Length);
        
        return "tok_" + Convert.ToBase64String(result).Replace("+", "-").Replace("/", "_").Replace("=", "");
    }

    private async Task<CardProcessingResult> SimulateCardProcessing(CardPaymentRequest request, CardValidationResult validation, CardTokenizationResult tokenization, CancellationToken cancellationToken)
    {
        // Simulate processing delay
        await Task.Delay(Random.Shared.Next(500, 2000), cancellationToken);
        
        // Simulate different processing outcomes based on card details
        var cleanCardNumber = CleanCardNumber(request.CardNumber);
        
        // Test card numbers for different scenarios
        var result = cleanCardNumber switch
        {
            "4111111111111111" => new CardProcessingResult
            {
                IsSuccess = true,
                TransactionId = $"txn_{Guid.NewGuid():N}",
                PaymentStatus = PaymentStatus.AUTHORIZED,
                AuthorizationCode = $"AUTH{Random.Shared.Next(100000, 999999)}",
                ProcessorResponse = "APPROVED",
                ProcessedAmount = request.Amount,
                Currency = request.Currency
            },
            "4000000000000002" => new CardProcessingResult
            {
                IsSuccess = false,
                ProcessingErrors = new List<string> { "Card declined by issuer" },
                PaymentStatus = PaymentStatus.NEW,
                ProcessorResponse = "DECLINED",
                ProcessedAmount = 0,
                Currency = request.Currency
            },
            "4000000000000119" => new CardProcessingResult
            {
                IsSuccess = false,
                ProcessingErrors = new List<string> { "Insufficient funds" },
                PaymentStatus = PaymentStatus.NEW,
                ProcessorResponse = "INSUFFICIENT_FUNDS",
                ProcessedAmount = 0,
                Currency = request.Currency
            },
            _ => new CardProcessingResult
            {
                IsSuccess = Random.Shared.NextDouble() > 0.1, // 90% success rate for other cards
                TransactionId = $"txn_{Guid.NewGuid():N}",
                PaymentStatus = PaymentStatus.AUTHORIZED,
                AuthorizationCode = $"AUTH{Random.Shared.Next(100000, 999999)}",
                ProcessorResponse = "APPROVED",
                ProcessedAmount = request.Amount,
                Currency = request.Currency
            }
        };

        if (!result.IsSuccess && result.ProcessingErrors.Count == 0)
        {
            result.ProcessingErrors.Add("Transaction declined by processor");
            result.ProcessorResponse = "DECLINED";
        }

        result.ProcessingMetadata["processor"] = "SimulatedProcessor";
        result.ProcessingMetadata["card_type"] = validation.DetectedCardType.ToString();
        result.ProcessingMetadata["masked_card"] = validation.MaskedCardNumber;
        result.ProcessingMetadata["processing_timestamp"] = DateTime.UtcNow;

        return result;
    }

    private async Task RecordCardTransaction(CardPaymentRequest request, CardValidationResult validation, CardTokenizationResult tokenization, CardProcessingResult processing, CancellationToken cancellationToken)
    {
        try
        {
            var transaction = new CardTransaction
            {
                PaymentId = request.PaymentId,
                TeamId = request.TeamId,
                OrderId = request.OrderId,
                MaskedCardNumber = validation.MaskedCardNumber,
                CardType = validation.DetectedCardType,
                CardToken = tokenization.CardToken,
                Amount = request.Amount,
                Currency = request.Currency,
                Status = processing.PaymentStatus,
                AuthorizationCode = processing.AuthorizationCode,
                TransactionId = processing.TransactionId,
                ProcessorResponse = processing.ProcessorResponse,
                ProcessedAt = DateTime.UtcNow,
                ProcessingDuration = processing.ProcessingDuration,
                TransactionMetadata = new Dictionary<string, object>
                {
                    ["validation_result"] = validation.IsValid,
                    ["tokenization_result"] = tokenization.IsSuccess,
                    ["processing_result"] = processing.IsSuccess,
                    ["card_type"] = validation.DetectedCardType.ToString(),
                    ["team_id"] = request.TeamId
                }
            };

            // This would typically save to a card transactions repository
            // For now, just log the transaction
            _logger.LogInformation("Card transaction recorded: TransactionId: {TransactionId}, PaymentId: {PaymentId}, Success: {IsSuccess}", 
                transaction.TransactionId, transaction.PaymentId, processing.IsSuccess);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to record card transaction: PaymentId: {PaymentId}", request.PaymentId);
            // Don't throw - this is just logging
        }
    }

    #endregion
}