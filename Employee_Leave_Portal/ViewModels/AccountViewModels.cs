// =============================================================================
// File: ViewModels/AccountViewModels.cs
// =============================================================================

// =============================================================================
// Employee_Leave_Portal — Account ViewModels
// File: ViewModels/AccountViewModels.cs
// =============================================================================

using System.ComponentModel.DataAnnotations;

namespace Employee_Leave_Portal.ViewModels
{
    public class LoginVm
    {
        [Required, EmailAddress]
        [Display(Name = "Email Address")]
        public string Email { get; set; } = string.Empty;

        [Display(Name = "Remember me")]
        public bool RememberMe { get; set; }
    }

    public class VerifyOtpVm
    {
        [Required]
        public string Email { get; set; } = string.Empty;

        [Required(ErrorMessage = "Please enter the OTP sent to your email.")]
        [StringLength(6, MinimumLength = 6, ErrorMessage = "OTP must be 6 digits.")]
        [Display(Name = "One-Time Password")]
        public string Otp { get; set; } = string.Empty;
    }
}

