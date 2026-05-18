// =============================================================================
// Employee_Leave_Portal — AccountController
// File: Controllers/AccountController.cs
// =============================================================================

using System.Collections.Generic;
using System.Security.Claims;
using System.Threading.Tasks;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
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

        public AccountController(AppDbContext db) => _db = db;

        // ── GET /Account/Login ────────────────────────────────────────────────

        [HttpGet]
        [AllowAnonymous]
        public IActionResult Login(string? returnUrl = null)
        {
            if (User.Identity?.IsAuthenticated == true)
                return RedirectToAction("Dashboard", "Leave");

            ViewData["ReturnUrl"] = returnUrl;
            return View();
        }

        // ── POST /Account/Login ───────────────────────────────────────────────

        [HttpPost]
        [AllowAnonymous]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Login(LoginVm vm, string? returnUrl = null)
        {
            ViewData["ReturnUrl"] = returnUrl;

            if (!ModelState.IsValid)
                return View(vm);

            // Look up by email only — no password in this system (extend as needed)
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

            // Build claims
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
                new AuthenticationProperties { IsPersistent = vm.RememberMe });

            // Role-based redirect
            if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
                return Redirect(returnUrl);

            return employee.Role switch
            {
                EmployeeRole.Admin => RedirectToAction("Employees", "Admin"),
                EmployeeRole.HR => RedirectToAction("Queue", "Hr"),
                EmployeeRole.HOD => RedirectToAction("Queue", "Hod"),
                _ => RedirectToAction("Dashboard", "Leave")
            };
        }

        // ── POST /Account/Logout ──────────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Logout()
        {
            await HttpContext.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return RedirectToAction(nameof(Login));
        }

        // ── GET /Account/AccessDenied ─────────────────────────────────────────

        [AllowAnonymous]
        public IActionResult AccessDenied() => View();
    }
}


