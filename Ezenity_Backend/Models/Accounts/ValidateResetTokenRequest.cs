﻿using System.ComponentModel.DataAnnotations;

namespace Ezenity_Backend.Models.Accounts
{
    /// <summary>
    /// Represents the request payload for validating a reset token.
    /// </summary>
    public class ValidateResetTokenRequest
    {
        /// <summary>
        /// Gets or sets the token to be validated.
        /// </summary>
        [Required]
        public string Token { get; set; }
    }
}
