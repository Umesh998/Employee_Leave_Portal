// =============================================================================
// Employee_Leave_Portal — Admin ViewModels
// File: ViewModels/AdminViewModels.cs
// =============================================================================

using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Employee_Leave_Portal.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace Employee_Leave_Portal.ViewModels
{
    // ── Employee list ─────────────────────────────────────────────────────────

    public class EmployeeRowVm
    {
        public int Id { get; set; }

        public string EmployeeCode { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        
        public bool IsActive { get; set; }
    }

    // ── Add single employee ───────────────────────────────────────────────────

    public class AddEmployeeVm
    {

        [Required, MaxLength(20)]
        [Display(Name = "Employee ID")]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Required, EmailAddress, MaxLength(256)]
        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;

        [Required]
        [Display(Name = "Role")]
        public EmployeeRole Role { get; set; } = EmployeeRole.Employee;

        [Required]
        [Display(Name = "Department")]
        public int DepartmentId { get; set; }

        

        // Populated by controller — not posted back
        public List<SelectListItem> Departments { get; set; } = new();
    }

    // ── Edit employee ─────────────────────────────────────────────────────────

    public class EditEmployeeVm
    {
        public int Id { get; set; }

        [Required, MaxLength(20)]
        [Display(Name = "Employee ID")]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        [Display(Name = "Full Name")]
        public string FullName { get; set; } = string.Empty;

        [Display(Name = "Email")]
        public string Email { get; set; } = string.Empty;   // read-only, not editable

        [Required]
        [Display(Name = "Role")]
        public EmployeeRole Role { get; set; }

        [Required]
        [Display(Name = "Department")]
        public int DepartmentId { get; set; }

       

        [Display(Name = "Active")]
        public bool IsActive { get; set; }

        public List<SelectListItem> Departments { get; set; } = new();
    }

    // ── Bulk upload ───────────────────────────────────────────────────────────

    public class BulkUploadVm
    {
        // Holds any model-level error surfaced before parsing begins
        public string? ErrorMessage { get; set; }
    }

    public class BulkUploadResultVm
    {
        public int TotalRows { get; set; }
        public int Inserted { get; set; }
        public int Updated { get; set; }
        public List<string> Errors { get; set; } = new();
    }


    public class EditBalanceVm
    {
        public int EmployeeId { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public int Year { get; set; }

        [Required]
        [Range(0, 18)]
        [Display(Name = "Paid Leave Balance")]
        public decimal PaidLeaveBalance { get; set; }

        [Required]
        [Range(0, 3)]
        [Display(Name = "Short Leaves Used This Month")]
        public int ShortLeaveUsedThisMonth { get; set; }
    }
}
