﻿using Ezenity_Backend.Entities.Accounts;
using Ezenity_Backend.Services.Common;
using Microsoft.AspNetCore.Http;

namespace Ezenity_Backend.Services.Accounts
{
    /// <summary>
    /// Service for handling authentication-related tasks.
    /// </summary>
    public class AuthService : IAuthService
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        /// <summary>
        /// Initializes a new instance of the <see cref="AuthService"/> class.
        /// </summary>
        /// <param name="httpContextAccessor">The HTTP context accessor for retrieving context information.</param>
        public AuthService(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        /// <summary>
        /// Gets the ID of the current user.
        /// </summary>
        /// <returns>The ID of the current user.</returns>
        public int GetCurrentUserId()
        {
            if (_httpContextAccessor.HttpContext.Items["Account"] is Account account)
                return account.Id;

            return 0;
        }

        /// <summary>
        /// Checks if the current user is an admin.
        /// </summary>
        /// <returns>True if the current user is an admin, false otherwise.</returns>
        public bool IsCurrentUserAdmin()
        {
            if (_httpContextAccessor.HttpContext.Items["Account"] is Account account)
                return account.Role.Name == "Admin";

            return false;
        }
    }
}
