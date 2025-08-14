/**
 * Payment Form Main Controller
 * Handles form interactions, real-time validation, card formatting, and form submission
 */

class PaymentFormController {
    constructor() {
        this.validator = new PaymentValidator();
        this.form = document.getElementById('payment-form');
        this.submitButton = document.getElementById('submit_payment');
        this.currentCardType = null;
        this.validationStates = {};
        
        // Initialize after DOM is loaded
        if (document.readyState === 'loading') {
            document.addEventListener('DOMContentLoaded', () => this.initialize());
        } else {
            this.initialize();
        }
    }

    initialize() {
        this.bindFormEvents();
        this.bindInputEvents();
        this.bindUIEvents();
        this.initializeFormState();
        this.setupAccessibilityFeatures();
        
        console.log('Payment form controller initialized');
        console.log('Payment data:', window.paymentData);
        console.log('Test card numbers: 4111111111111111 (Visa), 5555555555554444 (MasterCard)');
    }

    bindFormEvents() {
        // Form submission
        this.form.addEventListener('submit', (e) => this.handleFormSubmit(e));
        
        // Prevent form submission on Enter key in input fields
        this.form.addEventListener('keydown', (e) => {
            if (e.key === 'Enter' && e.target.tagName === 'INPUT') {
                e.preventDefault();
                this.moveToNextField(e.target);
            }
        });
    }

    bindInputEvents() {
        // Card number input
        const cardNumberInput = document.getElementById('card_number');
        cardNumberInput.addEventListener('input', (e) => this.handleCardNumberInput(e));
        cardNumberInput.addEventListener('paste', (e) => this.handleCardNumberPaste(e));
        cardNumberInput.addEventListener('blur', (e) => this.validateField(e.target));

        // Expiry date input
        const expiryInput = document.getElementById('expiry_date');
        expiryInput.addEventListener('input', (e) => this.handleExpiryInput(e));
        expiryInput.addEventListener('blur', (e) => this.validateField(e.target));

        // CVV input
        const cvvInput = document.getElementById('cvv');
        cvvInput.addEventListener('input', (e) => this.handleCvvInput(e));
        cvvInput.addEventListener('blur', (e) => this.validateField(e.target));

        // Cardholder name input
        const nameInput = document.getElementById('cardholder_name');
        nameInput.addEventListener('input', (e) => this.handleNameInput(e));
        nameInput.addEventListener('blur', (e) => this.validateField(e.target));

        // Email input
        const emailInput = document.getElementById('email');
        emailInput.addEventListener('input', (e) => this.handleEmailInput(e));
        emailInput.addEventListener('blur', (e) => this.validateField(e.target));

        // Phone input
        const phoneInput = document.getElementById('phone');
        phoneInput.addEventListener('input', (e) => this.handlePhoneInput(e));
        phoneInput.addEventListener('blur', (e) => this.validateField(e.target));

        // Terms agreement checkbox
        const termsCheckbox = document.getElementById('terms_agreement');
        termsCheckbox.addEventListener('change', (e) => this.handleTermsChange(e));
    }

    bindUIEvents() {
        // CVV help button
        const cvvHelpButton = document.querySelector('.cvv-help-button');
        if (cvvHelpButton) {
            cvvHelpButton.addEventListener('click', () => this.showCvvHelp());
        }

        // Modal close events
        const modal = document.getElementById('cvv_help_modal');
        if (modal) {
            modal.addEventListener('click', (e) => {
                if (e.target.hasAttribute('data-modal-close')) {
                    this.hideModal(modal);
                }
            });
        }

        // Cancel button
        const cancelButton = document.getElementById('cancel_payment');
        if (cancelButton) {
            cancelButton.addEventListener('click', () => this.handleCancel());
        }

        // Language toggle
        const languageToggle = document.querySelector('.language-toggle');
        if (languageToggle) {
            languageToggle.addEventListener('click', () => this.toggleLanguageDropdown());
        }

        // Language options
        const languageOptions = document.querySelectorAll('.language-option');
        languageOptions.forEach(option => {
            option.addEventListener('click', (e) => this.changeLanguage(e.target.dataset.lang));
        });
    }

    initializeFormState() {
        // Initialize validation states
        const inputs = this.form.querySelectorAll('input[required]');
        inputs.forEach(input => {
            this.validationStates[input.name] = false;
        });

        // Ensure form is not in loading state initially
        this.setFormLoadingState(false);

        // Set initial submit button state
        this.updateSubmitButtonState();

        // Initialize card type indicator
        this.updateCardTypeIndicator(null);
    }

    setupAccessibilityFeatures() {
        // Add ARIA live regions for dynamic content
        const form = document.getElementById('payment-form');
        form.setAttribute('aria-live', 'polite');
        form.setAttribute('aria-atomic', 'false');

        // Set up keyboard navigation
        this.setupKeyboardNavigation();
    }

    setupKeyboardNavigation() {
        const inputs = this.form.querySelectorAll('input, button');
        inputs.forEach((input, index) => {
            input.setAttribute('tabindex', index + 1);
        });
    }

    handleCardNumberInput(event) {
        const input = event.target;
        const rawValue = input.value;
        
        // Format the card number
        const formattedValue = this.validator.formatCardNumber(rawValue);
        input.value = formattedValue;

        // Detect card type
        const digits = rawValue.replace(/\D/g, '');
        const cardType = this.validator.detectCardType(digits);
        
        if (cardType !== this.currentCardType) {
            this.currentCardType = cardType;
            this.updateCardTypeIndicator(cardType);
            this.updateCvvFieldForCardType(cardType);
        }

        // Real-time validation
        this.validateFieldRealTime(input);

        // Auto-advance to next field when complete
        if (this.isCardNumberComplete(formattedValue)) {
            this.moveToNextField(input);
        }
    }

    handleCardNumberPaste(event) {
        // Allow paste and then format the result
        setTimeout(() => {
            this.handleCardNumberInput(event);
        }, 0);
    }

    handleExpiryInput(event) {
        const input = event.target;
        const formattedValue = this.validator.formatExpiryDate(input.value);
        input.value = formattedValue;

        // Real-time validation
        this.validateFieldRealTime(input);

        // Auto-advance when complete
        if (formattedValue.length === 5) {
            this.moveToNextField(input);
        }
    }

    handleCvvInput(event) {
        const input = event.target;
        
        // Only allow digits
        input.value = input.value.replace(/\D/g, '');

        // Limit length based on card type
        const maxLength = this.getCvvMaxLength();
        if (input.value.length > maxLength) {
            input.value = input.value.substring(0, maxLength);
        }

        // Real-time validation
        this.validateFieldRealTime(input);

        // Auto-advance when complete
        if (input.value.length === maxLength) {
            this.moveToNextField(input);
        }
    }

    handleNameInput(event) {
        const input = event.target;
        
        // Only allow letters, spaces, hyphens, and periods
        input.value = input.value.replace(/[^A-Za-z\s\-\.]/g, '');
        
        // Real-time validation
        this.validateFieldRealTime(input);
    }

    handleEmailInput(event) {
        const input = event.target;
        
        // Real-time validation with debounce
        clearTimeout(this.emailValidationTimeout);
        this.emailValidationTimeout = setTimeout(() => {
            this.validateFieldRealTime(input);
        }, 500);
    }

    handlePhoneInput(event) {
        const input = event.target;
        
        // Format phone number
        let value = input.value.replace(/\D/g, '');
        if (value.startsWith('7') || value.startsWith('8')) {
            // Russian phone format
            if (value.length > 10) {
                value = value.replace(/(\d{1})(\d{3})(\d{3})(\d{2})(\d{2})/, '+7 ($2) $3-$4-$5');
            }
        }
        
        // Real-time validation
        this.validateFieldRealTime(input);
    }

    handleTermsChange(event) {
        const checkbox = event.target;
        this.validationStates['terms_agreement'] = checkbox.checked;
        this.updateSubmitButtonState();
        
        // Clear error if checked
        if (checkbox.checked) {
            this.clearFieldError(checkbox);
        }
    }

    validateField(input) {
        const fieldName = this.getFieldValidationName(input.name);
        const value = input.value;

        let result;
        
        if (fieldName === 'cardNumber') {
            result = this.validator.validateCardNumber(value);
        } else if (fieldName === 'expiryDate') {
            result = this.validator.validateExpiryDate(value);
        } else if (fieldName === 'cvv') {
            result = this.validator.validateCVV(value, this.currentCardType);
        } else if (fieldName === 'cardholderName') {
            result = this.validator.validateCardholderName(value);
        } else if (fieldName === 'email') {
            result = this.validator.validateEmail(value);
        } else if (fieldName === 'phone') {
            result = this.validator.validatePhone(value);
        } else {
            // Default validation for other fields
            result = { isValid: value.trim() !== '', errors: [] };
        }

        this.updateFieldValidation(input, result);
        this.validationStates[input.name] = result.isValid;
        this.updateSubmitButtonState();

        return result;
    }

    validateFieldRealTime(input) {
        // Only update submit button state, don't show errors until blur
        this.updateSubmitButtonState();
    }

    updateFieldValidation(input, result) {
        const errorElement = document.getElementById(input.name + '_error');
        const inputContainer = input.closest('.input-container') || input.parentElement;

        if (result.isValid) {
            input.classList.remove('error');
            input.classList.add('valid');
            if (errorElement) {
                errorElement.textContent = '';
                errorElement.hidden = true;
            }
        } else if (result.errors.length > 0) {
            input.classList.remove('valid');
            input.classList.add('error');
            if (errorElement) {
                errorElement.textContent = result.errors[0];
                errorElement.hidden = false;
            }
        }
    }

    clearFieldError(input) {
        const errorElement = document.getElementById(input.name + '_error');
        input.classList.remove('error');
        if (errorElement) {
            errorElement.textContent = '';
            errorElement.hidden = true;
        }
    }

    updateCardTypeIndicator(cardType) {
        const indicator = document.getElementById('card_type_indicator');
        const icon = document.getElementById('card_type_icon');
        
        if (indicator && icon) {
            if (cardType && this.validator.cardTypes[cardType]) {
                const cardConfig = this.validator.cardTypes[cardType];
                icon.src = cardConfig.icon;
                icon.alt = cardConfig.name;
                indicator.title = cardConfig.name;
            } else {
                icon.src = '/images/cards/unknown.svg';
                icon.alt = 'Unknown card type';
                indicator.title = 'Unknown card type';
            }
        }
    }

    updateCvvFieldForCardType(cardType) {
        const cvvInput = document.getElementById('cvv');
        const cvvHelp = document.getElementById('cvv_help');
        
        if (cardType === 'amex') {
            cvvInput.maxLength = 4;
            cvvInput.placeholder = '1234';
            if (cvvHelp) {
                cvvHelp.textContent = '4-digit security code';
            }
        } else {
            cvvInput.maxLength = 3;
            cvvInput.placeholder = '123';
            if (cvvHelp) {
                cvvHelp.textContent = '3-digit security code';
            }
        }
    }

    getCvvMaxLength() {
        if (this.currentCardType && this.validator.cardTypes[this.currentCardType]) {
            return this.validator.cardTypes[this.currentCardType].cvvLength;
        }
        return 3; // Default CVV length
    }

    isCardNumberComplete(formattedValue) {
        const digits = formattedValue.replace(/\D/g, '');
        if (this.currentCardType && this.validator.cardTypes[this.currentCardType]) {
            const validLengths = this.validator.cardTypes[this.currentCardType].lengths;
            return validLengths.includes(digits.length);
        }
        return digits.length >= 13; // Minimum card number length
    }

    moveToNextField(currentInput) {
        const formInputs = Array.from(this.form.querySelectorAll('input:not([type="hidden"])'));
        const currentIndex = formInputs.indexOf(currentInput);
        
        if (currentIndex >= 0 && currentIndex < formInputs.length - 1) {
            const nextInput = formInputs[currentIndex + 1];
            nextInput.focus();
        }
    }

    getFieldValidationName(fieldName) {
        const fieldMapping = {
            'card_number': 'cardNumber',
            'expiry_date': 'expiryDate',
            'cvv': 'cvv',
            'cardholder_name': 'cardholderName',
            'email': 'email',
            'phone': 'phone'
        };
        return fieldMapping[fieldName] || fieldName;
    }

    updateSubmitButtonState() {
        // Simplified validation - only check required fields have values
        const cardNumber = document.getElementById('card_number').value;
        const expiryDate = document.getElementById('expiry_date').value;
        const cvv = document.getElementById('cvv').value;
        const cardholderName = document.getElementById('cardholder_name').value;
        const email = document.getElementById('email').value;
        const termsAccepted = document.getElementById('terms_agreement').checked;
        
        const hasRequiredFields = cardNumber.trim() && 
                                 expiryDate.trim() && 
                                 cvv.trim() && 
                                 cardholderName.trim() && 
                                 email.trim() && 
                                 termsAccepted;
        
        this.submitButton.disabled = !hasRequiredFields;
    }

    async handleFormSubmit(event) {
        event.preventDefault();
        
        // Get form data
        const formData = new FormData(this.form);
        
        // Create the payload with correct field names for backend
        const submitData = {
            PaymentId: window.paymentData?.paymentId || formData.get('payment_token') || '',
            CardNumber: formData.get('card_number') || '',
            ExpiryDate: formData.get('expiry_date') || '',
            Cvv: formData.get('cvv') || '',
            CardholderName: formData.get('cardholder_name') || '',
            Email: formData.get('email') || '',
            Phone: formData.get('phone') || '',
            SaveCard: formData.get('save_card') === 'on',
            TermsAgreement: formData.get('terms_agreement') === 'on',
            CsrfToken: formData.get('csrf_token') || ''
        };

        // Basic validation
        if (!submitData.PaymentId) {
            this.showErrorMessage('Payment ID is missing. Please refresh the page and try again.');
            return;
        }

        // Show loading state
        this.setFormLoadingState(true);

        try {
            await this.submitPaymentForm(submitData);
            
        } catch (error) {
            console.error('Payment submission error:', error);
            this.showErrorMessage(error.message || 'Payment processing failed. Please try again.');
        } finally {
            this.setFormLoadingState(false);
        }
    }

    async submitPaymentForm(formData) {
        // Create a sanitized copy for logging
        const sanitizedData = {};
        Object.keys(formData).forEach(key => {
            sanitizedData[key] = this.validator.sanitizeForLogging(key, formData[key]);
        });
        
        console.log('Submitting payment form:', sanitizedData);
        
        try {
            // Submit to the actual payment form endpoint
            const response = await fetch('/api/v1/paymentform/submit', {
                method: 'POST',
                headers: {
                    'Content-Type': 'application/x-www-form-urlencoded',
                },
                body: new URLSearchParams(formData),
                redirect: 'manual'  // Don't automatically follow redirects
            });

            // Check if response is a redirect (status 302/301)
            if (response.status === 302 || response.status === 301) {
                // Get the redirect location from the response headers
                const redirectLocation = response.headers.get('Location');
                if (redirectLocation) {
                    // Redirect to the location specified in the header
                    window.location.href = redirectLocation;
                    return { success: true };
                }
            }

            if (!response.ok) {
                const errorData = await response.json().catch(() => ({}));
                throw new Error(errorData.error || `HTTP ${response.status}: ${response.statusText}`);
            }

            // Check if response is HTML (success page) or JSON (error)
            const contentType = response.headers.get('content-type');
            if (contentType && contentType.includes('text/html')) {
                // Success - redirect to result page
                window.location.href = response.url;
                return { success: true };
            } else {
                // JSON response (should not happen with redirect implementation, but kept for compatibility)
                const result = await response.json();
                return result;
            }
        } catch (error) {
            console.error('Payment submission error:', error);
            throw error;
        }
    }

    showValidationErrors(validationResults) {
        // Show errors for invalid fields
        Object.keys(validationResults).forEach(fieldName => {
            if (fieldName !== 'isFormValid') {
                const result = validationResults[fieldName];
                const input = this.form.querySelector(`[name="${fieldName}"]`);
                if (input && !result.isValid) {
                    this.updateFieldValidation(input, result);
                }
            }
        });

        // Focus on first invalid field
        const firstInvalidField = this.form.querySelector('.error');
        if (firstInvalidField) {
            firstInvalidField.focus();
        }
    }

    setFormLoadingState(loading) {
        const btnContent = this.submitButton.querySelector('.btn-content');
        const btnLoading = this.submitButton.querySelector('.btn-loading');
        
        if (loading) {
            btnContent.hidden = true;
            btnLoading.hidden = false;
            this.submitButton.disabled = true;
            this.form.classList.add('form-loading');
        } else {
            btnContent.hidden = false;
            btnLoading.hidden = true;
            this.updateSubmitButtonState();
            this.form.classList.remove('form-loading');
        }
    }

    showErrorMessage(message) {
        const errorAlert = document.getElementById('error-message');
        const errorText = document.getElementById('error-message-text');
        
        if (errorAlert && errorText) {
            errorText.textContent = message;
            errorAlert.hidden = false;
            errorAlert.scrollIntoView({ behavior: 'smooth', block: 'center' });
            
            // Hide after 5 seconds
            setTimeout(() => {
                errorAlert.hidden = true;
            }, 5000);
        }
    }

    showSuccessMessage(message) {
        const successAlert = document.getElementById('success-message');
        const successText = document.getElementById('success-message-text');
        
        if (successAlert && successText) {
            successText.textContent = message;
            successAlert.hidden = false;
            successAlert.scrollIntoView({ behavior: 'smooth', block: 'center' });
        }
    }

    handleCancel() {
        if (confirm('Are you sure you want to cancel this payment?')) {
            window.history.back();
        }
    }

    showCvvHelp() {
        const modal = document.getElementById('cvv_help_modal');
        if (modal) {
            modal.setAttribute('aria-hidden', 'false');
            modal.style.display = 'flex';
            
            // Focus on close button for accessibility
            const closeButton = modal.querySelector('.modal-close');
            if (closeButton) {
                closeButton.focus();
            }
        }
    }

    hideModal(modal) {
        modal.setAttribute('aria-hidden', 'true');
        modal.style.display = 'none';
    }

    toggleLanguageDropdown() {
        const dropdown = document.querySelector('.language-dropdown');
        if (dropdown) {
            dropdown.hidden = !dropdown.hidden;
        }
    }

    changeLanguage(lang) {
        document.documentElement.setAttribute('data-lang', lang);
        this.validator.setLanguage(lang);
        
        // Update language toggle display
        const toggle = document.querySelector('.language-toggle');
        const flagElement = toggle.querySelector('.language-flag');
        const codeElement = toggle.querySelector('.language-code');
        
        if (lang === 'ru') {
            flagElement.textContent = '🇷🇺';
            codeElement.textContent = 'RU';
        } else {
            flagElement.textContent = '🇺🇸';
            codeElement.textContent = 'EN';
        }
        
        // Hide dropdown
        this.toggleLanguageDropdown();
        
        // Trigger localization update (if localization module is loaded)
        if (window.PaymentLocalization) {
            window.PaymentLocalization.updateLanguage(lang);
        }
    }
}

// Initialize the payment form controller
window.PaymentFormController = PaymentFormController;
new PaymentFormController();