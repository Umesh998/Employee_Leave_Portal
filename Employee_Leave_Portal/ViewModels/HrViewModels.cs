// =============================================================================
// Employee_Leave_Portal — HR ViewModels
// File: ViewModels/HrViewModels.cs
// =============================================================================

using System;
using System.Collections.Generic;
using Employee_Leave_Portal.ViewModels;

namespace Employee_Leave_Portal.ViewModels
{
    // ── Queue page ────────────────────────────────────────────────────────────

    public class HrQueueVm
    {
        public List<HrQueueRowVm> Requests { get; set; } = new();
    }

    public class HrQueueRowVm
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeEmail { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public DateTime LeaveDate { get; set; }
        public DateTime DateApplied { get; set; }
        public int Hours { get; set; }
        public string? Reason { get; set; }
        public bool IsLossOfPay { get; set; }
    }

    // ── Review page ───────────────────────────────────────────────────────────

    public class HrReviewVm
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeEmail { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public DateTime LeaveDate { get; set; }
        public DateTime DateApplied { get; set; }
        public int Hours { get; set; }
        public string? Reason { get; set; }
        public bool IsLossOfPay { get; set; }

        /// <summary>Employee's current paid leave balance — shown to HR for context.</summary>
        public decimal CurrentPaidBalance { get; set; }
        public int ShortLeaveUsedMonth { get; set; }

        /// <summary>Comments typed by HR before approving or rejecting.</summary>
        public string? Comments { get; set; }

        public List<ApprovalLogRowVm> Logs { get; set; } = new();
    }
}
