// Team Management Dashboard JavaScript

class TeamDashboard {
    constructor() {
        this.credentials = null;
        this.teamData = null;
        this.config = null;
        
        // Initialize with fallback API URL (relative to respect base tag)
        this.apiBaseUrl = 'api/v1/TeamManagement';
        
        this.init();
    }

    async init() {
        // Load configuration first
        await this.loadConfiguration();
        
        // Check for existing credentials
        this.loadStoredCredentials();
        
        // Bind event listeners
        this.bindEvents();
        
        // Show login modal if not authenticated
        if (!this.credentials) {
            this.showLoginModal();
        } else {
            this.hideLoginModal();
            this.loadTeamData();
        }
    }

    async loadConfiguration() {
        try {
            // First try to use window configuration if available
            const windowConfig = window.HACKLOAD_CONFIG;
            if (windowConfig && windowConfig.apiBaseUrl) {
                this.apiBaseUrl = `${windowConfig.apiBaseUrl}/TeamManagement`;
                return;
            }

            // Otherwise fetch configuration from the server using relative URL (respects base tag)
            const response = await fetch('team/config');
            if (response.ok) {
                this.config = await response.json();
                this.apiBaseUrl = this.config.Endpoints?.TeamManagement || 'api/v1/TeamManagement';
            }
        } catch (error) {
            console.warn('Failed to load configuration, using fallback URLs:', error);
            // Keep the fallback URL that was set in constructor but make it relative
            this.apiBaseUrl = 'api/v1/TeamManagement';
        }
    }

    bindEvents() {
        // Login form
        document.getElementById('login-form').addEventListener('submit', (e) => {
            e.preventDefault();
            this.handleLogin();
        });

        // Logout button
        document.getElementById('logout-btn').addEventListener('click', () => {
            this.handleLogout();
        });

        // Edit buttons
        document.getElementById('edit-info-btn').addEventListener('click', () => {
            this.toggleEditMode('team-info');
        });

        document.getElementById('edit-limits-btn').addEventListener('click', () => {
            this.toggleEditMode('payment-limits');
        });

        document.getElementById('edit-urls-btn').addEventListener('click', () => {
            this.toggleEditMode('urls');
        });

        // Cancel buttons
        document.getElementById('cancel-info-edit').addEventListener('click', () => {
            this.cancelEdit('team-info');
        });

        document.getElementById('cancel-limits-edit').addEventListener('click', () => {
            this.cancelEdit('payment-limits');
        });

        document.getElementById('cancel-urls-edit').addEventListener('click', () => {
            this.cancelEdit('urls');
        });

        // Form submissions
        document.getElementById('team-info-form').addEventListener('submit', (e) => {
            e.preventDefault();
            this.handleFormSubmit('team-info');
        });

        document.getElementById('payment-limits-form').addEventListener('submit', (e) => {
            e.preventDefault();
            this.handleFormSubmit('payment-limits');
        });

        document.getElementById('urls-form').addEventListener('submit', (e) => {
            e.preventDefault();
            this.handleFormSubmit('urls');
        });

        // Refresh stats button
        document.getElementById('refresh-stats-btn').addEventListener('click', () => {
            this.loadTeamData();
        });

        // Close modal on outside click
        document.getElementById('login-modal').addEventListener('click', (e) => {
            if (e.target === e.currentTarget) {
                if (this.credentials) {
                    this.hideLoginModal();
                }
            }
        });
    }

    // Authentication methods
    loadStoredCredentials() {
        const stored = sessionStorage.getItem('teamCredentials');
        if (stored) {
            try {
                this.credentials = JSON.parse(stored);
            } catch (e) {
                sessionStorage.removeItem('teamCredentials');
            }
        }
    }

    storeCredentials(teamSlug, password) {
        this.credentials = { teamSlug, password };
        sessionStorage.setItem('teamCredentials', JSON.stringify(this.credentials));
    }

    clearCredentials() {
        this.credentials = null;
        sessionStorage.removeItem('teamCredentials');
    }

    getAuthHeaders() {
        if (!this.credentials) return {};
        
        const { teamSlug, password } = this.credentials;
        const encoded = btoa(`${teamSlug}:${password}`);
        return {
            'Authorization': `Basic ${encoded}`,
            'Content-Type': 'application/json'
        };
    }

    async handleLogin() {
        const teamSlug = document.getElementById('team-slug').value.trim();
        const password = document.getElementById('password').value;

        if (!teamSlug || !password) {
            this.showNotification('Please enter both team slug and password', 'error');
            return;
        }

        // Store credentials temporarily to test
        this.storeCredentials(teamSlug, password);

        // Test authentication by trying to load team data
        try {
            await this.loadTeamData();
            this.hideLoginModal();
            this.showNotification('Successfully logged in!', 'success');
        } catch (error) {
            this.clearCredentials();
            if (error.status === 401) {
                this.showNotification('Invalid team credentials', 'error');
            } else {
                this.showNotification('Login failed. Please try again.', 'error');
            }
        }
    }

    handleLogout() {
        this.clearCredentials();
        this.teamData = null;
        this.hideMainContent();
        this.showLoginModal();
        this.showNotification('Logged out successfully', 'info');
    }

    // API methods
    async makeApiCall(endpoint, options = {}) {
        const url = this.apiBaseUrl + endpoint;
        const config = {
            headers: this.getAuthHeaders(),
            ...options
        };

        const response = await fetch(url, config);
        
        if (!response.ok) {
            const error = new Error(`API call failed: ${response.status}`);
            error.status = response.status;
            error.response = response;
            throw error;
        }

        return response.json();
    }

    async loadTeamData() {
        this.showLoading(true);
        
        try {
            this.teamData = await this.makeApiCall('/profile');
            this.updateDisplay();
            this.showMainContent();
        } catch (error) {
            console.error('Failed to load team data:', error);
            if (error.status === 401) {
                this.handleLogout();
            } else {
                this.showNotification('Failed to load team data', 'error');
            }
        } finally {
            this.showLoading(false);
        }
    }

    async updateTeamData(data) {
        this.showLoading(true);
        
        try {
            await this.makeApiCall('/profile', {
                method: 'PUT',
                body: JSON.stringify(data)
            });
            
            // Reload team data to get updated values
            await this.loadTeamData();
            this.showNotification('Changes saved successfully!', 'success');
        } catch (error) {
            console.error('Failed to update team data:', error);
            if (error.status === 401) {
                this.handleLogout();
            } else {
                this.showNotification('Failed to save changes', 'error');
            }
        } finally {
            this.showLoading(false);
        }
    }

    // UI methods
    showLoginModal() {
        document.getElementById('login-modal').style.display = 'flex';
        document.getElementById('team-slug').focus();
    }

    hideLoginModal() {
        document.getElementById('login-modal').style.display = 'none';
        // Clear form
        document.getElementById('login-form').reset();
    }

    showMainContent() {
        document.getElementById('main-content').style.display = 'block';
    }

    hideMainContent() {
        document.getElementById('main-content').style.display = 'none';
    }

    showLoading(show) {
        document.getElementById('loading').style.display = show ? 'flex' : 'none';
    }

    showNotification(message, type = 'info') {
        const notification = document.getElementById('notification');
        notification.textContent = message;
        notification.className = `notification ${type}`;
        notification.style.display = 'block';

        // Auto-hide after 5 seconds
        setTimeout(() => {
            notification.style.display = 'none';
        }, 5000);
    }

    updateDisplay() {
        if (!this.teamData) return;

        // Update header
        document.getElementById('team-name-display').textContent = 
            this.teamData.teamName || this.teamData.teamSlug;

        // Update team information
        this.updateElement('display-team-name', this.teamData.teamName);
        this.updateElement('display-contact-email', this.teamData.contactEmail);
        this.updateElement('display-contact-phone', this.teamData.contactPhone);
        this.updateElement('display-description', this.teamData.description);
        this.updateElement('display-timezone', this.teamData.timeZone);
        
        // Update status
        const statusElement = document.getElementById('display-status');
        statusElement.textContent = this.teamData.isActive ? 'Active' : 'Inactive';
        statusElement.className = `status-badge ${this.teamData.isActive ? 'active' : 'inactive'}`;

        // Update payment limits
        this.updateElement('display-min-payment', this.formatCurrency(this.teamData.minPaymentAmount));
        this.updateElement('display-max-payment', this.formatCurrency(this.teamData.maxPaymentAmount));
        this.updateElement('display-daily-limit', this.formatCurrency(this.teamData.dailyPaymentLimit));
        this.updateElement('display-monthly-limit', this.formatCurrency(this.teamData.monthlyPaymentLimit));
        this.updateElement('display-transaction-limit', this.teamData.dailyTransactionLimit);

        // Update URLs
        this.updateElement('display-success-url', this.teamData.successUrl);
        this.updateElement('display-fail-url', this.teamData.failUrl);
        this.updateElement('display-notification-url', this.teamData.notificationUrl);
        this.updateElement('display-cancel-url', this.teamData.cancelUrl);

        // Update statistics
        if (this.teamData.usageStats) {
            const stats = this.teamData.usageStats;
            this.updateElement('stat-total-payments', stats.totalPayments);
            // Use the team's primary supported currency or default to KZT
            const currency = this.teamData.supportedCurrencies?.[0] || this.teamData.feeCurrency || 'KZT';
            this.updateElement('stat-total-amount', this.formatCurrency(stats.totalPaymentAmount, currency));
            this.updateElement('stat-today-payments', stats.paymentsToday);
            this.updateElement('stat-month-payments', stats.paymentsThisMonth);
            this.updateElement('stat-customers', stats.totalCustomers);
            this.updateElement('stat-last-payment', this.formatDate(stats.lastPaymentAt));
        }
    }

    updateElement(id, value) {
        const element = document.getElementById(id);
        if (element) {
            element.textContent = value || '-';
        }
    }

    formatCurrency(amount, currency) {
        if (amount == null) return '-';
        
        // If no currency provided, try to get from team data
        if (!currency && this.teamData) {
            currency = this.teamData.supportedCurrencies?.[0] || this.teamData.feeCurrency || 'KZT';
        }
        currency = currency || 'KZT';
        
        const symbols = { KZT: '₸', RUB: '₽', USD: '$', EUR: '€' };
        const symbol = symbols[currency] || currency;
        
        return `${symbol}${Number(amount).toLocaleString()}`;
    }

    formatDate(dateString) {
        if (!dateString) return 'Never';
        
        const date = new Date(dateString);
        const now = new Date();
        const diff = now - date;
        
        if (diff < 60000) return 'Just now';
        if (diff < 3600000) return `${Math.floor(diff / 60000)} minutes ago`;
        if (diff < 86400000) return `${Math.floor(diff / 3600000)} hours ago`;
        if (diff < 604800000) return `${Math.floor(diff / 86400000)} days ago`;
        
        return date.toLocaleDateString();
    }

    // Form handling
    toggleEditMode(section) {
        const viewElement = document.getElementById(`${section}-view`);
        const editElement = document.getElementById(`${section}-edit`);
        
        viewElement.style.display = 'none';
        editElement.style.display = 'block';

        // Populate form with current values
        this.populateForm(section);
    }

    cancelEdit(section) {
        const viewElement = document.getElementById(`${section}-view`);
        const editElement = document.getElementById(`${section}-edit`);
        
        viewElement.style.display = 'block';
        editElement.style.display = 'none';
    }

    populateForm(section) {
        if (!this.teamData) return;

        switch (section) {
            case 'team-info':
                this.setFormValue('edit-team-name', this.teamData.teamName);
                this.setFormValue('edit-contact-email', this.teamData.contactEmail);
                this.setFormValue('edit-contact-phone', this.teamData.contactPhone);
                this.setFormValue('edit-description', this.teamData.description);
                this.setFormValue('edit-timezone', this.teamData.timeZone);
                break;
                
            case 'payment-limits':
                this.setFormValue('edit-min-payment', this.teamData.minPaymentAmount);
                this.setFormValue('edit-max-payment', this.teamData.maxPaymentAmount);
                this.setFormValue('edit-daily-limit', this.teamData.dailyPaymentLimit);
                this.setFormValue('edit-monthly-limit', this.teamData.monthlyPaymentLimit);
                this.setFormValue('edit-transaction-limit', this.teamData.dailyTransactionLimit);
                break;
                
            case 'urls':
                this.setFormValue('edit-success-url', this.teamData.successUrl);
                this.setFormValue('edit-fail-url', this.teamData.failUrl);
                this.setFormValue('edit-notification-url', this.teamData.notificationUrl);
                this.setFormValue('edit-cancel-url', this.teamData.cancelUrl);
                break;
        }
    }

    setFormValue(elementId, value) {
        const element = document.getElementById(elementId);
        if (element) {
            element.value = value || '';
        }
    }

    async handleFormSubmit(section) {
        const formId = `${section}-form`;
        const form = document.getElementById(formId);
        const formData = new FormData(form);
        
        // Convert FormData to object, handling empty values
        const data = {};
        for (const [key, value] of formData.entries()) {
            if (value.trim() !== '') {
                // Handle numeric values
                if (['minPaymentAmount', 'maxPaymentAmount', 'dailyPaymentLimit', 
                     'monthlyPaymentLimit', 'dailyTransactionLimit'].includes(key)) {
                    const numValue = parseFloat(value);
                    if (!isNaN(numValue) && numValue >= 0) {
                        data[key] = numValue;
                    }
                } else {
                    data[key] = value;
                }
            }
        }

        // Update team data
        await this.updateTeamData(data);
        
        // Return to view mode
        this.cancelEdit(section);
    }
}

// Initialize dashboard when DOM is loaded
document.addEventListener('DOMContentLoaded', () => {
    new TeamDashboard();
});

// Handle keyboard shortcuts
document.addEventListener('keydown', (e) => {
    // ESC key to close modals or cancel edits
    if (e.key === 'Escape') {
        const modal = document.getElementById('login-modal');
        if (modal.style.display === 'flex') {
            // Only close if already authenticated
            const stored = sessionStorage.getItem('teamCredentials');
            if (stored) {
                modal.style.display = 'none';
            }
        }
    }
});

// Handle window beforeunload
window.addEventListener('beforeunload', (e) => {
    // Check if there are any unsaved changes
    const editForms = document.querySelectorAll('.edit-form');
    const hasUnsavedChanges = Array.from(editForms).some(form => 
        form.style.display === 'block'
    );
    
    if (hasUnsavedChanges) {
        e.preventDefault();
        e.returnValue = 'You have unsaved changes. Are you sure you want to leave?';
    }
});