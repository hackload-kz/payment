/**
 * Payment Form Validation Library
 * Provides comprehensive validation for payment forms with real-time feedback
 * Supports Luhn algorithm validation, card type detection, and security checks
 */

class PaymentValidator {
    constructor() {
        this.cardTypes = {
            visa: {
                pattern: /^4/,
                lengths: [13, 16, 19],
                cvvLength: 3,
                name: 'Visa',
                icon: '/images/cards/visa.svg'
            },
            mastercard: {
                pattern: /^5[1-5]|^2[2-7]/,
                lengths: [16],
                cvvLength: 3,
                name: 'MasterCard',
                icon: '/images/cards/mastercard.svg'
            },
            amex: {
                pattern: /^3[47]/,
                lengths: [15],
                cvvLength: 4,
                name: 'American Express',
                icon: '/images/cards/amex.svg'
            },
            discover: {
                pattern: /^6(?:011|5)/,
                lengths: [16],
                cvvLength: 3,
                name: 'Discover',
                icon: '/images/cards/discover.svg'
            },
            jcb: {
                pattern: /^(?:2131|1800|35)/,
                lengths: [16],
                cvvLength: 3,
                name: 'JCB',
                icon: '/images/cards/jcb.svg'
            },
            dinersclub: {
                pattern: /^3[0689]/,
                lengths: [14],
                cvvLength: 3,
                name: 'Diners Club',
                icon: '/images/cards/dinersclub.svg'
            },
            unionpay: {
                pattern: /^(62|88)/,
                lengths: [16, 17, 18, 19],
                cvvLength: 3,
                name: 'UnionPay',
                icon: '/images/cards/unionpay.svg'
            },
            mir: {
                pattern: /^220[0-4]/,
                lengths: [16],
                cvvLength: 3,
                name: 'Mir',
                icon: '/images/cards/mir.svg'
            }
        };

        this.validators = {
            cardNumber: this.validateCardNumber.bind(this),
            expiryDate: this.validateExpiryDate.bind(this),
            cvv: this.validateCVV.bind(this),
            cardholderName: this.validateCardholderName.bind(this),
            email: this.validateEmail.bind(this),
            phone: this.validatePhone.bind(this)
        };

        this.errorMessages = {
            en: {
                required: 'This field is required',
                invalid_card_number: 'Please enter a valid card number',
                invalid_expiry: 'Please enter a valid expiry date (MM/YY)',
                expired_card: 'This card has expired',
                invalid_cvv: 'Please enter a valid CVV',
                invalid_name: 'Please enter a valid cardholder name',
                invalid_email: 'Please enter a valid email address',
                invalid_phone: 'Please enter a valid phone number',
                unsupported_card: 'This card type is not supported',
                luhn_check_failed: 'Invalid card number (checksum failed)'
            },
            ru: {
                required: 'Это поле обязательно для заполнения',
                invalid_card_number: 'Пожалуйста, введите действительный номер карты',
                invalid_expiry: 'Пожалуйста, введите действительную дату истечения (ММ/ГГ)',
                expired_card: 'Срок действия этой карты истек',
                invalid_cvv: 'Пожалуйста, введите действительный CVV',
                invalid_name: 'Пожалуйста, введите действительное имя держателя карты',
                invalid_email: 'Пожалуйста, введите действительный адрес электронной почты',
                invalid_phone: 'Пожалуйста, введите действительный номер телефона',
                unsupported_card: 'Этот тип карты не поддерживается',
                luhn_check_failed: 'Неверный номер карты (ошибка контрольной суммы)'
            }
        };

        this.currentLanguage = document.documentElement.getAttribute('data-lang') || 'en';
    }

    /**
     * Format card number with spaces for better readability
     * @param {string} value - Raw card number
     * @returns {string} Formatted card number
     */
    formatCardNumber(value) {
        // Remove all non-digit characters
        const digits = value.replace(/\D/g, '');
        
        // Detect card type for proper formatting
        const cardType = this.detectCardType(digits);
        
        // Format based on card type
        if (cardType === 'amex') {
            // American Express: 4-6-5 format
            return digits.replace(/(\d{4})(\d{6})(\d{5})/, '$1 $2 $3');
        } else if (cardType === 'dinersclub') {
            // Diners Club: 4-6-4 format
            return digits.replace(/(\d{4})(\d{6})(\d{4})/, '$1 $2 $3');
        } else {
            // Most cards: 4-4-4-4 format
            return digits.replace(/(\d{4})(?=\d)/g, '$1 ');
        }
    }

    /**
     * Format expiry date as MM/YY
     * @param {string} value - Raw expiry date
     * @returns {string} Formatted expiry date
     */
    formatExpiryDate(value) {
        const digits = value.replace(/\D/g, '');
        if (digits.length >= 2) {
            return digits.substring(0, 2) + (digits.length > 2 ? '/' + digits.substring(2, 4) : '');
        }
        return digits;
    }

    /**
     * Detect card type based on card number
     * @param {string} cardNumber - Card number (digits only)
     * @returns {string|null} Card type or null if unknown
     */
    detectCardType(cardNumber) {
        for (const [type, config] of Object.entries(this.cardTypes)) {
            if (config.pattern.test(cardNumber)) {
                return type;
            }
        }
        return null;
    }

    /**
     * Validate card number using Luhn algorithm (simplified)
     * @param {string} value - Card number
     * @returns {ValidationResult} Validation result
     */
    validateCardNumber(value) {
        const result = { isValid: false, errors: [], cardType: null };
        
        if (!value || value.trim() === '') {
            result.errors.push(this.getMessage('required'));
            return result;
        }

        // Remove spaces and validate format
        const digits = value.replace(/\s/g, '');
        
        if (!/^\d+$/.test(digits)) {
            result.errors.push(this.getMessage('invalid_card_number'));
            return result;
        }

        // Basic length check (13-19 digits)
        if (digits.length < 13 || digits.length > 19) {
            result.errors.push(this.getMessage('invalid_card_number'));
            return result;
        }

        // Detect card type (optional, don't fail if unknown)
        const cardType = this.detectCardType(digits);
        result.cardType = cardType;

        // Simplified Luhn check for real validation
        if (digits.length >= 15 && !this.luhnCheck(digits)) {
            result.errors.push(this.getMessage('invalid_card_number'));
            return result;
        }

        result.isValid = true;
        return result;
    }

    /**
     * Validate expiry date
     * @param {string} value - Expiry date in MM/YY format
     * @returns {ValidationResult} Validation result
     */
    validateExpiryDate(value) {
        const result = { isValid: false, errors: [] };
        
        if (!value || value.trim() === '') {
            result.errors.push(this.getMessage('required'));
            return result;
        }

        // Parse MM/YY format
        const match = value.match(/^(\d{1,2})\/(\d{2})$/);
        if (!match) {
            result.errors.push(this.getMessage('invalid_expiry'));
            return result;
        }

        const month = parseInt(match[1], 10);
        const year = parseInt(match[2], 10) + 2000; // Convert YY to YYYY

        // Validate month
        if (month < 1 || month > 12) {
            result.errors.push(this.getMessage('invalid_expiry'));
            return result;
        }

        // Check if card is expired
        const now = new Date();
        const currentYear = now.getFullYear();
        const currentMonth = now.getMonth() + 1;

        if (year < currentYear || (year === currentYear && month < currentMonth)) {
            result.errors.push(this.getMessage('expired_card'));
            return result;
        }

        result.isValid = true;
        return result;
    }

    /**
     * Validate CVV
     * @param {string} value - CVV value
     * @param {string} cardType - Card type for CVV length validation
     * @returns {ValidationResult} Validation result
     */
    validateCVV(value, cardType = null) {
        const result = { isValid: false, errors: [] };
        
        if (!value || value.trim() === '') {
            result.errors.push(this.getMessage('required'));
            return result;
        }

        if (!/^\d+$/.test(value)) {
            result.errors.push(this.getMessage('invalid_cvv'));
            return result;
        }

        // Validate length based on card type
        let expectedLength = 3; // Default CVV length
        if (cardType && this.cardTypes[cardType]) {
            expectedLength = this.cardTypes[cardType].cvvLength;
        }

        if (value.length !== expectedLength) {
            result.errors.push(this.getMessage('invalid_cvv'));
            return result;
        }

        result.isValid = true;
        return result;
    }

    /**
     * Validate cardholder name (simplified)
     * @param {string} value - Cardholder name
     * @returns {ValidationResult} Validation result
     */
    validateCardholderName(value) {
        const result = { isValid: false, errors: [] };
        
        if (!value || value.trim() === '') {
            result.errors.push(this.getMessage('required'));
            return result;
        }

        // Basic validation - just check length and allow most characters
        const trimmed = value.trim();
        if (trimmed.length < 2 || trimmed.length > 100) {
            result.errors.push(this.getMessage('invalid_name'));
            return result;
        }

        result.isValid = true;
        return result;
    }

    /**
     * Validate email address
     * @param {string} value - Email address
     * @returns {ValidationResult} Validation result
     */
    validateEmail(value) {
        const result = { isValid: false, errors: [] };
        
        if (!value || value.trim() === '') {
            result.errors.push(this.getMessage('required'));
            return result;
        }

        // RFC 5322 compliant email regex (simplified)
        const emailRegex = /^[a-zA-Z0-9.!#$%&'*+/=?^_`{|}~-]+@[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?(?:\.[a-zA-Z0-9](?:[a-zA-Z0-9-]{0,61}[a-zA-Z0-9])?)*$/;
        
        if (!emailRegex.test(value.trim())) {
            result.errors.push(this.getMessage('invalid_email'));
            return result;
        }

        result.isValid = true;
        return result;
    }

    /**
     * Validate phone number (optional field)
     * @param {string} value - Phone number
     * @returns {ValidationResult} Validation result
     */
    validatePhone(value) {
        const result = { isValid: true, errors: [] };
        
        // Phone is optional, so empty is valid
        if (!value || value.trim() === '') {
            return result;
        }

        // Should contain only digits, spaces, hyphens, parentheses, and plus
        if (!/^[\+]?[0-9\s\-\(\)]{10,20}$/.test(value.trim())) {
            result.errors.push(this.getMessage('invalid_phone'));
            result.isValid = false;
            return result;
        }

        // Should have at least 10 digits
        const digits = value.replace(/\D/g, '');
        if (digits.length < 10) {
            result.errors.push(this.getMessage('invalid_phone'));
            result.isValid = false;
            return result;
        }

        return result;
    }

    /**
     * Luhn algorithm implementation for card number validation
     * @param {string} cardNumber - Card number (digits only)
     * @returns {boolean} True if valid, false otherwise
     */
    luhnCheck(cardNumber) {
        let sum = 0;
        let alternate = false;
        
        // Process digits from right to left
        for (let i = cardNumber.length - 1; i >= 0; i--) {
            let digit = parseInt(cardNumber.charAt(i), 10);
            
            if (alternate) {
                digit *= 2;
                if (digit > 9) {
                    digit = Math.floor(digit / 10) + (digit % 10);
                }
            }
            
            sum += digit;
            alternate = !alternate;
        }
        
        return (sum % 10) === 0;
    }

    /**
     * Get localized error message
     * @param {string} key - Message key
     * @returns {string} Localized message
     */
    getMessage(key) {
        return this.errorMessages[this.currentLanguage]?.[key] || 
               this.errorMessages['en'][key] || 
               'Validation error';
    }

    /**
     * Set current language
     * @param {string} language - Language code (en/ru)
     */
    setLanguage(language) {
        this.currentLanguage = language;
    }

    /**
     * Validate all form fields
     * @param {Object} formData - Form data object
     * @returns {Object} Validation results for all fields
     */
    validateForm(formData) {
        const results = {};
        
        // Validate card number
        results.cardNumber = this.validateCardNumber(formData.cardNumber);
        
        // Validate expiry date
        results.expiryDate = this.validateExpiryDate(formData.expiryDate);
        
        // Validate CVV with card type context
        results.cvv = this.validateCVV(formData.cvv, results.cardNumber.cardType);
        
        // Validate cardholder name
        results.cardholderName = this.validateCardholderName(formData.cardholderName);
        
        // Validate email
        results.email = this.validateEmail(formData.email);
        
        // Validate phone (optional)
        results.phone = this.validatePhone(formData.phone);
        
        // Check if all required fields are valid
        results.isFormValid = results.cardNumber.isValid && 
                             results.expiryDate.isValid && 
                             results.cvv.isValid && 
                             results.cardholderName.isValid && 
                             results.email.isValid && 
                             results.phone.isValid;
        
        return results;
    }

    /**
     * Check if a field value is potentially sensitive
     * @param {string} fieldName - Field name
     * @param {string} value - Field value
     * @returns {boolean} True if field contains sensitive data
     */
    isSensitiveField(fieldName, value) {
        const sensitiveFields = ['cardNumber', 'cvv'];
        return sensitiveFields.includes(fieldName);
    }

    /**
     * Sanitize field value for logging (mask sensitive data)
     * @param {string} fieldName - Field name
     * @param {string} value - Field value
     * @returns {string} Sanitized value
     */
    sanitizeForLogging(fieldName, value) {
        if (fieldName === 'cardNumber' && value) {
            // Show only first 4 and last 4 digits
            const digits = value.replace(/\D/g, '');
            if (digits.length >= 8) {
                return digits.substring(0, 4) + '*'.repeat(digits.length - 8) + digits.substring(digits.length - 4);
            }
            return '*'.repeat(digits.length);
        }
        
        if (fieldName === 'cvv') {
            return '*'.repeat(value ? value.length : 0);
        }
        
        return value;
    }
}

// Export for use in other modules
window.PaymentValidator = PaymentValidator;