# Token Generation Technical Specification

## Overview
The Token generation mechanism provides request authentication and integrity verification for the I-Business payment API. Every API request requiring authentication must include a unique token generated using the merchant's secret credentials and request parameters.

## Security Purpose
- **Authentication**: Verifies request originates from authorized merchant
- **Integrity**: Ensures request data hasn't been tampered with during transmission
- **Replay Protection**: Each token is unique per request, preventing replay attacks
- **Trust Verification**: Server validates merchant identity before processing operations

## Required Merchant Credentials

### Information from I-Business Merchant Cabinet
Merchants must obtain the following credentials from their I-Business merchant portal:

1. **TerminalKey** (string, ≤20 characters)
   - Unique terminal identifier
   - Issued during terminal setup in I-Business
   - Used in all API requests
   - **Location**: Terminal settings section

2. **Password** (string)
   - Secret key for token generation
   - **Critical**: Must be kept secure and never transmitted
   - **Location**: Terminal security settings or API credentials section
   - **Usage**: Only for local token generation, never sent in requests

3. **API Endpoint URLs**
   - Production and sandbox environment URLs
   - **Location**: Integration settings or developer documentation section

### Security Requirements for Merchant
- **Password Storage**: Store password securely (encrypted, environment variables, secure vault)
- **Access Control**: Limit password access to authorized personnel only
- **Rotation**: Follow password rotation policies as recommended by I-Business
- **Environment Separation**: Use different credentials for testing and production

## Token Generation Algorithm

### Step-by-Step Process

#### 1. Parameter Collection
Collect **only root-level parameters** from the request body:
- Include: All scalar values at the root level
- Exclude: Nested objects (Receipt, DATA, Shops, etc.)
- Exclude: Arrays and complex structures

**Example for Init method:**
```json
// Original request
{
  "TerminalKey": "MerchantTerminalKey",
  "Amount": 19200,
  "OrderId": "21090", 
  "Description": "Подарочная карта на 1000 рублей",
  "DATA": { ... },      // EXCLUDED - nested object
  "Receipt": { ... }    // EXCLUDED - nested object
}

// Parameters for token generation
[
  {"TerminalKey": "MerchantTerminalKey"},
  {"Amount": "19200"},
  {"OrderId": "21090"},
  {"Description": "Подарочная карта на 1000 рублей"}
]
```

#### 2. Password Addition
Add the merchant password as a key-value pair:
```json
[
  {"TerminalKey": "MerchantTerminalKey"},
  {"Amount": "19200"},
  {"OrderId": "21090"},
  {"Description": "Подарочная карта на 1000 рублей"},
  {"Password": "usaf8fw8fsw21g"}  // From merchant cabinet
]
```

#### 3. Alphabetical Sorting
Sort the array alphabetically by key names:
```json
[
  {"Amount": "19200"},
  {"Description": "Подарочная карта на 1000 рублей"},
  {"OrderId": "21090"},
  {"Password": "usaf8fw8fsw21g"},
  {"TerminalKey": "MerchantTerminalKey"}
]
```

#### 4. Value Concatenation
Concatenate **only the values** into a single string:
```
"19200Подарочная карта на 1000 рублей21090usaf8fw8fsw21gMerchantTerminalKey"
```

#### 5. SHA-256 Hashing
Apply SHA-256 hash function with UTF-8 encoding:
```
Input:  "19200Подарочная карта на 1000 рублей21090usaf8fw8fsw21gMerchantTerminalKey"
Output: "0024a00af7c350a3a67ca168ce06502aa72772456662e38696d48b56ee9c97d9"
```

#### 6. Token Integration
Add the generated hash as the Token parameter in the request:
```json
{
  "TerminalKey": "MerchantTerminalKey",
  "Amount": 19200,
  "OrderId": "21090",
  "Description": "Подарочная карта на 1000 рублей",
  "Token": "0024a00af7c350a3a67ca168ce06502aa72772456662e38696d48b56ee9c97d9",
  "DATA": { ... },
  "Receipt": { ... }
}
```

## Implementation Guidelines

### Programming Language Examples

#### Python Implementation
```python
import hashlib
import json

def generate_token(request_params, password):
    # Step 1: Extract root-level parameters
    token_params = {}
    for key, value in request_params.items():
        if not isinstance(value, (dict, list)):
            token_params[key] = str(value)
    
    # Step 2: Add password
    token_params['Password'] = password
    
    # Step 3: Sort by key
    sorted_keys = sorted(token_params.keys())
    
    # Step 4: Concatenate values
    concatenated = ''.join(token_params[key] for key in sorted_keys)
    
    # Step 5: Generate SHA-256 hash
    token = hashlib.sha256(concatenated.encode('utf-8')).hexdigest()
    
    return token
```

#### JavaScript Implementation
```javascript
const crypto = require('crypto');

function generateToken(requestParams, password) {
    // Step 1: Extract root-level parameters
    const tokenParams = {};
    for (const [key, value] of Object.entries(requestParams)) {
        if (typeof value !== 'object') {
            tokenParams[key] = String(value);
        }
    }
    
    // Step 2: Add password
    tokenParams.Password = password;
    
    // Step 3: Sort by key
    const sortedKeys = Object.keys(tokenParams).sort();
    
    // Step 4: Concatenate values
    const concatenated = sortedKeys.map(key => tokenParams[key]).join('');
    
    // Step 5: Generate SHA-256 hash
    const token = crypto.createHash('sha256').update(concatenated, 'utf8').digest('hex');
    
    return token;
}
```

### Critical Implementation Notes

#### Parameter Handling
- **Data Types**: Convert all values to strings before concatenation
- **Null Values**: Handle null/undefined values appropriately
- **Boolean Values**: Convert to string representation ("true"/"false")
- **Numeric Values**: Convert to string without formatting

#### Character Encoding
- **Always use UTF-8** encoding for hash generation
- Ensure proper handling of Unicode characters
- Test with non-ASCII characters in descriptions

#### Sorting Considerations
- Use case-sensitive alphabetical sorting
- Ensure consistent sorting across different programming languages
- Test with various parameter combinations

## Validation and Testing

### Token Validation in I-Business Cabinet
1. Navigate to **Operations** section in merchant cabinet
2. Select the specific order/transaction
3. Go to **Additional Order Information**
4. Check **inittokenisvalid** field:
   - `true`: Token is valid
   - `false`: Token is incorrect

### Testing Strategy
1. **Unit Tests**: Test token generation with known inputs
2. **Integration Tests**: Verify tokens work with actual API calls
3. **Edge Cases**: Test with special characters, empty values, different parameter combinations
4. **Cross-Language**: Ensure consistent results across different implementations

## Security Best Practices

### Development Environment
- **Never log passwords** in application logs
- **Use environment variables** for password storage
- **Separate credentials** for development/staging/production
- **Regular password rotation** as per security policies

### Production Environment
- **Secure credential storage** (AWS Secrets Manager, Azure Key Vault, etc.)
- **Access logging** for credential usage
- **Monitoring** for failed authentication attempts
- **Backup procedures** for credential recovery

### Common Security Mistakes to Avoid
- ❌ Including password in request body
- ❌ Storing password in client-side code
- ❌ Using the same password across environments
- ❌ Logging token generation parameters
- ❌ Hardcoding credentials in source code

## Error Handling

### Common Token Generation Errors
1. **Parameter Exclusion**: Including nested objects in token calculation
2. **Sorting Issues**: Incorrect alphabetical ordering
3. **Encoding Problems**: Using wrong character encoding
4. **Type Conversion**: Improper string conversion of numeric/boolean values

### Debugging Tips
1. **Log concatenated string** (without password) for debugging
2. **Verify parameter extraction** from request body
3. **Test sorting algorithm** with sample data
4. **Compare hash outputs** with known good examples
5. **Use cabinet validation** to verify token correctness

This token generation mechanism ensures secure, authenticated communication between merchant systems and the I-Business payment platform while maintaining request integrity and preventing unauthorized access.