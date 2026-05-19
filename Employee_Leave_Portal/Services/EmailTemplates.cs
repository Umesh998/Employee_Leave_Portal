// =============================================================================
// Employee_Leave_Portal — EmailTemplates
// File: Services/EmailTemplates.cs
// =============================================================================

using System;
using Employee_Leave_Portal.Models;

namespace Employee_Leave_Portal.Services
{
    public static class EmailTemplates
    {
        private static string Wrap(string title, string body) => $@"
        <div style='font-family:Segoe UI,sans-serif;max-width:600px;margin:auto;border:1px solid #E2E8F0;border-radius:12px;overflow:hidden'>
            <div style='background:#2563EB;padding:24px 32px'>
                <h2 style='color:#fff;margin:0;font-size:1.2rem'>Employee Leave Portal</h2>
                <p style='color:#BFDBFE;margin:4px 0 0;font-size:.85rem'>{title}</p>
            </div>
            <div style='padding:32px;background:#fff'>
                {body}
            </div>
            <div style='background:#F8FAFC;padding:16px 32px;text-align:center;font-size:.78rem;color:#94A3B8'>
                This is an automated notification. Please do not reply to this email.
            </div>
        </div>";

        private static string Row(string label, string value) =>
            $"<tr><td style='padding:6px 0;color:#64748B;font-size:.85rem;width:140px'>{label}</td><td style='padding:6px 0;font-weight:600;font-size:.85rem;color:#1E293B'>{value}</td></tr>";

        private static string Table(string rows) =>
            $"<table style='width:100%;border-collapse:collapse;margin:16px 0'>{rows}</table>";

        private static string Button(string text, string color = "#2563EB") =>
            $"<a style='display:inline-block;margin-top:20px;padding:10px 24px;background:{color};color:#fff;border-radius:8px;text-decoration:none;font-weight:600;font-size:.88rem'>{text}</a>";

        // ── 1. HOD notified when employee submits ─────────────────────────────

        public static (string subject, string body) LeaveSubmittedToHod(
            string hodName, string employeeName, string employeeCode,
            LeaveType leaveType, DateTime leaveDate, int hours, string? reason)
        {
            string subject = $"New Leave Request from {employeeName} — Action Required";
            string body = Wrap("New Leave Request", $@"
                <p style='color:#334155'>Hi <strong>{hodName}</strong>,</p>
                <p style='color:#334155'>A leave request has been submitted and requires your approval.</p>
                {Table(
                    Row("Employee ID", employeeCode) +
                    Row("Employee", employeeName) +
                    Row("Leave Type", leaveType.ToString().Replace("Leave", " Leave")) +
                    Row("Leave Date", leaveDate.ToString("dd MMM yyyy")) +
                    Row("Hours", $"{hours} hrs") +
                    Row("Reason", string.IsNullOrWhiteSpace(reason) ? "—" : reason)
                )}
                <p style='color:#64748B;font-size:.85rem'>Please log in to the portal to approve or reject this request.</p>
                {Button("Open Portal")}
            ");
            return (subject, body);
        }

        // ── 2. HR notified when HOD approves ──────────────────────────────────

        public static (string subject, string body) LeaveApprovedByHodToHr(
            string hrName, string employeeName, string employeeCode,
            LeaveType leaveType, DateTime leaveDate, int hours, string? hodComments)
        {
            string subject = $"Leave Request for {employeeName} — HOD Approved, Awaiting HR";
            string body = Wrap("HOD Approved — Final HR Review Required", $@"
                <p style='color:#334155'>Hi <strong>{hrName}</strong>,</p>
                <p style='color:#334155'>The following leave request has been approved by the HOD and now requires your final review.</p>
                {Table(
                    Row("Employee ID", employeeCode) +
                    Row("Employee", employeeName) +
                    Row("Leave Type", leaveType.ToString().Replace("Leave", " Leave")) +
                    Row("Leave Date", leaveDate.ToString("dd MMM yyyy")) +
                    Row("Hours", $"{hours} hrs") +
                    Row("HOD Comment", string.IsNullOrWhiteSpace(hodComments) ? "—" : hodComments)
                )}
                <p style='color:#64748B;font-size:.85rem'>Please log in to the portal to give your final decision.</p>
                {Button("Open Portal")}
            ");
            return (subject, body);
        }

        // ── 3. Employee notified — Approved ───────────────────────────────────

        public static (string subject, string body) LeaveApprovedToEmployee(
            string employeeName, string employeeCode,
            LeaveType leaveType, DateTime leaveDate, int hours, bool isLossOfPay)
        {
            string lopWarning = isLossOfPay
                ? "<p style='background:#FEE2E2;color:#991B1B;padding:12px;border-radius:8px;font-size:.85rem'>⚠ This leave has been marked as <strong>Loss of Pay</strong>. A salary deduction will be applied.</p>"
                : "";

            string subject = $"Your Leave Request for {leaveDate:dd MMM yyyy} has been Approved";
            string body = Wrap("Leave Approved ✓", $@"
                <p style='color:#334155'>Hi <strong>{employeeName}</strong>,</p>
                <p style='color:#334155'>Great news! Your leave request has been <strong style='color:#065F46'>approved</strong>.</p>
                {Table(
                    Row("Employee ID", employeeCode) +
                    Row("Leave Type", leaveType.ToString().Replace("Leave", " Leave")) +
                    Row("Leave Date", leaveDate.ToString("dd MMM yyyy")) +
                    Row("Hours", $"{hours} hrs")
                )}
                {lopWarning}
                {Button("View Dashboard", "#10B981")}
            ");
            return (subject, body);
        }

        // ── 4. Employee notified — Rejected ───────────────────────────────────

        public static (string subject, string body) LeaveRejectedToEmployee(
            string employeeName, string employeeCode,
            LeaveType leaveType, DateTime leaveDate, string rejectedBy, string? comments)
        {
            string subject = $"Your Leave Request for {leaveDate:dd MMM yyyy} has been Rejected";
            string body = Wrap("Leave Rejected", $@"
                <p style='color:#334155'>Hi <strong>{employeeName}</strong>,</p>
                <p style='color:#334155'>Unfortunately your leave request has been <strong style='color:#991B1B'>rejected</strong>.</p>
                {Table(
                    Row("Employee ID", employeeCode) +
                    Row("Leave Type", leaveType.ToString().Replace("Leave", " Leave")) +
                    Row("Leave Date", leaveDate.ToString("dd MMM yyyy")) +
                    Row("Rejected By", rejectedBy) +
                    Row("Comments", string.IsNullOrWhiteSpace(comments) ? "—" : comments)
                )}
                <p style='color:#64748B;font-size:.85rem'>You may submit a new request or contact your manager for clarification.</p>
                {Button("View Dashboard", "#EF4444")}
            ");
            return (subject, body);
        }
    }
}
