// =============================================================================
// Employee_Leave_Portal — LeaveController
// File: Controllers/LeaveController.cs
// =============================================================================
//
// Actions:
//   GET  /Leave/Dashboard          — employee's own leave history + balance
//   GET  /Leave/Apply              — blank application form
//   POST /Leave/Apply              — submit a leave request
//   GET  /Leave/BalancePreview     — AJAX: returns JSON warning for live UI
//   GET  /Leave/Details/{id}       — read-only detail view of one request
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
    [Authorize]                          // all actions require login
    public class LeaveController : Controller
    {
        private readonly AppDbContext _db;
        private readonly ILeaveService _leaveService;

        public LeaveController(AppDbContext db, ILeaveService leaveService)
        {
            _db = db;
            _leaveService = leaveService;
        }

        // ── Helpers ───────────────────────────────────────────────────────────

        /// <summary>Resolves the logged-in employee's Id from claims.</summary>
        private int CurrentEmployeeId()
            => int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        // ── GET /Leave/Dashboard ──────────────────────────────────────────────

        public async Task<IActionResult> Dashboard()
        {
            int empId = CurrentEmployeeId();
            int year = DateTime.Today.Year;

            var balance = await _leaveService.GetOrCreateBalanceAsync(empId, year);

            var requests = await _db.LeaveRequests
                .Where(lr => lr.EmployeeId == empId)
                .OrderByDescending(lr => lr.LeaveDate)
                .Select(lr => new LeaveRequestRowVm
                {
                    Id = lr.Id,
                    LeaveType = lr.LeaveType.ToString(),
                    LeaveDate = lr.LeaveDate,
                    DateApplied = lr.DateApplied,
                    Hours = lr.Hours,
                    Status = lr.Status.ToString(),
                    IsLossOfPay = lr.IsLossOfPay,
                    Reason = lr.Reason
                })
                .ToListAsync();

            // Fire confetti flag: any request whose status just became Approved
            // and hasn't been acknowledged yet (we track this via TempData set
            // during the redirect after submission so it fires exactly once).
            bool showConfetti = TempData["NewlyApproved"] is bool b && b;

            var vm = new DashboardVm
            {
                EmployeeId = empId,
                PaidLeaveBalance = balance.PaidLeaveBalance,
                ShortLeaveUsedThisMonth = balance.ShortLeaveUsedThisMonth,
                Year = year,
                Requests = requests,
                ShowConfetti = showConfetti
            };

            return View(vm);
        }

        // ── GET /Leave/Apply ──────────────────────────────────────────────────

        public IActionResult Apply()
        {
            return View(new ApplyLeaveVm
            {
                LeaveDate = DateTime.Today   // default to today
            });
        }

        // ── POST /Leave/Apply ─────────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Apply(ApplyLeaveVm vm)
        {
            if (!ModelState.IsValid)
                return View(vm);

            int empId = CurrentEmployeeId();

            var result = await _leaveService.SubmitLeaveAsync(
                empId, vm.LeaveType, vm.LeaveDate, vm.Reason);

            if (!result.Success)
            {
                // Surface the service-layer error as a model error so the
                // Razor view can display it in the validation summary.
                ModelState.AddModelError(string.Empty, result.Message);
                return View(vm);
            }

            // Pass the severity + message to the next page via TempData
            TempData["AlertSeverity"] = result.Severity;
            TempData["AlertMessage"] = result.Message;

            return RedirectToAction(nameof(Dashboard));
        }

        // ── GET /Leave/BalancePreview — AJAX endpoint ─────────────────────────
        //
        // Called by the leave form's JavaScript whenever the user changes the
        // LeaveType dropdown or LeaveDate picker. Returns JSON so the frontend
        // can update the warning banner without a full page reload.
        //
        // Query params: leaveType (int, maps to LeaveType enum), leaveDate (string yyyy-MM-dd)
        //
        // Response shape:
        // {
        //   "severity":         "success" | "warning" | "danger",
        //   "message":          "...",
        //   "isLossOfPay":      true | false,
        //   "projectedBalance": 12.5,
        //   "isAllowed":        true | false
        // }

        [HttpGet]
        public async Task<IActionResult> BalancePreview(int leaveType, string leaveDate)
        {
            if (!Enum.IsDefined(typeof(LeaveType), leaveType))
                return BadRequest(new { error = "Invalid leave type." });

            if (!DateTime.TryParse(leaveDate, out DateTime parsedDate))
                return BadRequest(new { error = "Invalid date." });

            int empId = CurrentEmployeeId();

            var result = await _leaveService.PreviewLeaveAsync(
                empId, (LeaveType)leaveType, parsedDate);

            return Json(new
            {
                severity = result.Severity,
                message = result.Message,
                isLossOfPay = result.IsLossOfPay,
                projectedBalance = result.ProjectedBalance,
                isAllowed = result.IsAllowed
            });
        }

        // ── GET /Leave/Details/{id} ───────────────────────────────────────────

        public async Task<IActionResult> Details(int id)
        {
            int empId = CurrentEmployeeId();

            var request = await _db.LeaveRequests
                .Include(lr => lr.ApprovalLogs)
                    .ThenInclude(al => al.ActionBy)
                .FirstOrDefaultAsync(lr => lr.Id == id && lr.EmployeeId == empId);

            if (request is null)
                return NotFound();

            // If this is the first time the employee opens a newly-approved
            // request, set the confetti flag for the dashboard redirect.
            if (request.Status == LeaveStatus.Approved)
                TempData["NewlyApproved"] = true;

            var vm = new LeaveDetailsVm
            {
                Id = request.Id,
                LeaveType = request.LeaveType.ToString(),
                LeaveDate = request.LeaveDate,
                DateApplied = request.DateApplied,
                Hours = request.Hours,
                Reason = request.Reason,
                Status = request.Status.ToString(),
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
    }
}
