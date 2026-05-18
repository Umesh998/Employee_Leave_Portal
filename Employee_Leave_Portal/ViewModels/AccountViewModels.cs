// =============================================================================
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
}
