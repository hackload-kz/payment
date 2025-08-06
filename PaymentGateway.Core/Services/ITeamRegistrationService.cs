using PaymentGateway.Core.DTOs.TeamRegistration;
using PaymentGateway.Core.Entities;

namespace PaymentGateway.Core.Services;

/// <summary>
/// Interface for team registration and management services
/// </summary>
public interface ITeamRegistrationService
{
    /// <summary>
    /// Register a new team/merchant
    /// </summary>
    /// <param name="request">Registration request data</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Registration response with team details</returns>
    Task<TeamRegistrationResponseDto> RegisterTeamAsync(TeamRegistrationRequestDto request, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get team status and details
    /// </summary>
    /// <param name="teamSlug">Team slug identifier</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Team entity or null if not found</returns>
    Task<Team?> GetTeamStatusAsync(string teamSlug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Check if team slug is available for registration
    /// </summary>
    /// <param name="teamSlug">Team slug to check</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>True if available, false if taken</returns>
    Task<bool> IsTeamSlugAvailableAsync(string teamSlug, CancellationToken cancellationToken = default);

    /// <summary>
    /// Update team information
    /// </summary>
    /// <param name="teamId">Team ID</param>
    /// <param name="updateData">Data to update</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Updated team entity</returns>
    Task<Team> UpdateTeamAsync(Guid teamId, Dictionary<string, object> updateData, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activate or deactivate a team
    /// </summary>
    /// <param name="teamSlug">Team slug</param>
    /// <param name="isActive">Activation status</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>Success status</returns>
    Task<bool> SetTeamActiveStatusAsync(string teamSlug, bool isActive, CancellationToken cancellationToken = default);
}