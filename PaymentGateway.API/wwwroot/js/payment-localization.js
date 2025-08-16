/**
 * Payment Form Localization
 * Handles internationalization for English and Russian languages
 */

class PaymentLocalization {
    constructor() {
        this.currentLanguage = document.documentElement.getAttribute('data-lang') || 'en';
        this.translations = {
            en: {
                // Page elements
                page_title: 'Secure Payment - HackLoad Payment Gateway',
                skip_to_content: 'Skip to main content',
                payment_gateway: 'Payment Gateway',
                secure_connection: 'Secure Connection',
                
                // Payment summary
                payment_summary: 'Payment Summary',
                order_number: 'Order #:',
                total_amount: 'Total Amount',
                view_order_details: 'View Order Details',
                
                // Payment form
                payment_details: 'Payment Details',
                payment_info: 'Payment Info',
                verification: 'Verification',
                confirmation: 'Confirmation',
                
                // Form fields
                card_information: 'Card Information',
                card_number: 'Card Number',
                card_number_help: 'Enter your 13-19 digit card number',
                expiry_date: 'Expiry Date',
                expiry_date_help: 'MM/YY format',
                cvv: 'CVV',
                cvv_help: '3-4 digit security code',
                
                cardholder_information: 'Cardholder Information',
                cardholder_name: 'Cardholder Name',
                cardholder_name_help: 'Name as it appears on your card',
                email: 'Email Address',
                email_help: 'For payment confirmation',
                phone: 'Phone Number (Optional)',
                phone_help: 'For SMS notifications',
                
                // Agreement
                agreement_and_security: 'Agreement and Security',
                save_card: 'Save card for future payments',
                save_card_help: 'Your card will be securely tokenized and stored',
                terms_agreement: 'I agree to the <a href="/terms" target="_blank" rel="noopener">Terms of Service</a> and <a href="/privacy" target="_blank" rel="noopener">Privacy Policy</a>',
                
                // Buttons
                pay_now: 'Pay Now',
                cancel: 'Cancel',
                processing: 'Processing...',
                
                // Security info
                security_info: 'Security Information',
                ssl_encryption: '256-bit SSL Encryption',
                ssl_description: 'Your data is encrypted and secure',
                pci_compliance: 'PCI DSS Compliant',
                pci_description: 'Meets industry security standards',
                fraud_protection: 'Fraud Protection',
                fraud_description: 'Advanced security monitoring',
                
                // Footer
                help: 'Help',
                contact: 'Contact',
                terms: 'Terms',
                privacy: 'Privacy',
                powered_by: 'Powered by',
                
                // CVV Modal
                cvv_help_title: 'Where to find CVV',
                cvv_explanation: 'The CVV (Card Verification Value) is a 3 or 4 digit security code on your card:',
                cvv_visa_mc: 'Visa/MasterCard: 3 digits on the back',
                cvv_amex: 'American Express: 4 digits on the front',
                
                // Messages
                error_occurred: 'An error occurred',
                success: 'Success',
                payment_successful: 'Payment processed successfully!',
                payment_failed: 'Payment processing failed. Please try again.',
                confirm_cancel: 'Are you sure you want to cancel this payment?',
                
                // Timer messages
                payment_expires_in: 'Payment expires in:',
                timer_timezone_info: 'Time shown in your local timezone',
                payment_expiring_soon: 'Payment expires soon',
                payment_expired: 'Payment has expired',
                timer_minutes: 'minutes',
                timer_minute: 'minute',
                timer_seconds: 'seconds',
                timer_second: 'second'
            },
            ru: {
                // Page elements
                page_title: 'Безопасная оплата - Платёжный шлюз HackLoad',
                skip_to_content: 'Перейти к основному содержанию',
                payment_gateway: 'Платёжный шлюз',
                secure_connection: 'Защищённое соединение',
                
                // Payment summary
                payment_summary: 'Сводка платежа',
                order_number: 'Заказ №:',
                total_amount: 'Общая сумма',
                view_order_details: 'Показать детали заказа',
                
                // Payment form
                payment_details: 'Данные платежа',
                payment_info: 'Информация об оплате',
                verification: 'Проверка',
                confirmation: 'Подтверждение',
                
                // Form fields
                card_information: 'Информация о карте',
                card_number: 'Номер карты',
                card_number_help: 'Введите номер карты (13-19 цифр)',
                expiry_date: 'Срок действия',
                expiry_date_help: 'Формат ММ/ГГ',
                cvv: 'CVV',
                cvv_help: 'Код безопасности (3-4 цифры)',
                
                cardholder_information: 'Информация о держателе карты',
                cardholder_name: 'Имя держателя карты',
                cardholder_name_help: 'Имя как указано на карте',
                email: 'Адрес электронной почты',
                email_help: 'Для подтверждения платежа',
                phone: 'Номер телефона (необязательно)',
                phone_help: 'Для SMS-уведомлений',
                
                // Agreement
                agreement_and_security: 'Соглашение и безопасность',
                save_card: 'Сохранить карту для будущих платежей',
                save_card_help: 'Ваша карта будет безопасно токенизирована и сохранена',
                terms_agreement: 'Я согласен с <a href="/terms" target="_blank" rel="noopener">Условиями обслуживания</a> и <a href="/privacy" target="_blank" rel="noopener">Политикой конфиденциальности</a>',
                
                // Buttons
                pay_now: 'Оплатить',
                cancel: 'Отмена',
                processing: 'Обработка...',
                
                // Security info
                security_info: 'Информация о безопасности',
                ssl_encryption: '256-битное SSL-шифрование',
                ssl_description: 'Ваши данные зашифрованы и защищены',
                pci_compliance: 'Соответствие PCI DSS',
                pci_description: 'Соответствует отраслевым стандартам безопасности',
                fraud_protection: 'Защита от мошенничества',
                fraud_description: 'Расширенный мониторинг безопасности',
                
                // Footer
                help: 'Помощь',
                contact: 'Контакты',
                terms: 'Условия',
                privacy: 'Конфиденциальность',
                powered_by: 'Работает на',
                
                // CVV Modal
                cvv_help_title: 'Где найти CVV',
                cvv_explanation: 'CVV (Card Verification Value) — это код безопасности из 3 или 4 цифр на вашей карте:',
                cvv_visa_mc: 'Visa/MasterCard: 3 цифры на обратной стороне',
                cvv_amex: 'American Express: 4 цифры на лицевой стороне',
                
                // Messages
                error_occurred: 'Произошла ошибка',
                success: 'Успешно',
                payment_successful: 'Платёж успешно обработан!',
                payment_failed: 'Обработка платежа не удалась. Пожалуйста, попробуйте снова.',
                confirm_cancel: 'Вы уверены, что хотите отменить этот платёж?',
                
                // Timer messages
                payment_expires_in: 'Платёж истекает через:',
                timer_timezone_info: 'Время показано в вашем часовом поясе',
                payment_expiring_soon: 'Платёж скоро истечёт',
                payment_expired: 'Платёж истёк',
                timer_minutes: 'минут',
                timer_minute: 'минута',
                timer_seconds: 'секунд',
                timer_second: 'секунда'
            }
        };

        this.initialize();
    }

    initialize() {
        this.updateLanguage(this.currentLanguage);
        console.log('Payment localization initialized with language:', this.currentLanguage);
    }

    /**
     * Update page language
     * @param {string} lang - Language code (en/ru)
     */
    updateLanguage(lang) {
        if (!this.translations[lang]) {
            console.warn('Language not supported:', lang);
            return;
        }

        this.currentLanguage = lang;
        document.documentElement.setAttribute('data-lang', lang);
        
        // Update all elements with data-i18n attributes
        const elements = document.querySelectorAll('[data-i18n]');
        elements.forEach(element => {
            const key = element.getAttribute('data-i18n');
            const translation = this.getTranslation(key);
            
            if (translation) {
                if (element.tagName === 'INPUT' && element.type === 'submit') {
                    element.value = translation;
                } else if (element.hasAttribute('placeholder')) {
                    element.placeholder = translation;
                } else if (element.hasAttribute('title')) {
                    element.title = translation;
                } else if (element.hasAttribute('aria-label')) {
                    element.setAttribute('aria-label', translation);
                } else {
                    // Handle HTML content (for links in terms agreement)
                    if (translation.includes('<a')) {
                        element.innerHTML = translation;
                    } else {
                        element.textContent = translation;
                    }
                }
            }
        });

        // Update document title
        const titleTranslation = this.getTranslation('page_title');
        if (titleTranslation) {
            document.title = titleTranslation;
        }

        // Update meta description
        const metaDescription = document.querySelector('meta[name="description"]');
        if (metaDescription) {
            const descKey = lang === 'ru' ? 'meta_description_ru' : 'meta_description_en';
            const description = this.getTranslation(descKey);
            if (description) {
                metaDescription.setAttribute('content', description);
            }
        }

        // Update HTML lang attribute
        document.documentElement.setAttribute('lang', lang === 'ru' ? 'ru' : 'en');

        // Update currency and number formatting
        this.updateCurrencyDisplay();

        // Trigger custom event for other components
        document.dispatchEvent(new CustomEvent('languageChanged', {
            detail: { language: lang }
        }));

        console.log('Language updated to:', lang);
    }

    /**
     * Get translation for a key
     * @param {string} key - Translation key
     * @returns {string} Translated text
     */
    getTranslation(key) {
        return this.translations[this.currentLanguage]?.[key] || 
               this.translations['en'][key] || 
               key;
    }

    /**
     * Update currency display based on language
     */
    updateCurrencyDisplay() {
        const currencyElements = document.querySelectorAll('#currency, #submit_currency');
        const amountElements = document.querySelectorAll('#payment-amount, #submit_amount');
        
        // In a real implementation, you might want to convert currencies
        // For now, we'll just update the display format
        currencyElements.forEach(element => {
            if (this.currentLanguage === 'ru') {
                element.textContent = 'KZT';
            } else {
                element.textContent = 'KZT'; // Keep RUB for consistency
            }
        });

        // Update number formatting
        amountElements.forEach(element => {
            const amount = parseFloat(element.textContent.replace(/[^\d.-]/g, ''));
            if (!isNaN(amount)) {
                element.textContent = this.formatAmount(amount);
            }
        });
    }

    /**
     * Format amount according to locale
     * @param {number} amount - Amount to format
     * @returns {string} Formatted amount
     */
    formatAmount(amount) {
        const locale = this.currentLanguage === 'ru' ? 'ru-RU' : 'en-US';
        
        try {
            return new Intl.NumberFormat(locale, {
                minimumFractionDigits: 2,
                maximumFractionDigits: 2
            }).format(amount);
        } catch (error) {
            // Fallback formatting
            return amount.toFixed(2).replace(/\B(?=(\d{3})+(?!\d))/g, 
                this.currentLanguage === 'ru' ? ' ' : ',');
        }
    }

    /**
     * Get current language
     * @returns {string} Current language code
     */
    getCurrentLanguage() {
        return this.currentLanguage;
    }

    /**
     * Get available languages
     * @returns {string[]} Array of available language codes
     */
    getAvailableLanguages() {
        return Object.keys(this.translations);
    }

    /**
     * Check if a language is supported
     * @param {string} lang - Language code to check
     * @returns {boolean} True if language is supported
     */
    isLanguageSupported(lang) {
        return lang in this.translations;
    }

    /**
     * Get localized error message
     * @param {string} errorType - Type of error
     * @param {Object} context - Additional context
     * @returns {string} Localized error message
     */
    getErrorMessage(errorType, context = {}) {
        const errorMessages = {
            en: {
                validation_required: 'This field is required',
                validation_invalid_card: 'Please enter a valid card number',
                validation_invalid_expiry: 'Please enter a valid expiry date',
                validation_expired_card: 'This card has expired',
                validation_invalid_cvv: 'Please enter a valid CVV',
                validation_invalid_name: 'Please enter a valid name',
                validation_invalid_email: 'Please enter a valid email address',
                validation_invalid_phone: 'Please enter a valid phone number',
                payment_declined: 'Your payment was declined. Please try a different card.',
                payment_network_error: 'Network error. Please check your connection and try again.',
                payment_timeout: 'Payment timed out. Please try again.',
                payment_server_error: 'Server error. Please try again later.'
            },
            ru: {
                validation_required: 'Это поле обязательно для заполнения',
                validation_invalid_card: 'Пожалуйста, введите действительный номер карты',
                validation_invalid_expiry: 'Пожалуйста, введите действительную дату истечения',
                validation_expired_card: 'Срок действия этой карты истёк',
                validation_invalid_cvv: 'Пожалуйста, введите действительный CVV',
                validation_invalid_name: 'Пожалуйста, введите действительное имя',
                validation_invalid_email: 'Пожалуйста, введите действительный адрес электронной почты',
                validation_invalid_phone: 'Пожалуйста, введите действительный номер телефона',
                payment_declined: 'Ваш платёж был отклонён. Пожалуйста, попробуйте другую карту.',
                payment_network_error: 'Ошибка сети. Проверьте подключение и попробуйте снова.',
                payment_timeout: 'Время ожидания платежа истекло. Пожалуйста, попробуйте снова.',
                payment_server_error: 'Ошибка сервера. Пожалуйста, попробуйте позже.'
            }
        };

        return errorMessages[this.currentLanguage]?.[errorType] || 
               errorMessages['en'][errorType] || 
               'An error occurred';
    }

    /**
     * Update placeholder text for form fields
     */
    updatePlaceholders() {
        const placeholders = {
            en: {
                card_number: '0000 0000 0000 0000',
                expiry_date: 'MM/YY',
                cvv: '123',
                cardholder_name: 'John Doe',
                email: 'john@example.com',
                phone: '+1 (555) 123-4567'
            },
            ru: {
                card_number: '0000 0000 0000 0000',
                expiry_date: 'ММ/ГГ',
                cvv: '123',
                cardholder_name: 'Иван Иванов',
                email: 'ivan@example.com',
                phone: '+7 (900) 123-45-67'
            }
        };

        const currentPlaceholders = placeholders[this.currentLanguage];
        if (currentPlaceholders) {
            Object.keys(currentPlaceholders).forEach(fieldName => {
                const field = document.getElementById(fieldName);
                if (field) {
                    field.placeholder = currentPlaceholders[fieldName];
                }
            });
        }
    }

    /**
     * Format phone number according to locale
     * @param {string} phone - Phone number to format
     * @returns {string} Formatted phone number
     */
    formatPhoneNumber(phone) {
        const digits = phone.replace(/\D/g, '');
        
        if (this.currentLanguage === 'ru') {
            // Russian phone format: +7 (900) 123-45-67
            if (digits.length === 11 && (digits.startsWith('7') || digits.startsWith('8'))) {
                const normalized = digits.startsWith('8') ? '7' + digits.slice(1) : digits;
                return `+${normalized[0]} (${normalized.slice(1, 4)}) ${normalized.slice(4, 7)}-${normalized.slice(7, 9)}-${normalized.slice(9, 11)}`;
            }
        } else {
            // US phone format: +1 (555) 123-4567
            if (digits.length === 10) {
                return `+1 (${digits.slice(0, 3)}) ${digits.slice(3, 6)}-${digits.slice(6, 10)}`;
            } else if (digits.length === 11 && digits.startsWith('1')) {
                return `+${digits[0]} (${digits.slice(1, 4)}) ${digits.slice(4, 7)}-${digits.slice(7, 11)}`;
            }
        }
        
        return phone; // Return original if no formatting rules match
    }
}

// Initialize localization
window.PaymentLocalization = new PaymentLocalization();