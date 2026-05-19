// =============================================================================
// Employee_Leave_Portal — AccountController
// File: Controllers/AccountController.cs
// =============================================================================

// =============================================================================
// Employee_Leave_Portal — AccountController (with OTP for HOD & HR)
// File: Controllers/AccountController.cs
// =============================================================================

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
using Employee_Leave_Portal.Services;
using Employee_Leave_Portal.ViewModels;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Controllers
{
    public class AccountController : Controller
    {
        private readonly AppDbContext _db;
        private readonly IOtpService _otpService;
        private readonly IEmailService _emailService;

        public AccountController(AppDbContext db, IOtpService otpService, IEmailService emailService)
        {
            _db = db;
            _otpService = otpService;
            _emailService = emailService;
        }

        // ── GET /Account/Login ────────────────────────────────────────────────

        [HttpGet, AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Dashboard", "Leave");

            ViewData["ReturnUrl"] = returnUrl;
            return View(new LoginVm());
        }

        // ── POST /Account/Login ───────────────────────────────────────────────

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(vm);

            var employee = await _db.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e =>
                    e.Email == vm.Email.Trim().ToLowerInvariant() &&
                    e.IsActive);

            if (employee is null)
            {
                ModelState.AddModelError(string.Empty,
                    "No active account found with that email address.");
                return View(vm);
            }

            // HOD and HR → OTP flow
            if (employee.Role == EmployeeRole.HOD || employee.Role == EmployeeRole.HR)
            {
                string otp = _otpService.GenerateOtp(employee.Email);
                string subject = "Your Login OTP — Employee Leave Portal";
                string body = $@"
                    <div style='font-family:Segoe UI,sans-serif;max-width:500px;margin:auto'>
                        <div style='background:#2563EB;padding:24px;border-radius:12px 12px 0 0'>
                            <h2 style='color:#fff;margin:0'>Employee Leave Portal</h2>
                            <p style='color:#BFDBFE;margin:4px 0 0'>Login Verification</p>
                        </div>
                        <div style='background:#fff;padding:32px;border:1px solid #E2E8F0;border-radius:0 0 12px 12px'>
                            <p style='color:#334155'>Hi <strong>{employee.FullName}</strong>,</p>
                            <p style='color:#334155'>Your one-time login code is:</p>
                            <div style='text-align:center;margin:24px 0'>
                                <span style='font-size:2.5rem;font-weight:700;letter-spacing:.5rem;
                                             color:#2563EB;background:#EFF6FF;
                                             padding:16px 32px;border-radius:12px'>
                                    {otp}
                                </span>
                            </div>
                            <p style='color:#64748B;font-size:.85rem'>
                                This code expires in <strong>5 minutes</strong>.
                                Do not share it with anyone.
                            </p>
                        </div>
                    </div>";

                try
                {
                    await _emailService.SendAsync(employee.Email, employee.FullName, subject, body);
                }

                catch (Exception ex)
                {
                    var inner = ex.InnerException?.Message ?? "no inner exception";
                    ModelState.AddModelError(string.Empty,
                        $"SMTP Error: {ex.Message} | Inner: {inner}");
                    return View(vm);
                }

                TempData["OtpEmail"] = employee.Email;
                TempData["OtpRememberMe"] = vm.RememberMe;

                return RedirectToAction(nameof(VerifyOtp));
            }

            // Employee / Admin — direct login
            await SignInAsync(employee, vm.RememberMe);
            return RedirectBasedOnRole(employee.Role, returnUrl);
        }

        // ── GET /Account/VerifyOtp ────────────────────────────────────────────

        [HttpGet, AllowAnonymous]
        public IActionResult VerifyOtp()
        {
            if (TempData["OtpEmail"] is not string email)
                return RedirectToAction(nameof(Login));

            TempData.Keep("OtpEmail");
            TempData.Keep("OtpRememberMe");

            return View(new VerifyOtpVm { Email = email });
        }

        // ── POST /Account/VerifyOtp ───────────────────────────────────────────

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> VerifyOtp(VerifyOtpVm vm)
        {
            if (!ModelState.IsValid)
            {
                TempData.Keep("OtpEmail");
                TempData.Keep("OtpRememberMe");
                return View(vm);
            }

            bool rememberMe = TempData["OtpRememberMe"] is bool b && b;

            if (!_otpService.ValidateOtp(vm.Email, vm.Otp))
            {
                ModelState.AddModelError(nameof(vm.Otp),
                    "Invalid or expired OTP. Please try again.");
                TempData.Keep("OtpEmail");
                TempData.Keep("OtpRememberMe");
                return View(vm);
            }

            var employee = await _db.Employees
                .FirstOrDefaultAsync(e =>
                    e.Email == vm.Email.ToLowerInvariant() && e.IsActive);

            if (employee is null)
                return RedirectToAction(nameof(Login));

            await SignInAsync(employee, rememberMe);
            return RedirectBasedOnRole(employee.Role, null);
        }

        // ── POST /Account/ResendOtp ───────────────────────────────────────────

        [HttpPost, AllowAnonymous, ValidateAntiForgeryToken]
        public async Task<IActionResult> ResendOtp(string email)
        {
            var employee = await _db.Employees
                .FirstOrDefaultAsync(e =>
                    e.Email == email.ToLowerInvariant() && e.IsActive);

            if (employee is null)
                return RedirectToAction(nameof(Login));

            string otp = _otpService.GenerateOtp(employee.Email);
            string subject = "Your New Login OTP — Employee Leave Portal";
            string body = $@"
                <div style='font-family:Segoe UI,sans-serif;max-width:500px;margin:auto;text-align:center;padding:32px'>
                    <h2 style='color:#2563EB'>Employee Leave Portal</h2>
                    <p>Your new OTP is:</p>
                    <h1 style='letter-spacing:.5rem;color:#2563EB;background:#EFF6FF;
                               padding:16px 32px;border-radius:12px;display:inline-block'>
                        {otp}
                    </h1>
                    <p style='color:#64748B;font-size:.85rem'>Expires in 5 minutes.</p>
                </div>";

            try { await _emailService.SendAsync(employee.Email, employee.FullName, subject, body); }
            catch { /* silent */ }

            TempData["OtpEmail"] = employee.Email;
            TempData["OtpRememberMe"] = false;
            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = "A new OTP has been sent to your email.";

            return RedirectToAction(nameof(VerifyOtp));
        }

        // ── POST /Account/Logout ──────────────────────────────────────────────

        [HttpPost, ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        // ── GET /Account/AccessDenied ─────────────────────────────────────────

        [AllowAnonymous]
        public IActionResult AccessDenied() => View();

        // ── Private helpers ───────────────────────────────────────────────────

        private async Task SignInAsync(Employee employee, bool rememberMe)
        {
            var claims = new List<Claim>
            {
                new(ClaimTypes.NameIdentifier, employee.Id.ToString()),
                new(ClaimTypes.Name,           employee.FullName),
                new(ClaimTypes.Email,          employee.Email),
                new(ClaimTypes.Role,           employee.Role.ToString()),
                new("DepartmentId",            employee.DepartmentId.ToString())
            };

            var identity = new ClaimsIdentity(claims, CookieAuthenticationDefaults.AuthenticationScheme);
            var principal = new ClaimsPrincipal(identity);

            await HttpContext.SignInAsync(
                CookieAuthenticationDefaults.AuthenticationScheme,
                principal,
                new AuthenticationProperties { IsPersistent = rememberMe });
        }

        private IActionResult RedirectBasedOnRole(EmployeeRole role, string? returnUrl)
        {
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return role switch
            {
                EmployeeRole.Admin => RedirectToAction("Employees", "Admin"),
                EmployeeRole.HR => RedirectToAction("Queue", "Hr"),
                EmployeeRole.HOD => RedirectToAction("Queue", "Hod"),
                _ => RedirectToAction("Dashboard", "Leave")
            };
        }
    }
}
