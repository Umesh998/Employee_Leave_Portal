// =============================================================================
// Employee_Leave_Portal — HodController
// File: Controllers/HodController.cs
// =============================================================================
//
// Actions:
//   GET  /Hod/Queue              — pending requests for the HOD's department
//   GET  /Hod/Review/{id}        — detail view of one request before decision
//   POST /Hod/Approve/{id}       — approve → transitions to Pending_HR
//   POST /Hod/Reject/{id}        — reject  → terminal Rejected status
//
// Access: Role = HOD only (enforced via [Authorize(Roles = "HOD")])
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
    [Authorize(Roles = "HOD")]
    public class HodController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IApprovalService _approvalService;

        public HodController(AppDbContext db, IApprovalService approvalService)
        {
            _db = db;
            _approvalService = approvalService;
        }

        // ── Helper ────────────────────────────────────────────────────────────

        private int CurrentEmployeeId()
            => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Hod/Queue ────────────────────────────────────────────────────
        //
        // Shows only requests where:
        //   • Status == Pending_HOD
        //   • The requesting employee belongs to the HOD's department

        public async Task<IActionResult> Queue()
        {
            int hodId = CurrentEmployeeId();

            // Resolve the department this HOD manages
            var department = await _db.Departments
                .FirstOrDefaultAsync(d => d.HOD_EmployeeId == hodId);

            if (department is null)
            {
                // HOD has no department assigned — show empty queue with a notice
                return View(new HodQueueVm
                {
                    DepartmentName = "Unassigned",
                    Requests = new()
                });
            }

            var requests = await _db.LeaveRequests
                .Include(lr => lr.Employee)
                .Where(lr =>
                    lr.Status == LeaveStatus.Pending_HOD &&
                    lr.Employee!.DepartmentId == department.Id)
                .OrderBy(lr => lr.DateApplied)
                .Select(lr => new HodQueueRowVm
                {
                    Id = lr.Id,
                    EmployeeName = lr.Employee!.FullName,
                    EmployeeEmail = lr.Employee.Email,
                    LeaveType = lr.LeaveType.ToString(),
                    LeaveDate = lr.LeaveDate,
                    DateApplied = lr.DateApplied,
                    Hours = lr.Hours,
                    Reason = lr.Reason,
                    IsLossOfPay = lr.IsLossOfPay
                })
                .ToListAsync();

            var vm = new HodQueueVm
            {
                DepartmentName = department.DepartmentName,
                Requests = requests
            };

            return View(vm);
        }

        // ── GET /Hod/Review/{id} ──────────────────────────────────────────────
        //
        // Loads the full detail of a single pending request so the HOD can
        // read the reason and any prior context before deciding.

        public async Task<IActionResult> Review(int id)
        {
            int hodId = CurrentEmployeeId();

            var department = await _db.Departments
                .FirstOrDefaultAsync(d => d.HOD_EmployeeId == hodId);

            if (department is null)
                return Forbid();

            var request = await _db.LeaveRequests
                .Include(lr => lr.Employee)
                .Include(lr => lr.ApprovalLogs)
                    .ThenInclude(al => al.ActionBy)
                .FirstOrDefaultAsync(lr =>
                    lr.Id == id &&
                    lr.Status == LeaveStatus.Pending_HOD &&
                    lr.Employee!.DepartmentId == department.Id);

            if (request is null)
                return NotFound();

            var vm = new HodReviewVm
            {
                Id = request.Id,
                EmployeeName = request.Employee!.FullName,
                EmployeeEmail = request.Employee.Email,
                LeaveType = request.LeaveType.ToString(),
                LeaveDate = request.LeaveDate,
                DateApplied = request.DateApplied,
                Hours = request.Hours,
                Reason = request.Reason,
                IsLossOfPay = request.IsLossOfPay,
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

        // ── POST /Hod/Approve/{id} ────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Approve(int id, string? comments)
        {
            int hodId = CurrentEmployeeId();

            var (success, message) = await _approvalService
                .HodDecisionAsync(id, hodId, approve: true, comments);

            TempData["AlertSeverity"] = success ? "success" : "danger";
            TempData["AlertMessage"] = message;

            return RedirectToAction(nameof(Queue));
        }

        // ── POST /Hod/Reject/{id} ─────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Reject(int id, string? comments)
        {
            int hodId = CurrentEmployeeId();

            var (success, message) = await _approvalService
                .HodDecisionAsync(id, hodId, approve: false, comments);

            TempData["AlertSeverity"] = success ? "success" : "danger";
            TempData["AlertMessage"] = message;

            return RedirectToAction(nameof(Queue));
        }
    }
}
