// =============================================================================
// Employee_Leave_Portal — EF Core Domain Models
// File: Models/Domain.cs
// =============================================================================

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace Employee_Leave_Portal.Models
{
    // ─────────────────────────────────────────────────────────────────────────
    // Enumerations
    // ─────────────────────────────────────────────────────────────────────────

    public enum EmployeeRole
    {
        Employee = 0,
        HOD = 1,
        HR = 2,
        Admin = 3
    }

    public enum LeaveType
    {
        ShortLeave = 0,   // 2 hours
        HalfDay = 1,   // 4 hours
        FullLeave = 2    // 8 hours
    }

    public enum LeaveStatus
    {
        Pending_HOD = 0,
        Pending_HR = 1,
        Approved = 2,
        Rejected = 3
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Department
    // ─────────────────────────────────────────────────────────────────────────

    public class Department
    {
        [Key]
        public int Id { get; set; }

        [Required, MaxLength(150)]
        public string DepartmentName { get; set; } = string.Empty;

        /// <summary>
        /// FK to the Employee who is the Head of Department.
        /// Nullable because the HOD employee must be inserted first.
        /// </summary>
        public int? HOD_EmployeeId { get; set; }

        // Navigation
        [ForeignKey(nameof(HOD_EmployeeId))]
        public Employee? HOD { get; set; }

        public ICollection<Employee> Employees { get; set; } = new List<Employee>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Employee
    // ─────────────────────────────────────────────────────────────────────────

    public class Employee
    {
        [Key]
        public int Id { get; set; }

        [MaxLength(20)]
        public string EmployeeCode { get; set; } = string.Empty;

        [Required, MaxLength(200)]
        public string FullName { get; set; } = string.Empty;

        /// <summary>Email is the unique business key used for upserts.</summary>
        [Required, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        public EmployeeRole Role { get; set; } = EmployeeRole.Employee;

        public int DepartmentId { get; set; }

        public bool IsActive { get; set; } = true;

        // Navigation
        [ForeignKey(nameof(DepartmentId))]
        public Department? Department { get; set; }

        public ICollection<LeaveRequest> LeaveRequests { get; set; } = new List<LeaveRequest>();
        public ICollection<LeaveBalance> LeaveBalances { get; set; } = new List<LeaveBalance>();
        public ICollection<ApprovalLog> ApprovalLogs { get; set; } = new List<ApprovalLog>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LeaveBalance
    // ─────────────────────────────────────────────────────────────────────────

    public class LeaveBalance
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public int Year { get; set; }

        /// <summary>
        /// Annual paid-leave quota. Defaults to 18.0 at year-start.
        /// Decremented by 0.5 (HalfDay / overflow ShortLeave) or 1.0 (FullLeave).
        /// </summary>
        [Column(TypeName = "decimal(5,1)")]
        public decimal PaidLeaveBalance { get; set; } = 18.0m;

        /// <summary>
        /// Tracks how many short-leaves have been used in the current calendar month.
        /// Must be reset to 0 at the start of each new month (via a scheduled job or
        /// lazy-reset strategy — see LeaveService.EnsureMonthlyReset).
        /// </summary>
        public int ShortLeaveUsedThisMonth { get; set; } = 0;

        /// <summary>
        /// The month for which ShortLeaveUsedThisMonth was last reset.
        /// Used for the lazy-reset strategy.
        /// </summary>
        public int LastResetMonth { get; set; } = DateTime.UtcNow.Month;

        /// <summary>
        /// The year for which LastResetMonth applies.
        /// </summary>
        public int LastResetYear { get; set; } = DateTime.UtcNow.Year;

        // Navigation
        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // LeaveRequest
    // ─────────────────────────────────────────────────────────────────────────

    public class LeaveRequest
    {
        [Key]
        public int Id { get; set; }

        public int EmployeeId { get; set; }

        public LeaveType LeaveType { get; set; }

        /// <summary>The calendar date on which the employee was absent.</summary>
        public DateTime LeaveDate { get; set; }

        /// <summary>UTC timestamp when the form was submitted.</summary>
        public DateTime DateApplied { get; set; } = DateTime.Now;

        /// <summary>
        /// Fixed by LeaveType: ShortLeave=2, HalfDay=4, FullLeave=8.
        /// Set programmatically — never trusted from the form POST.
        /// </summary>
        public int Hours { get; set; }

        [MaxLength(1000)]
        public string? Reason { get; set; }

        public LeaveStatus Status { get; set; } = LeaveStatus.Pending_HOD;

        /// <summary>
        /// True when the employee has no paid-leave balance remaining and a salary
        /// deduction is warranted.
        /// </summary>
        public bool IsLossOfPay { get; set; } = false;

        // Navigation
        [ForeignKey(nameof(EmployeeId))]
        public Employee? Employee { get; set; }

        public ICollection<ApprovalLog> ApprovalLogs { get; set; } = new List<ApprovalLog>();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // ApprovalLog
    // ─────────────────────────────────────────────────────────────────────────

    public class ApprovalLog
    {
        [Key]
        public int Id { get; set; }

        public int LeaveRequestId { get; set; }

        public int ActionBy_EmployeeId { get; set; }

        /// <summary>"Approved" or "Rejected"</summary>
        [Required, MaxLength(20)]
        public string Action { get; set; } = string.Empty;

        [MaxLength(1000)]
        public string? Comments { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.Now;

        // Navigation
        [ForeignKey(nameof(LeaveRequestId))]
        public LeaveRequest? LeaveRequest { get; set; }

        [ForeignKey(nameof(ActionBy_EmployeeId))]
        public Employee? ActionBy { get; set; }
    }
}
