// =============================================================================
// Employee_Leave_Portal — HrController
// File: Controllers/HrController.cs
// =============================================================================
//
// Actions:
//   GET  /Hr/Queue              — all requests where Status == Pending_HR
//   GET  /Hr/Review/{id}        — full detail view before final decision
//   POST /Hr/Approve/{id}       — final approval → balance deducted, Approved
//   POST /Hr/Reject/{id}        — terminal rejection at HR stage
//
// Access: Role = HR only (enforced via [Authorize(Roles = "HR")])
//
// =============================================================================

using System;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
using Employee_Leave_Portal.Services;
using Employee_Leave_Portal.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Controllers
{
    [Authorize(Roles = "HR")]
    public class HrController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IApprovalService _approvalService;

        public HrController(AppDbContext db, IApprovalService approvalService)
        {
            _db = db;
            _approvalService = approvalService;
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private int CurrentEmployeeId()
            => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Hr/Queue ─────────────────────────────────────────────────────
        //
        // HR sees every request across all departments where Status == Pending_HR.
        // No department filter — HR is org-wide.

        public async Task<IActionResult> Queue()
        {
            var requests = await _db.LeaveRequests
                .Include(lr => lr.Employee)
                    .ThenInclude(e => e!.Department)
                .Where(lr => lr.Status == LeaveStatus.Pending_HR)
                .OrderBy(lr => lr.DateApplied)
                .Select(lr => new HrQueueRowVm
                {
                    Id = lr.Id,
                    EmployeeName = lr.Employee!.FullName,
                    EmployeeEmail = lr.Employee.Email,
                    DepartmentName = lr.Employee.Department!.DepartmentName,
                    LeaveType = lr.LeaveType.ToString(),
                    LeaveDate = lr.LeaveDate,
                    DateApplied = lr.DateApplied,
                    Hours = lr.Hours,
                    Reason = lr.Reason,
                    IsLossOfPay = lr.IsLossOfPay
                })
                .ToListAsync();

            var vm = new HrQueueVm { Requests = requests };
            return View(vm);
        }

        // ── GET /Hr/Review/{id} ───────────────────────────────────────────────
        //
        // Loads full request detail plus the complete approval log so HR can
        // see the HOD's comments before making the final call.

        public async Task<IActionResult> Review(int id)
        {
            var request = await _db.LeaveRequests
                .Include(lr => lr.Employee)
                    .ThenInclude(e => e!.Department)
                .Include(lr => lr.ApprovalLogs)
                    .ThenInclude(al => al.ActionBy)
                .FirstOrDefaultAsync(lr =>
                    lr.Id == id &&
                    lr.Status == LeaveStatus.Pending_HR);

            if (request is null)
                return NotFound();

            // Load the employee's current leave balance so HR can see it
            // alongside the request before approving.
            var balance = await _db.LeaveBalances
                .FirstOrDefaultAsync(lb =>
                    lb.EmployeeId == request.EmployeeId &&
                    lb.Year == request.LeaveDate.Year);

            var vm = new HrReviewVm
            {
                Id = request.Id,
                EmployeeName = request.Employee!.FullName,
                EmployeeEmail = request.Employee.Email,
                DepartmentName = request.Employee.Department!.DepartmentName,
                LeaveType = request.LeaveType.ToString(),
                LeaveDate = request.LeaveDate,
                DateApplied = request.DateApplied,
                Hours = request.Hours,
                Reason = request.Reason,
                IsLossOfPay = request.IsLossOfPay,
                CurrentPaidBalance = balance?.PaidLeaveBalance ?? 0m,
                ShortLeaveUsedMonth = balance?.ShortLeaveUsedThisMonth ?? 0,
                Logs = request.ApprovalLogs
                    .OrderBy(al => al.Timestamp)
                    .Select(al => new ApprovalLogRowVm
                    {
                        ActionBy = al.ActionBy!.FullName,
                        Action = al.Action,
                        Comments = al.Comments,
                        Timestamp = al.Timestamp
                    })
                    .ToList()
            };

            return View(vm);
        }

        // ── POST /Hr/Approve/{id} ─────────────────────────────────────────────
        //
        // Final approval gate. ApprovalService.HrDecisionAsync will:
        //   1. Transition Status → Approved
        //   2. Call FinaliseApprovalAsync to commit the balance deduction
        //   3. Write the ApprovalLog entry

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? comments)
        {
            int hrId = CurrentEmployeeId();

            var (success, message) = await _approvalService
                .HrDecisionAsync(id, hrId, approve: true, comments);

            TempData["AlertSeverity"] = success ? "success" : "danger";
            TempData["AlertMessage"] = message;

            return RedirectToAction(nameof(Queue));
        }

        // ── POST /Hr/Reject/{id} ──────────────────────────────────────────────
        //
        // Terminal rejection at HR stage. No balance changes are made.

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? comments)
        {
            int hrId = CurrentEmployeeId();

            var (success, message) = await _approvalService
                .HrDecisionAsync(id, hrId, approve: false, comments);

            TempData["AlertSeverity"] = success ? "success" : "danger";
            TempData["AlertMessage"] = message;

            return RedirectToAction(nameof(Queue));
        }
    }
}
