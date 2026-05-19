// =============================================================================
// Employee_Leave_Portal — Leave Rules Engine (Service Layer)
// File: Services/LeaveService.cs
// =============================================================================

using System;
using System.Threading.Tasks;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Services
{
    public class LeaveEvaluationResult
    {
        public bool IsAllowed { get; init; }
        public string Severity { get; init; } = "success";
        public string Message { get; init; } = string.Empty;
        public bool IsLossOfPay { get; init; }
        public decimal ProjectedBalance { get; init; }
    }

    public class SubmitLeaveResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string Severity { get; init; } = "success";
        public int? LeaveRequestId { get; init; }
    }

    public interface ILeaveService
    {
        Task<LeaveEvaluationResult> PreviewLeaveAsync(int employeeId, LeaveType leaveType, DateTime leaveDate);
        Task<SubmitLeaveResult> SubmitLeaveAsync(int employeeId, LeaveType leaveType, DateTime leaveDate, string? reason);
        Task<LeaveBalance> GetOrCreateBalanceAsync(int employeeId, int year);
    }

    public class LeaveService : ILeaveService
    {
        private readonly AppDbContext _db;
        private readonly IEmailService _emailService;

        private static readonly System.Collections.Generic.Dictionary<LeaveType, int> HoursMap = new()
        {
            [LeaveType.ShortLeave] = 2,
            [LeaveType.HalfDay] = 4,
            [LeaveType.FullLeave] = 8
        };

        public LeaveService(AppDbContext db, IEmailService emailService)
        {
            _db = db;
            _emailService = emailService;
        }

        // ── Public: balance query ─────────────────────────────────────────────

        public async Task<LeaveBalance> GetOrCreateBalanceAsync(int employeeId, int year)
        {
            var balance = await _db.LeaveBalances
                .FirstOrDefaultAsync(lb => lb.EmployeeId == employeeId && lb.Year == year);

            if (balance is null)
            {
                balance = new LeaveBalance
                {
                    EmployeeId = employeeId,
                    Year = year,
                    PaidLeaveBalance = 18.0m,
                    ShortLeaveUsedThisMonth = 0,
                    LastResetMonth = DateTime.Now.Month,
                    LastResetYear = DateTime.Now.Year
                };
                _db.LeaveBalances.Add(balance);
                await _db.SaveChangesAsync();
            }

            return balance;
        }

        // ── Public: AJAX preview (no DB writes) ───────────────────────────────

        public async Task<LeaveEvaluationResult> PreviewLeaveAsync(
            int employeeId, LeaveType leaveType, DateTime leaveDate)
        {
            var deadlineResult = CheckSubmissionDeadline(leaveDate);
            if (deadlineResult is not null) return deadlineResult;

            var balance = await GetOrCreateBalanceAsync(employeeId, leaveDate.Year);
            EnsureMonthlyReset(balance, leaveDate);

            return EvaluateRules(leaveType, balance, dryRun: true);
        }

        // ── Public: submission (writes to DB) ─────────────────────────────────

        public async Task<SubmitLeaveResult> SubmitLeaveAsync(
            int employeeId, LeaveType leaveType, DateTime leaveDate, string? reason)
        {
            // 1. Deadline check
            var deadlineResult = CheckSubmissionDeadline(leaveDate);
            if (deadlineResult is not null)
            {
                return new SubmitLeaveResult
                {
                    Success = false,
                    Severity = deadlineResult.Severity,
                    Message = deadlineResult.Message
                };
            }

            // 2. Duplicate check
            bool duplicate = await _db.LeaveRequests.AnyAsync(lr =>
                lr.EmployeeId == employeeId &&
                lr.LeaveDate == leaveDate.Date &&
                lr.Status != LeaveStatus.Rejected);

            if (duplicate)
            {
                return new SubmitLeaveResult
                {
                    Success = false,
                    Severity = "danger",
                    Message = "A leave request for this date already exists."
                };
            }

            // 3. Load & lazy-reset balance
            var balance = await GetOrCreateBalanceAsync(employeeId, leaveDate.Year);
            EnsureMonthlyReset(balance, leaveDate);

            // 4. Rules engine evaluation
            var evaluation = EvaluateRules(leaveType, balance, dryRun: false);

            if (!evaluation.IsAllowed)
            {
                return new SubmitLeaveResult
                {
                    Success = false,
                    Severity = evaluation.Severity,
                    Message = evaluation.Message
                };
            }

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // 5. Save balance counter change
                _db.LeaveBalances.Update(balance);

                // 6. Create leave request
                var request = new LeaveRequest
                {
                    EmployeeId = employeeId,
                    LeaveType = leaveType,
                    LeaveDate = leaveDate.Date,
                    DateApplied = DateTime.Now,
                    Hours = HoursMap[leaveType],
                    Reason = reason,
                    Status = LeaveStatus.Pending_HOD,
                    IsLossOfPay = evaluation.IsLossOfPay
                };

                _db.LeaveRequests.Add(request);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

                // 7. Notify HOD of new leave request
                try
                {
                    var emp = await _db.Employees
                        .Include(e => e.Department)
                        .FirstOrDefaultAsync(e => e.Id == employeeId);

                    if (emp?.Department != null)
                    {
                        var hodEmployee = await _db.Employees
                            .FirstOrDefaultAsync(e => e.Id == emp.Department.HOD_EmployeeId);

                        if (hodEmployee != null)
                        {
                            var (hodSubject, hodBody) = EmailTemplates.LeaveSubmittedToHod(
                                hodEmployee.FullName,
                                emp.FullName,
                                emp.EmployeeCode,
                                leaveType,
                                leaveDate,
                                HoursMap[leaveType],
                                reason);

                            await _emailService.SendAsync(
                                hodEmployee.Email, hodEmployee.FullName, hodSubject, hodBody);
                        }
                    }
                }
                catch { /* email failure never blocks submission */ }

                // 8. Send email if this was the 3rd short leave (limit reached)
                if (leaveType == LeaveType.ShortLeave && balance.ShortLeaveUsedThisMonth == 3)
                {
                    try
                    {
                        var emp = await _db.Employees.FindAsync(employeeId);
                        if (emp != null)
                        {
                            string subject = "Short Leave Limit Reached — Action Required";
                            string body = $@"
                                <div style='font-family:Segoe UI,sans-serif;max-width:500px;margin:auto'>
                                    <div style='background:#F59E0B;padding:24px;border-radius:12px 12px 0 0'>
                                        <h2 style='color:#fff;margin:0'>Leave Limit Notice</h2>
                                        <p style='color:#FEF3C7;margin:4px 0 0'>Employee Leave Portal</p>
                                    </div>
                                    <div style='background:#fff;padding:32px;
                                                border:1px solid #E2E8F0;
                                                border-radius:0 0 12px 12px'>
                                        <p style='color:#334155'>Hi <strong>{emp.FullName}</strong>,</p>
                                        <p style='color:#334155'>
                                            You have used all <strong>3 free short leaves</strong>
                                            for this month.
                                        </p>
                                        <div style='background:#FEF3C7;padding:16px;
                                                    border-radius:8px;color:#92400E;margin:16px 0'>
                                            ⚠ From now until the end of the month, you can only apply for
                                            <strong>Half Day</strong> or <strong>Full Leave</strong>.
                                            Short leave applications will be blocked.
                                        </div>
                                        <p style='color:#64748B;font-size:.85rem'>
                                            Your short leave counter resets automatically
                                            on the 1st of next month.
                                        </p>
                                    </div>
                                </div>";

                            await _emailService.SendAsync(emp.Email, emp.FullName, subject, body);
                        }
                    }
                    catch { /* email failure never blocks submission */ }
                }

                return new SubmitLeaveResult
                {
                    Success = true,
                    Severity = evaluation.Severity,
                    Message = evaluation.Message,
                    LeaveRequestId = request.Id
                };
            }
            catch
            {
                await tx.RollbackAsync();
                throw;
            }
        }

        // ── Private: deadline guard ───────────────────────────────────────────

        private static LeaveEvaluationResult? CheckSubmissionDeadline(DateTime leaveDate)
        {
            if (leaveDate.Date < DateTime.Today.AddDays(-2))
            {
                return new LeaveEvaluationResult
                {
                    IsAllowed = false,
                    Severity = "danger",
                    Message = $"Submission deadline exceeded. Leave requests for past dates must be "
                              + $"submitted within 2 days. The deadline for {leaveDate:dd MMM yyyy} "
                              + $"was {leaveDate.Date.AddDays(2):dd MMM yyyy}."
                };
            }
            return null;
        }

        // ── Private: monthly short-leave counter reset (lazy strategy) ────────

        private static void EnsureMonthlyReset(LeaveBalance balance, DateTime referenceDate)
        {
            bool monthRolled =
                referenceDate.Year > balance.LastResetYear ||
                (referenceDate.Year == balance.LastResetYear &&
                 referenceDate.Month > balance.LastResetMonth);

            if (monthRolled)
            {
                balance.ShortLeaveUsedThisMonth = 0;
                balance.LastResetMonth = referenceDate.Month;
                balance.LastResetYear = referenceDate.Year;
            }
        }

        // ── Private: core rules engine ────────────────────────────────────────

        private static LeaveEvaluationResult EvaluateRules(
            LeaveType leaveType, LeaveBalance balance, bool dryRun)
        {
            return leaveType switch
            {
                LeaveType.ShortLeave => EvaluateShortLeave(balance, dryRun),
                LeaveType.HalfDay => EvaluateHalfDay(balance),
                LeaveType.FullLeave => EvaluateFullLeave(balance),
                _ => throw new ArgumentOutOfRangeException(nameof(leaveType))
            };
        }

        // ── Short Leave ───────────────────────────────────────────────────────

        //private static LeaveEvaluationResult EvaluateShortLeave(LeaveBalance balance, bool dryRun)
        //{
        //    const int freeMonthlyLimit = 3;
        //    const decimal overflowDeduction = 0.5m;

        //    if (balance.ShortLeaveUsedThisMonth < freeMonthlyLimit)
        //    {
        //        // Within free tier
        //        if (!dryRun) balance.ShortLeaveUsedThisMonth++;

        //        int remaining = freeMonthlyLimit - balance.ShortLeaveUsedThisMonth;

        //        return new LeaveEvaluationResult
        //        {
        //            IsAllowed = true,
        //            Severity = "success",
        //            IsLossOfPay = false,
        //            ProjectedBalance = balance.PaidLeaveBalance,
        //            Message = remaining > 0
        //                ? $"Short leave granted. You have {remaining} free short leave(s) remaining this month."
        //                : "Short leave granted. This was your last free short leave for this month. "
        //                + "Any further short leaves will be deducted from your Paid Leave balance."
        //        };
        //    }

        //    // Overflow — count already at 3 or more
        //    if (balance.PaidLeaveBalance >= overflowDeduction)
        //    {
        //        decimal projected = balance.PaidLeaveBalance - overflowDeduction;
        //        return new LeaveEvaluationResult
        //        {
        //            IsAllowed = true,
        //            Severity = "warning",
        //            IsLossOfPay = false,
        //            ProjectedBalance = projected,
        //            Message = $"You have used all 3 free short leaves this month. "
        //                             + $"0.5 day will be deducted from your Paid Leave balance upon approval. "
        //                             + $"Remaining balance after approval: {projected:F1} day(s)."
        //        };
        //    }

        //    // Overflow AND balance exhausted → Loss of Pay
        //    return new LeaveEvaluationResult
        //    {
        //        IsAllowed = true,
        //        Severity = "danger",
        //        IsLossOfPay = true,
        //        ProjectedBalance = 0m,
        //        Message = "⚠ Your Paid Leave balance is exhausted. This short leave will be "
        //                         + "marked as Loss of Pay and a half-day salary deduction will be applied."
        //    };
        //}

        private static LeaveEvaluationResult EvaluateShortLeave(LeaveBalance balance, bool dryRun)
        {
            const int freeMonthlyLimit = 3;

            if (balance.ShortLeaveUsedThisMonth >= freeMonthlyLimit)
            {
                return new LeaveEvaluationResult
                {
                    IsAllowed = false,
                    Severity = "danger",
                    IsLossOfPay = false,
                    ProjectedBalance = balance.PaidLeaveBalance,
                    Message = "You have used all 3 short leaves for this month. "
                                     + "Please apply for a Half Day or Full Leave instead."
                };
            }

            if (!dryRun) balance.ShortLeaveUsedThisMonth++;

            int remaining = freeMonthlyLimit - balance.ShortLeaveUsedThisMonth;

            return new LeaveEvaluationResult
            {
                IsAllowed = true,
                Severity = remaining == 0 ? "warning" : "success",
                IsLossOfPay = false,
                ProjectedBalance = balance.PaidLeaveBalance,
                Message = remaining > 0
                    ? $"Short leave granted. You have {remaining} short leave(s) remaining this month."
                    : "Short leave granted. This was your last free short leave for this month. "
                    + "From next short leave you will need to apply for Half Day or Full Leave."
            };
        }

        // ── Half Day ──────────────────────────────────────────────────────────

        private static LeaveEvaluationResult EvaluateHalfDay(LeaveBalance balance)
        {
            const decimal deduction = 0.5m;

            if (balance.PaidLeaveBalance >= deduction)
            {
                decimal projected = balance.PaidLeaveBalance - deduction;
                return new LeaveEvaluationResult
                {
                    IsAllowed = true,
                    Severity = projected <= 3m ? "warning" : "success",
                    IsLossOfPay = false,
                    ProjectedBalance = projected,
                    Message = projected <= 3m
                        ? $"Half day approved. Remaining balance after approval: {projected:F1} day(s). "
                        + "Your paid leave balance is running low."
                        : $"Half day approved. Remaining balance after approval: {projected:F1} day(s)."
                };
            }

            return new LeaveEvaluationResult
            {
                IsAllowed = true,
                Severity = "danger",
                IsLossOfPay = true,
                ProjectedBalance = 0m,
                Message = "⚠ Insufficient Paid Leave balance for a half day. "
                                 + "This request will be flagged as Loss of Pay — "
                                 + "a half-day salary deduction will be applied upon approval."
            };
        }

        // ── Full Leave ────────────────────────────────────────────────────────

        private static LeaveEvaluationResult EvaluateFullLeave(LeaveBalance balance)
        {
            const decimal deduction = 1.0m;

            if (balance.PaidLeaveBalance >= deduction)
            {
                decimal projected = balance.PaidLeaveBalance - deduction;
                return new LeaveEvaluationResult
                {
                    IsAllowed = true,
                    Severity = projected <= 3m ? "warning" : "success",
                    IsLossOfPay = false,
                    ProjectedBalance = projected,
                    Message = projected <= 3m
                        ? $"Full leave approved. Remaining balance after approval: {projected:F1} day(s). "
                        + "Your paid leave balance is running low."
                        : $"Full leave approved. Remaining balance after approval: {projected:F1} day(s)."
                };
            }

            if (balance.PaidLeaveBalance > 0m)
            {
                return new LeaveEvaluationResult
                {
                    IsAllowed = true,
                    Severity = "warning",
                    IsLossOfPay = false,
                    ProjectedBalance = 0m,
                    Message = $"⚠ You only have {balance.PaidLeaveBalance:F1} day(s) of Paid Leave remaining. "
                                     + "This will be applied toward today's full leave; "
                                     + "the remaining portion will be Loss of Pay."
                };
            }

            return new LeaveEvaluationResult
            {
                IsAllowed = true,
                Severity = "danger",
                IsLossOfPay = true,
                ProjectedBalance = 0m,
                Message = "⚠ Your Paid Leave balance is completely exhausted. "
                                 + "This full-day leave will be flagged as Loss of Pay — "
                                 + "salary for this day will be deducted upon approval."
            };
        }
    }
}
