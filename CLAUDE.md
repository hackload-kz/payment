# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

This is a payment gateway system built for HackLoad 2025 hackathon. The project consists of:
- **Payment gateway backend** (F# with Oxpecker framework) - handles payment processing, card validation, and merchant authentication
- **Frontend bookstore demo** (SvelteKit with TypeScript) - demonstrates payment integration

## Development Commands

### Backend (.NET/F#)
```bash
# Build the solution
dotnet build

# Run tests
dotnet test

# Run the payment gateway (from root directory)
dotnet run --project Payment

# Restore dependencies
dotnet restore
```

### Frontend (SvelteKit)
```bash
# Install dependencies (run from Payment.Front/)
cd Payment.Front && npm install

# Start development server
npm run dev

# Build for production  
npm run build

# Type checking
npm run check

# Watch mode type checking
npm run check:watch
```

### License Compliance
```bash
# Check license compliance (requires reuse tool)
pipx run reuse lint
```

## Architecture Overview

### Backend Core Components

**Payment.Core** (`Payment/Core.fs`) - Main business logic:
- `PaymentIntent` - Payment request structure with merchant info, amounts, callbacks
- `Transaction` - Core payment state tracking with status progression
- `BankInterface` - Simulated bank integration for card processing
- `TransactionStorage` - In-memory transaction management
- Payment flow: Intent → Card Acceptance → 3DS (if needed) → Processing → Completion

**Payment.Program** (`Payment/Program.fs`) - Web API:
- RESTful endpoints using Oxpecker framework
- Basic authentication for merchants using merchant_id/merchant_key
- OpenAPI/Swagger documentation at `/swagger`
- Card acceptance forms with server-side rendering

**Key API Endpoints:**
- `POST /payment-intent` - Create payment transaction (requires auth)
- `GET /transaction/{id}/start` - Card input form
- `POST /transaction/{id}/start` - Process card submission

**Authentication:** Basic Auth using merchant credentials from `merchants.csv`

### Frontend Structure

**SvelteKit Application** (`Payment.Front/`):
- Bookstore demo using static book data (`static/books.json`)
- TypeScript with Svelte 5
- Vite build system
- Store-based state management (`src/lib/stores.ts`)

### Configuration Files

- `payment.sln` - Visual Studio solution file
- `Directory.Packages.props` - Centralized NuGet package management
- `FAILURE_MODES.md` - Documents simulated failure scenarios for testing
- `.github/workflows/main.yml` - CI pipeline (build, test, license check)

## Payment Flow Architecture

1. **Merchant Integration:** Merchants authenticate and create payment intents
2. **Customer Redirection:** Customers redirected to hosted payment page  
3. **Card Processing:** Card validation, bank simulation, 3DS handling
4. **Status Management:** Transaction status tracking with callback notifications
5. **Failure Simulation:** Configurable failure modes for testing resilience

## Important Notes

- Uses .NET 10 Preview
- In-memory storage (data resets on restart)
- Simulated bank responses for testing
- MIT licensed with REUSE-compliant headers
- Russian language documentation and comments