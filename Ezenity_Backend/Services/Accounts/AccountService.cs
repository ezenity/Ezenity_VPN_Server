﻿using AutoMapper;
using AutoMapper.QueryableExtensions;
using Ezenity_Backend.Helpers;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BC = BCrypt.Net.BCrypt;
using Ezenity_Backend.Services.Common;
using Ezenity_Backend.Entities.Emails;
using Ezenity_Backend.Entities.Accounts;
using Ezenity_Backend.Helpers.Exceptions;
using Ezenity_Backend.Models;
using Ezenity_Backend.Models.Accounts;

namespace Ezenity_Backend.Services.Accounts
{
    /// <summary>
    /// Provides services related to account management such as email verification, password reset, CRUD operations, etc.
    /// </summary>
    public class AccountService : IAccountService
    {
        private readonly DataContext _context;
        private readonly IMapper _mapper;
        private readonly AppSettings _appSettings;
        private readonly IEmailService _emailService;
        private readonly ILogger<AccountService> _logger;
        private readonly TokenHelper _tokenHelper;
        private readonly IAuthService _authService;

        /// <summary>
        /// Initializes a new instance of the <see cref="AccountService"/> class with required dependencies.
        /// </summary>
        /// <param name="context">The data context for database operations.</param>
        /// <param name="mapper">The AutoMapper instance for object mapping.</param>
        /// <param name="appSettings">The application settings.</param>
        /// <param name="emailService">The service for email-related tasks.</param>
        /// <param name="logger">The logger for logging information.</param>
        /// <param name="tokenHelper">The helper for generating tokens.</param>
        /// <param name="authService">The service for authentication-related tasks.</param>
        public AccountService(DataContext context, IMapper mapper, IOptions<AppSettings> appSettings, IEmailService emailService, ILogger<AccountService> logger, TokenHelper tokenHelper, IAuthService authService)
        {
            _context = context;
            _mapper = mapper;
            _appSettings = appSettings.Value;
            _emailService = emailService;
            _logger = logger;
            _tokenHelper = tokenHelper;
            _authService = authService;
        }

        /// <summary>
        /// Authenticates a user and returns an authentication response.
        /// </summary>
        /// <param name="model">The authentication request model containing the user credentials.</param>
        /// <param name="ipAddress">The IP address of the request origin.</param>
        /// <returns>An IAuthenticateResponse object containing the authentication details.</returns>
        /// <exception cref="ResourceNotFoundException">Thrown when the account does not exist or is not verified.</exception>
        /// <exception cref="AppException">Thrown when the password is incorrect.</exception>
        public async Task<AuthenticateResponse> AuthenticateAsync(AuthenticateRequest model, string ipAddress)
        {
            var account = await FindAndValidateAccountAsync(model.Email);

            if (!BC.Verify(model.Password, account.PasswordHash))
                throw new AppException("The 'password' provided is incorrect");

            var jwtToken = _tokenHelper.GenerateJwtToken(account.Id);
            var refreshToken = _tokenHelper.GenerateNewRefreshToken(ipAddress);

            account.RefreshTokens.Add(refreshToken);
            _tokenHelper.RemoveOldRefreshTokens(account);

            _context.Update(account);
            await _context.SaveChangesAsync();

            var response = _mapper.Map<AuthenticateResponse>(account);
            response.JwtToken = jwtToken;
            response.RefreshToken = refreshToken.Token;

            return response;
        }

        /// <summary>
        /// Refreshes an existing JWT token.
        /// </summary>
        /// <param name="token">The refresh token used to issue a new JWT token.</param>
        /// <param name="ipAddress">The IP address of the client requesting the token refresh.</param>
        /// <returns>Returns an <see cref="AuthenticateResponse"/> object containing the new JWT and refresh tokens.</returns>
        /// <exception cref="Exception">Thrown when the token refresh fails.</exception>
        public async Task<AuthenticateResponse> RefreshTokenAsync(string token, string ipAddress)
        {
            using(var transaction = _context.Database.BeginTransaction())
            {
                try
                {
                    var (refrehToken, account) = await _tokenHelper.GetRefreshTokenAsync(token);
                    var newRefreshToken = _tokenHelper.GenerateNewRefreshToken(ipAddress);

                    _tokenHelper.UpdateRefreshToken(refrehToken, newRefreshToken, ipAddress);
                    _tokenHelper.RemoveOldRefreshTokens(account);

                    _context.Update(account);
                    await _context.SaveChangesAsync();

                    var jwtToken = _tokenHelper.GenerateJwtToken(account.Id);

                    var response = _mapper.Map<AuthenticateResponse>(account);
                    response.JwtToken = jwtToken;
                    response.RefreshToken = newRefreshToken.Token;

                    transaction.Commit();

                    _logger.LogInformation("Refresh token successfully updated.");

                    return response;
                }
                catch(Exception ex)
                {
                    transaction.Rollback();

                    _logger.LogError("Failed to refresh token: {0}", ex.Message);
                    throw;
                }
            }
        }

        /// <summary>
        /// Revokes a refresh token.
        /// </summary>
        /// <param name="token">The refresh token to revoke.</param>
        /// <param name="ipAddress">The IP address of the client requesting the token revocation.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        /// <exception cref="Exception">Thrown when the token revocation fails.</exception>
        public async Task RevokeTokenAsync(string token, string ipAddress)
        {
            var (refreshToken, account) = await _tokenHelper.GetRefreshTokenAsync(token);

            // Revoke token and save
            refreshToken.Revoked = DateTime.UtcNow;
            refreshToken.RevokedByIp = ipAddress;
            _context.Update(account);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Registers a new account.
        /// </summary>
        /// <param name="model">The registration request containing the user's details.</param>
        /// <param name="origin">The origin header from the client request.</param>
        /// <returns>A <see cref="Task"/> representing the asynchronous operation.</returns>
        /// <exception cref="ResourceAlreadyExistsException">Thrown when the email is already registered.</exception>
        /// <exception cref="ResourceNotFoundException">Thrown when the origin header is missing.</exception>
        public async Task RegisterAsync(RegisterRequest model, string origin)
        {
            // Check is account exists
            if (await _context.Accounts.AnyAsync(x => x.Email == model.Email))
            {
                EmailMessage alreadyRegisteredEmail = new EmailMessage
                {
                    To = model?.Email,
                    TemplateName = "alreadyRegistered",
                    DynamicValues = new Dictionary<string, string>
                    {
                        { "accountFullName", model?.FirstName + model?.LastName }
                    }
                };

                await _emailService.SendEmailAsync(alreadyRegisteredEmail);

                throw new ResourceAlreadyExistsException($"Email '{model.Email}' is already registered");
            }

            if (string.IsNullOrEmpty(origin))
                throw new ResourceNotFoundException("Origin header is missing");

            // Map model to new account object
            var account = _mapper.Map<Account>(model);

            //account.Role = _context.Accounts.Count() == 0 ? new Role { Name = "Admin" } : new Role { Name = "User" };

            ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////

            Role role;
            if (_context.Accounts.Count() == 0)
            {
                // If it's the first account, set the role to Admin
                role = _context.Roles.FirstOrDefault(r => r.Name == "Admin");
                if (role == null)
                {
                    role = new Role { Name = "Admin" };
                    _context.Roles.Add(role);
                }
            }
            else
            {
                // Otherwise, set the role to User
                role = _context.Roles.FirstOrDefault(r => r.Name == "User");
                if (role == null)
                {
                    role = new Role { Name = "User" };
                    _context.Roles.Add(role);
                }
            }

            account.Role = role;

            ////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////////


            account.Created = DateTime.UtcNow;
            account.VerificationToken = _tokenHelper.RandomTokenString();

            // Hash password
            account.PasswordHash = BC.HashPassword(model.Password);

            // Save account
            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            // Send email
            EmailMessage verifyEmail = new EmailMessage
            {
                To = model?.Email,
                TemplateName = "verification",
                DynamicValues = new Dictionary<string, string>
                {
                    { "accountFullName", model?.FirstName + model?.LastName },
                    //{ "{verificationUrl}", $"{_appSettings.BaseUrl }/account/verify-email?token={account.VerificationToken}" }
                    { "verificationUrl", $"{origin}/account/verify-email?token={account.VerificationToken}" }
                }
            };

            await _emailService.SendEmailAsync(verifyEmail);
        }

        /// <summary>
        /// Verifies an account's email using a verification token.
        /// </summary>
        /// <param name="token">The verification token.</param>
        /// <exception cref="InvalidVerificationTokenException">Thrown when the verification token is invalid or expired.</exception>
        public async Task VerifyEmailAsync(string token)
        {
            var account = await _context.Accounts.SingleOrDefaultAsync(x => x.VerificationToken == token);

            if (account == null) throw new InvalidVerificationTokenException("The verification token is invalid or expired.");

            account.Verified = DateTime.UtcNow;
            account.VerificationToken = null;

            _context.Accounts.Update(account);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Initiates the password reset process for an account identified by email.
        /// </summary>
        /// <param name="model">The request model containing the email of the account.</param>
        /// <param name="origin">The origin header from the client request.</param>
        public async Task ForgotPasswordAsync(ForgotPasswordRequest model, string origin)
        {
            var account = await _context.Accounts.SingleOrDefaultAsync(x => x.Email == model.Email);

            // Always return ok response to prevent email enumeration
            if (account == null) return;

            // Create reset token that expires after 1 day
            account.ResetToken = _tokenHelper.RandomTokenString();
            account.ResetTokenExpires = DateTime.UtcNow.AddDays(1);

            _context.Accounts.Update(account);
            await _context.SaveChangesAsync();

            // URL-encode the token
            var encodedToken = System.Net.WebUtility.UrlEncode(account.ResetToken);

            // Construct reset password URL with token
            var resetUrl = $"{origin}/account/reset-password?token={encodedToken}";

            // Send email
            EmailMessage resetEmail = new EmailMessage
            {
                To = model?.Email,
                TemplateName = "passwordReset",
                DynamicValues = new Dictionary<string, string>
                {
                    { "{accountFullName}", account.FirstName },
                    { "{token}", encodedToken },
                    { "{resetUrl}", resetUrl }
                }
            };

            await _emailService.SendEmailAsync(resetEmail);
        }

        /// <summary>
        /// Validates a password reset token.
        /// </summary>
        /// <param name="model">The request model containing the reset token.</param>
        /// <exception cref="AppException">Thrown when the reset token is invalid.</exception>
        public async Task ValidateResetTokenAsync(ValidateResetTokenRequest model)
        {
            var account = await _context.Accounts.SingleOrDefaultAsync(x =>
                x.ResetToken == model.Token &&
                x.ResetTokenExpires > DateTime.UtcNow
            );

            if (account == null)
                throw new AppException("Invalid token.");
        }

        /// <summary>
        /// Resets the account's password using a valid reset token.
        /// </summary>
        /// <param name="model">The request model containing the new password and reset token.</param>
        /// <exception cref="AppException">Thrown when the reset token is invalid.</exception>
        public async Task ResetPasswordAsync(ResetPasswordRequest model)
        {
            var account = await _context.Accounts.SingleOrDefaultAsync(x =>
                x.ResetToken == model.Token &&
                x.ResetTokenExpires > DateTime.UtcNow
            );

            if (account == null)
                throw new AppException("Invalid token.");

            // Update password and remove reset token
            account.PasswordHash = BC.HashPassword(model.Password);
            account.PasswordReset = DateTime.UtcNow;
            account.ResetToken = null;
            account.ResetTokenExpires = null;

            _context.Accounts.Update(account);
            await _context.SaveChangesAsync();
        }

        /// <summary>
        /// Retrieves all accounts.
        /// </summary>
        /// <returns>A list of all accounts.</returns>
        public async Task<IEnumerable<AccountResponse>> GetAllAsync()
        {
            return await _context.Accounts
                                    .ProjectTo<AccountResponse>(_mapper.ConfigurationProvider)
                                    .ToListAsync();
        }

        public async Task<IEnumerable<AccountResponse>> GetAllAsync(string? name, string? searchQuery)
        {
            if(string.IsNullOrEmpty(name) && string.IsNullOrWhiteSpace(searchQuery))
                return await GetAllAsync();

            var collection = _context.Accounts as IQueryable<AccountResponse>;

            if (!string.IsNullOrWhiteSpace(name))
            {
                name = name.Trim();
                collection = collection.Where(c => c.FirstName == name || c.LastName == name);
            }

            // TOD: Add search function for Lucen
            if (!string.IsNullOrWhiteSpace(searchQuery))
            {
                searchQuery = searchQuery.Trim();
                collection = collection.Where(a => a.FirstName.Contains(searchQuery) || a.LastName.Contains(searchQuery) || (a.Email != null && a.Email.Contains(searchQuery)) );
            }


            return await _context.Accounts
                                    .ProjectTo<AccountResponse>(_mapper.ConfigurationProvider)
                                    .ToListAsync();
        }

        /// <summary>
        /// Retrieves an account by its ID.
        /// </summary>
        /// <param name="id">The ID of the account.</param>
        /// <returns>The account if found; otherwise null.</returns>
        public async Task<AccountResponse> GetByIdAsync(int id)
        {
            var account = await _context.Accounts
                                    .Where(x => x.Id == id)
                                    .ProjectTo<AccountResponse>(_mapper.ConfigurationProvider)
                                    .SingleOrDefaultAsync();
            return account;
        }

        /// <summary>
        /// Creates a new account.
        /// </summary>
        /// <param name="model">The request model containing the account details.</param>
        /// <returns>The created account.</returns>
        /// <exception cref="ResourceAlreadyExistsException">Thrown when an account with the provided email already exists.</exception>
        public async Task<AccountResponse> CreateAsync(CreateAccountRequest model)
        {
            // Validate
            if (await _context.Accounts.AnyAsync(x => x.Email == model.Email))
                throw new ResourceAlreadyExistsException($"Email '{model.Email}' is already registered");

            // Map model to new account object
            var account = _mapper.Map<Account>(model);
            account.Created = DateTime.UtcNow;
            account.Verified = DateTime.UtcNow;

            // Hash password
            account.PasswordHash = BC.HashPassword(model.Password);

            // Save account
            _context.Accounts.Add(account);
            await _context.SaveChangesAsync();

            return _mapper.Map<AccountResponse>(account);
        }

        /// <summary>
        /// Updates an existing account.
        /// </summary>
        /// <param name="id">The ID of the account to update.</param>
        /// <param name="model">The request model containing the updated details of the account.</param>
        /// <returns>The updated account.</returns>
        /// <exception cref="ResourceNotFoundException">Thrown when the account to update is not found.</exception>
        /// <exception cref="AuthorizationException">Thrown when the current user is not authorized to perform the operation.</exception>
        /// <exception cref="ResourceAlreadyExistsException">Thrown when the email is already taken.</exception>
        /// <exception cref="DataAccessException">Thrown when an error occurs while updating the resource.</exception>
        public async Task<AccountResponse> UpdateAsync(int id, UpdateAccountRequest model)
        {
            int currentUserId = _authService.GetCurrentUserId();
            bool isAdmin = _authService.IsCurrentUserAdmin();
            var account = await GetAccountAsync(id);

            if (account == null)
                throw new ResourceNotFoundException($"Account with ID {id} not found.");

            if (id != currentUserId || !isAdmin)
                throw new AuthorizationException("Current user is not authorized.");

            // Validate
            if (account.Email != model.Email && await _context.Accounts.AnyAsync(x => x.Email == model.Email))
                throw new ResourceAlreadyExistsException($"Email, '{model.Email}', is already taken");

            // Hash password if it was entered
            if (!string.IsNullOrEmpty(model.Password))
                account.PasswordHash = BC.HashPassword(model.Password);

            // Copy model to accound and save
            _mapper.Map(model, account);
            account.Updated = DateTime.UtcNow;

            try
            {
                _context.Accounts.Update(account);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new DataAccessException("An error occurred while updating the resource.", ex);
            }

            return _mapper.Map<AccountResponse>(account);
        }

        /*public async Task<AccountResponse> UpdatePartiallyAsync(int id, UpdateAccountRequest model)
        {
            int currentUserId = _authService.GetCurrentUserId();
            bool isAdmin = _authService.IsCurrentUserAdmin();
            var account = await GetAccountAsync(id);

            if (account == null)
                throw new ResourceNotFoundException($"Account with ID {id} not found.");

            if (id != currentUserId || !isAdmin)
                throw new AuthorizationException("Current user is not authorized.");

            // Validate
            if (account.Email != model.Email && await _context.Accounts.AnyAsync(x => x.Email == model.Email))
                throw new ResourceAlreadyExistsException($"Email, '{model.Email}', is already taken");

            // Hash password if it was entered
            if (!string.IsNullOrEmpty(model.Password))
                account.PasswordHash = BC.HashPassword(model.Password);

            try
            {
                var updateAccountPatch = _mapper.Map<UpdateAccountRequest>(account);

                //model.ApplyTo(updateAccountPatch, ModelState); // required microsoft.aspnetcore.mvc

                var accountResponse = _mapper.Map(updateAccountPatch, account);

                await _context.SaveChangesAsync();

                return accountResponse;
            }
            catch (Exception ex)
            {
                throw new DataAccessException("An error occurred while updating the resource.", ex);
            }
        }*/

        /// <summary>
        /// Asynchronously deletes an account with the given ID.
        /// </summary>
        /// <param name="id">The ID of the account to be deleted.</param>
        /// <returns>Returns a <see cref="DeleteResponse"/> object that represents the status of the deletion.</returns>
        /// <exception cref="ResourceNotFoundException">Thrown when no account with the provided ID is found.</exception>
        /// <exception cref="AuthorizationException">Thrown when the current user is not authorized to perform the deletion.</exception>
        /// <exception cref="DeletionFailedException">Thrown when an error occurs during the deletion process.</exception>
        public async Task<DeleteResponse> DeleteAsync(int id)
        {
            int currentUserId = _authService.GetCurrentUserId();
            bool isAdmin = _authService.IsCurrentUserAdmin();
            var account = await GetAccountAsync(id);

            if (account == null)
                throw new ResourceNotFoundException($"Account with ID {id} was not found");

            if (id != currentUserId || !isAdmin)
                throw new AuthorizationException("Current user is not authorized.");

            try
            {
                _context.Accounts.Remove(account);
                await _context.SaveChangesAsync();
            }
            catch (Exception ex)
            {
                throw new DeletionFailedException($"Failed to delete account with ID {id}", ex);
            }

            return _mapper.Map<DeleteResponse>(account);
        }

        /// //////////////////
        /// Helper Methods ///
        /// //////////////////

        /// <summary>
        /// Retrieves the account associated with the given ID.
        /// </summary>
        /// <param name="id">The ID of the account to retrieve.</param>
        /// <returns>The <see cref="Account"/> object associated with the given ID.</returns>
        /// <exception cref="ResourceNotFoundException">Thrown when no account is found for the given ID.</exception>
        private async Task<Account> GetAccountAsync(int id)
        {
            var account = await _context.Accounts.FindAsync(id);
            if (account == null) throw new ResourceNotFoundException($"Account ID, {id}, not found.");
            return account;
        }

        /// <summary>
        /// Finds and validates an account based on the email provided.
        /// </summary>
        /// <param name="email">The email of the account to find and validate.</param>
        /// <returns>An IAccount object if the account exists and is verified.</returns>
        /// <exception cref="ResourceNotFoundException">Thrown when the account does not exist or is not verified.</exception>
        private async Task<Account> FindAndValidateAccountAsync(string email)
        {
            var account = await _context.Accounts.SingleOrDefaultAsync(x => x.Email == email);

            if (account == null || !account.IsVerified)
                throw new ResourceNotFoundException($"The Email, {email}, was not found or not verified.");

            return account;
        }
    }
}
