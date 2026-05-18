// =============================================================================
// Employee_Leave_Portal — Leave Rules Engine (Service Layer)
// File: Services/LeaveService.cs
// =============================================================================
//
// Responsibilities:
//   1. Validate the 2-day submission deadline.
//   2. Run the leave-type deduction & overflow rules engine.
//   3. Persist a new LeaveRequest (status = Pending_HOD).
//   4. Expose a read-only balance query for the AJAX balance-check endpoint.
//   5. Expose a balance preview method (no writes) used by the form's
//      live-warning AJAX call.
//
// =============================================================================

using System;
using System.Threading.Tasks;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Services
{
    // ─────────────────────────────────────────────────────────────────────────
    // Result / DTO types
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Describes the outcome of a rules-engine evaluation — used both for the
    /// live AJAX preview and for the actual submission attempt.
    /// </summary>
    public class LeaveEvaluationResult
    {
        public bool IsAllowed { get; init; }

        /// <summary>UI severity: "success" | "warning" | "danger"</summary>
        public string Severity { get; init; } = "success";

        /// <summary>Human-readable message shown as the inline banner.</summary>
        public string Message { get; init; } = string.Empty;

        public bool IsLossOfPay { get; init; }

        /// <summary>
        /// Projected paid-leave balance after this request (for preview only;
        /// never persisted until HR approves).
        /// </summary>
        public decimal ProjectedBalance { get; init; }
    }

    public class SubmitLeaveResult
    {
        public bool Success { get; init; }
        public string Message { get; init; } = string.Empty;
        public string Severity { get; init; } = "success";
        public int? LeaveRequestId { get; init; }
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Service
    // ─────────────────────────────────────────────────────────────────────────

    public interface ILeaveService
    {
        Task<LeaveEvaluationResult> PreviewLeaveAsync(int employeeId, LeaveType leaveType, DateTime leaveDate);
        Task<SubmitLeaveResult> SubmitLeaveAsync(int employeeId, LeaveType leaveType, DateTime leaveDate, string? reason);
        Task<LeaveBalance> GetOrCreateBalanceAsync(int employeeId, int year);
    }

    public class LeaveService : ILeaveService
    {
        private readonly AppDbContext _db;

        // Hours are fixed by business rule — never trusted from the form POST
        private static readonly System.Collections.Generic.Dictionary<LeaveType, int> HoursMap = new()
        {
            [LeaveType.ShortLeave] = 2,
            [LeaveType.HalfDay] = 4,
            [LeaveType.FullLeave] = 8
        };

        public LeaveService(AppDbContext db) => _db = db;

        // ── Public: balance query ─────────────────────────────────────────────

        /// <summary>
        /// Returns the LeaveBalance for the given employee and year, creating and
        /// persisting a fresh one (18 days) if it does not yet exist.
        /// </summary>
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
                    LastResetMonth = DateTime.UtcNow.Month,
                    LastResetYear = DateTime.UtcNow.Year
                };
                _db.LeaveBalances.Add(balance);
                await _db.SaveChangesAsync();
            }

            return balance;
        }

        // ── Public: AJAX preview (no DB writes) ───────────────────────────────

        /// <summary>
        /// Evaluates the rules engine and returns a preview result without
        /// persisting anything. Consumed by the leave form's AJAX endpoint.
        /// </summary>
        public async Task<LeaveEvaluationResult> PreviewLeaveAsync(
            int employeeId, LeaveType leaveType, DateTime leaveDate)
        {
            // 1. Deadline check
            var deadlineResult = CheckSubmissionDeadline(leaveDate);
            if (deadlineResult is not null) return deadlineResult;

            // 2. Load balance (lazy-reset short-leave counter if month rolled)
            var balance = await GetOrCreateBalanceAsync(employeeId, leaveDate.Year);
            EnsureMonthlyReset(balance, leaveDate);   // no save — preview only

            // 3. Run rules (read-only snapshot — no balance mutation)
            return EvaluateRules(leaveType, balance, dryRun: true);
        }

        // ── Public: submission (writes to DB) ────────────────────────────────

        /// <summary>
        /// Validates, evaluates the rules engine, and — if permitted — inserts a
        /// new LeaveRequest in Pending_HOD status. Balance mutation happens only
        /// on HR approval (see ApprovalService).
        /// </summary>
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

            // 2. Duplicate check — prevent double-submission for the same date
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

            // 4. Rules engine evaluation (dry-run = false to mutate counters)
            var evaluation = EvaluateRules(leaveType, balance, dryRun: false);

            // NOTE: balance.PaidLeaveBalance is NOT decremented here.
            // Decrement happens inside ApprovalService.FinaliseApprovalAsync
            // when HR approves. The ShortLeaveUsedThisMonth counter IS incremented
            // immediately (within the free-tier) so concurrent submissions are
            // correctly bounded.

            await using var tx = await _db.Database.BeginTransactionAsync();
            try
            {
                // 5. Save balance counter change (ShortLeaveUsedThisMonth)
                _db.LeaveBalances.Update(balance);

                // 6. Create the leave request
                var request = new LeaveRequest
                {
                    EmployeeId = employeeId,
                    LeaveType = leaveType,
                    LeaveDate = leaveDate.Date,
                    DateApplied = DateTime.UtcNow,
                    Hours = HoursMap[leaveType],
                    Reason = reason,
                    Status = LeaveStatus.Pending_HOD,
                    IsLossOfPay = evaluation.IsLossOfPay
                };

                _db.LeaveRequests.Add(request);
                await _db.SaveChangesAsync();
                await tx.CommitAsync();

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
            if (DateTime.Today > leaveDate.Date.AddDays(2))
            {
                return new LeaveEvaluationResult
                {
                    IsAllowed = false,
                    Severity = "danger",
                    Message = $"Submission deadline exceeded. Leave requests must be submitted "
                              + $"within 2 days of the leave date ({leaveDate:dd MMM yyyy}). "
                              + $"The deadline was {leaveDate.Date.AddDays(2):dd MMM yyyy}."
                };
            }
            return null;
        }

        // ── Private: monthly short-leave counter reset (lazy strategy) ────────

        /// <summary>
        /// Resets ShortLeaveUsedThisMonth to 0 if the calendar month/year has
        /// rolled past the last recorded reset. Call before any reads of that
        /// counter. Caller is responsible for persisting the entity.
        /// </summary>
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

        /// <summary>
        /// Applies the deduction and overflow rules for the requested leave type.
        ///
        /// When <paramref name="dryRun"/> is true:
        ///   — balance fields are NOT mutated (safe for preview calls).
        ///
        /// When <paramref name="dryRun"/> is false:
        ///   — ShortLeaveUsedThisMonth is incremented (within free tier only).
        ///   — PaidLeaveBalance is NOT touched here; it is decremented by
        ///     ApprovalService.FinaliseApprovalAsync on HR approval.
        /// </summary>
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
        //
        //  Free tier: up to 3 per month — increment counter, no paid-leave touch.
        //  4th+, balance >= 0.5: warn user, flag 0.5 day will be deducted on approval.
        //  4th+, balance  < 0.5: allow, flag IsLossOfPay = true.

        private static LeaveEvaluationResult EvaluateShortLeave(LeaveBalance balance, bool dryRun)
        {
            const int freeMonthlyLimit = 3;
            const decimal overflowDeduction = 0.5m;

            if (balance.ShortLeaveUsedThisMonth < freeMonthlyLimit)
            {
                // Within free tier
                if (!dryRun) balance.ShortLeaveUsedThisMonth++;

                int remaining = freeMonthlyLimit - balance.ShortLeaveUsedThisMonth;

                return new LeaveEvaluationResult
                {
                    IsAllowed = true,
                    Severity = "success",
                    IsLossOfPay = false,
                    ProjectedBalance = balance.PaidLeaveBalance,
                    Message = remaining > 0
                        ? $"Short leave granted. You have {remaining} free short leave(s) remaining this month."
                        : "Short leave granted. This was your last free short leave for this month. "
                        + "Any further short leaves will be deducted from your Paid Leave balance."
                };
            }

            // Overflow — count already at 3 or more
            if (balance.PaidLeaveBalance >= overflowDeduction)
            {
                decimal projected = balance.PaidLeaveBalance - overflowDeduction;
                return new LeaveEvaluationResult
                {
                    IsAllowed = true,
                    Severity = "warning",
                    IsLossOfPay = false,
                    ProjectedBalance = projected,
                    Message = $"You have used all 3 free short leaves this month. "
                                     + $"0.5 day will be deducted from your Paid Leave balance upon approval. "
                                     + $"Remaining balance after approval: {projected:F1} day(s)."
                };
            }

            // Overflow AND balance exhausted → Loss of Pay
            return new LeaveEvaluationResult
            {
                IsAllowed = true,
                Severity = "danger",
                IsLossOfPay = true,
                ProjectedBalance = 0m,
                Message = "⚠ Your Paid Leave balance is exhausted. This short leave will be "
                                 + "marked as Loss of Pay and a half-day salary deduction will be applied."
            };
        }

        // ── Half Day ──────────────────────────────────────────────────────────
        //
        //  Deduct 0.5 days. If balance is insufficient → Loss of Pay.

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
        //
        //  Deduct 1.0 day. Partial balance (e.g. 0.5) → partial deduction allowed but warn.
        //  Zero balance → Loss of Pay.

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
                // Has a fractional balance (e.g. 0.5) — not enough for a full day
                return new LeaveEvaluationResult
                {
                    IsAllowed = true,
                    Severity = "warning",
                    IsLossOfPay = false,   // partially covered — HR decides on approval
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
