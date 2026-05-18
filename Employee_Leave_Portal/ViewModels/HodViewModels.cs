// =============================================================================
// Employee_Leave_Portal — HOD ViewModels
// File: ViewModels/HodViewModels.cs
// =============================================================================

using System;
using System.Collections.Generic;
using Employee_Leave_Portal.ViewModels;

namespace Employee_Leave_Portal.ViewModels
{
    // ── Queue page ────────────────────────────────────────────────────────────

    public class HodQueueVm
    {
        public string DepartmentName { get; set; } = string.Empty;
        public List<HodQueueRowVm> Requests { get; set; } = new();
    }

    public class HodQueueRowVm
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeEmail { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public DateTime LeaveDate { get; set; }
        public DateTime DateApplied { get; set; }
        public int Hours { get; set; }
        public string? Reason { get; set; }
        public bool IsLossOfPay { get; set; }
    }

    // ── Review page ───────────────────────────────────────────────────────────

    public class HodReviewVm
    {
        public int Id { get; set; }
        public string EmployeeName { get; set; } = string.Empty;
        public string EmployeeEmail { get; set; } = string.Empty;
        public string LeaveType { get; set; } = string.Empty;
        public DateTime LeaveDate { get; set; }
        public DateTime DateApplied { get; set; }
        public int Hours { get; set; }
        public string? Reason { get; set; }
        public bool IsLossOfPay { get; set; }

        /// <summary>Comments the HOD types before approving or rejecting.</summary>
        public string? Comments { get; set; }

        public List<ApprovalLogRowVm> Logs { get; set; } = new();
    }
}
