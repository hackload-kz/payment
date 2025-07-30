// SPDX-License-Identifier: MIT
// Copyright (c) 2025 HackLoad Payment Gateway

using Microsoft.AspNetCore.Mvc;

namespace PaymentGateway.API.Configuration;

/// <summary>
/// Simplified API versioning configuration for payment gateway endpoints
/// This is a minimal implementation to allow compilation without full versioning libraries
/// </summary>
public static class ApiVersioningConfiguration
{
    /// <summary>
    /// Configure basic API versioning for the payment gateway
    /// </summary>
    public static IServiceCollection AddPaymentGatewayApiVersioning(this IServiceCollection services)
    {
        // For now, this is a stub implementation that allows compilation
        // Full API versioning can be implemented later when needed
        return services;
    }
}