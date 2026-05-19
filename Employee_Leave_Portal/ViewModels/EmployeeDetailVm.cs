// =============================================================================
// Employee_Leave_Portal — Employee Detail ViewModel
// File: ViewModels/EmployeeDetailVm.cs
// =============================================================================

using System;
using System.Collections.Generic;

namespace Employee_Leave_Portal.ViewModels
{
    public class EmployeeDetailVm
    {
        // ── Profile ───────────────────────────────────────────────────────────
        public int Id { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
        public string Role { get; set; } = string.Empty;
        public string DepartmentName { get; set; } = string.Empty;
        public bool IsActive { get; set; }

        // ── Balance ───────────────────────────────────────────────────────────
        public int Year { get; set; }
        public decimal PaidLeaveBalance { get; set; }
        public decimal PaidLeaveUsed { get; set; }
        public int ShortLeaveUsedThisMonth { get; set; }
        public int ShortLeaveFreeRemaining => Math.Max(0, 3 - ShortLeaveUsedThisMonth);

        // ── Request summary ───────────────────────────────────────────────────
        public int TotalRequests { get; set; }
        public int ApprovedCount { get; set; }
        public int PendingCount { get; set; }
        public int RejectedCount { get; set; }
        public int LossOfPayCount { get; set; }

        // ── Leave history ─────────────────────────────────────────────────────
        public List<LeaveRequestRowVm> Requests { get; set; } = new();
    }
}
