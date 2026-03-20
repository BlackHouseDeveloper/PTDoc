// <copyright file="IUserService.cs" company="PlaceholderCompany">
// Copyright (c) PlaceholderCompany. All rights reserved.
// </copyright>

namespace PTDoc.Application.Auth
{
    using System.Security.Claims;
    using System.Threading;
    using System.Threading.Tasks;

    /// <summary>
    /// Service interface for user authentication and management.
    /// </summary>
    public interface IUserService
    {
        /// <summary>
        /// Gets a value indicating whether the user is currently authenticated.
        /// </summary>
        bool IsAuthenticated { get; }

        /// <summary>
        /// Gets a value indicating whether login is handled by an external identity provider.
        /// </summary>
        bool UsesExternalIdentityProvider { get; }

        /// <summary>
        /// Gets the display name of the active identity provider.
        /// </summary>
        string IdentityProviderDisplayName { get; }

        /// <summary>
        /// Gets a value indicating whether an external identity provider can be started from the current UI.
        /// </summary>
        bool SupportsExternalIdentityLogin { get; }

        /// <summary>
        /// Gets a value indicating whether the current UI should allow self-service registration.
        /// </summary>
        bool SupportsSelfServiceRegistration { get; }

        /// <summary>
        /// Gets the current user's claims principal.
        /// </summary>
        ClaimsPrincipal? CurrentUser { get; }

        /// <summary>
        /// Gets the current user's email if available.
        /// </summary>
        string? UserEmail { get; }

        /// <summary>
        /// Gets the current user's display name if available.
        /// </summary>
        string? UserDisplayName { get; }

        /// <summary>
        /// Authenticates a user with username and password.
        /// </summary>
        /// <param name="username">The username to authenticate.</param>
        /// <param name="password">The password to authenticate.</param>
        /// <param name="returnUrl">Optional return URL for browser-based flows.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>True if login was successful, false otherwise.</returns>
        Task<bool> LoginAsync(
          string username,
          string password,
          string? returnUrl = null,
          CancellationToken cancellationToken = default);

        /// <summary>
        /// Starts an external identity provider login flow when supported by the current platform.
        /// </summary>
        /// <param name="returnUrl">Optional return URL for browser-based flows.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>True if the external login flow was started, false otherwise.</returns>
        Task<bool> BeginExternalLoginAsync(
          string? returnUrl = null,
          CancellationToken cancellationToken = default);

        /// <summary>
        /// Registers a new user with the provided information.
        /// </summary>
        /// <param name="fullName">The user's full legal name.</param>
        /// <param name="email">The user's professional email address.</param>
        /// <param name="dateOfBirth">The user's date of birth.</param>
        /// <param name="licenseType">The type of license (PT or PTA).</param>
        /// <param name="licenseNumber">The user's license number.</param>
        /// <param name="licenseState">The state where the license was issued.</param>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>True if registration was successful, false otherwise.</returns>
        Task<bool> RegisterAsync(
          string fullName,
          string email,
          DateTime dateOfBirth,
          string licenseType,
          string licenseNumber,
          string licenseState,
          CancellationToken cancellationToken = default);

        /// <summary>
        /// Logs out the current user.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>A task representing the logout operation.</returns>
        Task LogoutAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Gets the current access token for API calls.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>The access token if available, null otherwise.</returns>
        Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default);

        /// <summary>
        /// Refreshes the current authentication token if supported.
        /// </summary>
        /// <param name="cancellationToken">Cancellation token to cancel the operation.</param>
        /// <returns>True if refresh was successful, false otherwise.</returns>
        Task<bool> RefreshTokenAsync(CancellationToken cancellationToken = default);
    }
}
