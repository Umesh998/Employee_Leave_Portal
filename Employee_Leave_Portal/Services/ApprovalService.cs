// =============================================================================
// Employee_Leave_Portal — ApprovalService (with email notifications)
// File: Services/ApprovalService.cs
// =============================================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Services
{
    public interface IApprovalService
    {
        Task<(bool success, string message)> HodDecisionAsync(
            int leaveRequestId, int hodEmployeeId, bool approve, string? comments);

        Task<(bool success, string message)> HrDecisionAsync(
            int leaveRequestId, int hrEmployeeId, bool approve, string? comments);
    }

    public class ApprovalService : IApprovalService
    {
        private readonly AppDbContext _db;
        private readonly ILeaveService _leaveService;
        private readonly IEmailService _emailService;

        public ApprovalService(AppDbContext db, ILeaveService leaveService, IEmailService emailService)
        {
            _db = db;
            _leaveService = leaveService;
            _emailService = emailService;
        }

        // ── HOD Decision ──────────────────────────────────────────────────────

        public async Task<(bool success, string message)> HodDecisionAsync(
            int leaveRequestId, int hodEmployeeId, bool approve, string? comments)
        {
            var request = await _db.LeaveRequests
                .Include(lr => lr.Employee)
                .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId);

            if (request is null)
                return (false, "Leave request not found.");

            if (request.Status != LeaveStatus.Pending_HOD)
                return (false, "This request is not awaiting HOD approval.");

            var hod = await _db.Employees.FindAsync(hodEmployeeId);
            if (hod is null || hod.Role != EmployeeRole.HOD)
                return (false, "Actor is not a recognised HOD.");

            var dept = await _db.Departments.FindAsync(request.Employee!.DepartmentId);
            if (dept?.HOD_EmployeeId != hodEmployeeId)
                return (false, "You are not the HOD of this employee's department.");

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                request.Status = approve ? LeaveStatus.Pending_HR : LeaveStatus.Rejected;

                _db.ApprovalLogs.Add(new ApprovalLog
                {
                    LeaveRequestId = leaveRequestId,
                    ActionBy_EmployeeId = hodEmployeeId,
                    Action = approve ? "Approved" : "Rejected",
                    Comments = comments,
                    Timestamp = DateTime.Now
                });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            // ── Emails after commit ───────────────────────────────────────────
            try
            {
                if (approve)
                {
                    var hrList = await _db.Employees
                        .Where(e => e.Role == EmployeeRole.HR && e.IsActive)
                        .ToListAsync();

                    foreach (var hr in hrList)
                    {
                        var (subject, body) = EmailTemplates.LeaveApprovedByHodToHr(
                            hr.FullName,
                            request.Employee!.FullName,
                            request.Employee!.EmployeeCode,
                            request.LeaveType,
                            request.LeaveDate,
                            request.Hours,
                            comments);

                        await _emailService.SendAsync(hr.Email, hr.FullName, subject, body);
                    }
                }
                else
                {
                    var (subject, body) = EmailTemplates.LeaveRejectedToEmployee(
                        request.Employee!.FullName,
                        request.Employee!.EmployeeCode,
                        request.LeaveType,
                        request.LeaveDate,
                        hod.FullName,
                        comments);

                    await _emailService.SendAsync(
                        request.Employee!.Email, request.Employee.FullName, subject, body);
                }
            }
            catch { /* email failure never rolls back approval */ }

            return approve
                ? (true, "Request forwarded to HR for final approval.")
                : (true, "Request rejected. The employee has been notified.");
        }

        // ── HR Decision ───────────────────────────────────────────────────────

        public async Task<(bool success, string message)> HrDecisionAsync(
            int leaveRequestId, int hrEmployeeId, bool approve, string? comments)
        {
            var request = await _db.LeaveRequests
                .Include(lr => lr.Employee)
                .FirstOrDefaultAsync(lr => lr.Id == leaveRequestId);

            if (request is null)
                return (false, "Leave request not found.");

            if (request.Status != LeaveStatus.Pending_HR)
                return (false, "This request is not awaiting HR approval.");

            var hr = await _db.Employees.FindAsync(hrEmployeeId);
            if (hr is null || hr.Role != EmployeeRole.HR)
                return (false, "Actor is not a recognised HR officer.");

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                if (approve)
                {
                    request.Status = LeaveStatus.Approved;
                    await FinaliseApprovalAsync(request);
                }
                else
                {
                    request.Status = LeaveStatus.Rejected;
                }

                _db.ApprovalLogs.Add(new ApprovalLog
                {
                    LeaveRequestId = leaveRequestId,
                    ActionBy_EmployeeId = hrEmployeeId,
                    Action = approve ? "Approved" : "Rejected",
                    Comments = comments,
                    Timestamp = DateTime.Now
                });

                await _db.SaveChangesAsync();
                await tx.CommitAsync();
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }

            // ── Emails after commit ───────────────────────────────────────────
            try
            {
                if (approve)
                {
                    var (subject, body) = EmailTemplates.LeaveApprovedToEmployee(
                        request.Employee!.FullName,
                        request.Employee!.EmployeeCode,
                        request.LeaveType,
                        request.LeaveDate,
                        request.Hours,
                        request.IsLossOfPay);

                    await _emailService.SendAsync(
                        request.Employee!.Email, request.Employee.FullName, subject, body);
                }
                else
                {
                    var (subject, body) = EmailTemplates.LeaveRejectedToEmployee(
                        request.Employee!.FullName,
                        request.Employee!.EmployeeCode,
                        request.LeaveType,
                        request.LeaveDate,
                        hr.FullName,
                        comments);

                    await _emailService.SendAsync(
                        request.Employee!.Email, request.Employee.FullName, subject, body);
                }
            }
            catch { /* email failure never rolls back approval */ }

            return approve
                ? (true, "Leave approved. Balance updated and employee notified.")
                : (true, "Leave rejected by HR. Employee notified.");
        }

        // ── Balance finalisation ──────────────────────────────────────────────

        private async Task FinaliseApprovalAsync(LeaveRequest request)
        {
            var balance = await _leaveService.GetOrCreateBalanceAsync(
                request.EmployeeId, request.LeaveDate.Year);

            switch (request.LeaveType)
            {
                case LeaveType.ShortLeave:
                    if (!request.IsLossOfPay && balance.PaidLeaveBalance >= 0.5m)
                    {
                        balance.PaidLeaveBalance -= 0.5m;
                        if (balance.PaidLeaveBalance < 0m) balance.PaidLeaveBalance = 0m;
                    }
                    break;

                case LeaveType.HalfDay:
                    if (!request.IsLossOfPay)
                    {
                        balance.PaidLeaveBalance -= 0.5m;
                        if (balance.PaidLeaveBalance < 0m)
                        {
                            balance.PaidLeaveBalance = 0m;
                            request.IsLossOfPay = true;
                        }
                    }
                    break;

                case LeaveType.FullLeave:
                    if (!request.IsLossOfPay)
                    {
                        if (balance.PaidLeaveBalance >= 1.0m)
                            balance.PaidLeaveBalance -= 1.0m;
                        else if (balance.PaidLeaveBalance > 0m)
                        {
                            balance.PaidLeaveBalance = 0m;
                            request.IsLossOfPay = true;
                        }
                    }
                    break;
            }

            _db.LeaveBalances.Update(balance);
        }
    }
}
