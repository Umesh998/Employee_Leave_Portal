// =============================================================================
// Employee_Leave_Portal — Leave ViewModels
// File: ViewModels/LeaveViewModels.cs
// =============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using Employee_Leave_Portal.Models;

namespace Employee_Leave_Portal.ViewModels
{
    // ── Apply form ────────────────────────────────────────────────────────────

    public class ApplyLeaveVm
    {
        [Required(ErrorMessage = "Please select a leave type.")]
        [Display(Name = "Leave Type")]
        public LeaveType LeaveType { get; set; }

        [Required(ErrorMessage = "Please select a leave date.")]
        [DataType(DataType.Date)]
        [Display(Name = "Leave Date")]
        public DateTime LeaveDate { get; set; }

        /// <summary>
        /// Hours are displayed read-only on the form and set by JS based on
        /// LeaveType. Never posted back — assigned server-side in LeaveService.
        /// </summary>
        [Display(Name = "Hours")]
        public int Hours { get; set; }

        [MaxLength(1000, ErrorMessage = "Reason cannot exceed 1000 characters.")]
        [Display(Name = "Reason")]
        public string? Reason { get; set; }
    }

    // ── Dashboard ─────────────────────────────────────────────────────────────

    public class DashboardVm
    {
        public int EmployeeId { get; set; }
        public decimal PaidLeaveBalance { get; set; }
        public int ShortLeaveUsedThisMonth { get; set; }
        public int Year { get; set; }
        public bool ShowConfetti { get; set; }

        public List<LeaveRequestRowVm> Requests { get; set; } = new();
    }

    public class LeaveRequestRowVm
    {
        public int Id { get; set; }
        public string LeaveType { get; set; } = string.Empty;
        public DateTime LeaveDate { get; set; }
        public DateTime DateApplied { get; set; }
        public int Hours { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsLossOfPay { get; set; }
        public string? Reason { get; set; }
    }

    // ── Details ───────────────────────────────────────────────────────────────

    public class LeaveDetailsVm
    {
        public int Id { get; set; }
        public string LeaveType { get; set; } = string.Empty;
        public DateTime LeaveDate { get; set; }
        public DateTime DateApplied { get; set; }
        public int Hours { get; set; }
        public string? Reason { get; set; }
        public string Status { get; set; } = string.Empty;
        public bool IsLossOfPay { get; set; }

        public List<ApprovalLogRowVm> Logs { get; set; } = new();
    }

    public class ApprovalLogRowVm
    {
        public string ActionBy { get; set; } = string.Empty;
        public string Action { get; set; } = string.Empty;
        public string? Comments { get; set; }
        public DateTime Timestamp { get; set; }
    }
}
