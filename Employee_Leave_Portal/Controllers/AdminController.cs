// =============================================================================
// Employee_Leave_Portal — AdminController
// File: Controllers/AdminController.cs
// =============================================================================
//
// Actions:
//   GET  /Admin/Employees              — list all employees
//   GET  /Admin/AddEmployee            — blank single-add form
//   POST /Admin/AddEmployee            — insert one employee
//   GET  /Admin/EditEmployee/{id}      — edit form for existing employee
//   POST /Admin/EditEmployee/{id}      — update employee metadata
//   GET  /Admin/BulkUpload             — Excel upload form
//   POST /Admin/BulkUpload             — parse sheet, upsert employees
//
// Access: Role = Admin only
//
// Excel upsert strategy (per spec):
//   • Unique key = Email
//   • Existing employee → update Name, DepartmentId, Salary, Role only
//   • New employee      → insert + provision 18 paid leave days
//   • Leave history, requests, and balances are NEVER deleted
//
// NuGet required: ClosedXML (dotnet add package ClosedXML)
//
// =============================================================================

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OfficeOpenXml;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
using Employee_Leave_Portal.ViewModels;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Controllers
{
    [Authorize(Roles = "Admin")]
    public class AdminController : Controller
    {
        private readonly AppDbContext _db;

        public AdminController(AppDbContext db) => _db = db;

        // ── GET /Admin/Employees ──────────────────────────────────────────────

        public async Task<IActionResult> Employees()
        {
            var employees = await _db.Employees
                .Include(e => e.Department)
                .OrderBy(e => e.FullName)
                .Select(e => new EmployeeRowVm
                {
                    Id = e.Id,
                    FullName = e.FullName,
                    Email = e.Email,
                    Role = e.Role.ToString(),
                    DepartmentName = e.Department != null ? e.Department.DepartmentName : "—",
                    //Salary = e.Salary,
                    IsActive = e.IsActive
                })
                .ToListAsync();

            return View(employees);
        }

        // ── GET /Admin/AddEmployee ────────────────────────────────────────────

        public async Task<IActionResult> AddEmployee()
        {
            var vm = new AddEmployeeVm();
            await PopulateDepartmentsAsync(vm);
            return View(vm);
        }

        // ── POST /Admin/AddEmployee ───────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> AddEmployee(AddEmployeeVm vm)
        {
            if (!ModelState.IsValid)
            {
                await PopulateDepartmentsAsync(vm);
                return View(vm);
            }

            // Email uniqueness check
            bool emailTaken = await _db.Employees
                .AnyAsync(e => e.Email == vm.Email);

            if (emailTaken)
            {
                ModelState.AddModelError(nameof(vm.Email),
                    "An employee with this email already exists.");
                await PopulateDepartmentsAsync(vm);
                return View(vm);
            }

            var employee = new Employee
            {
                FullName = vm.FullName.Trim(),
                Email = vm.Email.Trim().ToLowerInvariant(),
                Role = vm.Role,
                DepartmentId = vm.DepartmentId,
                IsActive = true
            };

            _db.Employees.Add(employee);
            await _db.SaveChangesAsync();

            // Provision initial leave balance
            await ProvisionLeaveBalanceAsync(employee.Id);

            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = $"{employee.FullName} added successfully.";

            return RedirectToAction(nameof(Employees));
        }

        // ── GET /Admin/EditEmployee/{id} ──────────────────────────────────────

        public async Task<IActionResult> EditEmployee(int id)
        {
            var employee = await _db.Employees.FindAsync(id);
            if (employee is null) return NotFound();

            var vm = new EditEmployeeVm
            {
                Id = employee.Id,
                FullName = employee.FullName,
                Email = employee.Email,
                Role = employee.Role,
                DepartmentId = employee.DepartmentId,
                IsActive = employee.IsActive
            };

            await PopulateDepartmentsAsync(vm);
            return View(vm);
        }

        // ── POST /Admin/EditEmployee/{id} ─────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditEmployee(int id, EditEmployeeVm vm)
        {
            if (id != vm.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                await PopulateDepartmentsAsync(vm);
                return View(vm);
            }

            var employee = await _db.Employees.FindAsync(id);
            if (employee is null) return NotFound();

            // Only metadata is updated — never touch leave history
            employee.FullName = vm.FullName.Trim();
            employee.Role = vm.Role;
            employee.DepartmentId = vm.DepartmentId;
           
            employee.IsActive = vm.IsActive;

            await _db.SaveChangesAsync();

            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = $"{employee.FullName} updated successfully.";

            return RedirectToAction(nameof(Employees));
        }

        // ── GET /Admin/BulkUpload ─────────────────────────────────────────────

        public IActionResult BulkUpload() => View(new BulkUploadVm());

        // ── POST /Admin/BulkUpload ────────────────────────────────────────────
        //
        // Expected Excel columns (row 1 = header, data from row 2):
        //   A: FullName
        //   B: Email
        //   C: Role         (Employee | HOD | HR | Admin)
        //   D: DepartmentName
        //   E: Salary
        //
        // Upsert key = Email (case-insensitive)

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkUpload(IFormFile file)
        {
            if (file is null || file.Length == 0)
            {
                ModelState.AddModelError(string.Empty, "Please select an Excel file.");
                return View(new BulkUploadVm());
            }

            var extension = Path.GetExtension(file.FileName).ToLowerInvariant();
            if (extension is not ".xlsx" and not ".xls")
            {
                ModelState.AddModelError(string.Empty,
                    "Only .xlsx and .xls files are supported.");
                return View(new BulkUploadVm());
            }

            var result = new BulkUploadResultVm();

            // Load all departments once — used for name → id lookup
            var departments = await _db.Departments
                .ToDictionaryAsync(
                    d => d.DepartmentName.Trim().ToLowerInvariant(),
                    d => d.Id);

            // Load all existing employees keyed by email for upsert
            var existingEmployees = await _db.Employees
                .ToDictionaryAsync(
                    e => e.Email.ToLowerInvariant(),
                    e => e);

            using var stream = new MemoryStream();
            await file.CopyToAsync(stream);
            stream.Position = 0;

            

            using var package = new ExcelPackage(stream);
            var sheet = package.Workbook.Worksheets[0];
            int totalRows = sheet.Dimension?.Rows ?? 0;

            int rowNumber = 2;

            for (int r = 2; r <= totalRows; r++)
            {
                rowNumber = r;

                try
                {
                    string fullName = sheet.Cells[r, 1].Text.Trim();
                    string email = sheet.Cells[r, 2].Text.Trim().ToLowerInvariant();
                    string roleStr = sheet.Cells[r, 3].Text.Trim();
                    string deptName = sheet.Cells[r, 4].Text.Trim().ToLowerInvariant();

                    // ── Basic validation ──────────────────────────────────────

                    // Skip completely empty rows silently
                    if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(email))
                        continue;

                    // Only error if one is missing but not both
                    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email))
                    {
                        result.Errors.Add($"Row {rowNumber}: FullName and Email are required.");
                        continue;
                    }
                    {
                        result.Errors.Add($"Row {rowNumber}: FullName and Email are required.");
                        continue;
                    }

                    if (!Enum.TryParse<EmployeeRole>(roleStr, ignoreCase: true, out var role))
                    {
                        result.Errors.Add(
                            $"Row {rowNumber}: Invalid role '{roleStr}'. " +
                            "Expected: Employee, HOD, HR, or Admin.");
                        continue;
                    }

                    if (!departments.TryGetValue(deptName, out int deptId))
                    {
                        result.Errors.Add(
                            $"Row {rowNumber}: Department '{sheet.Cells[r, 4].Text.Trim()}' " +
                            "not found. Add it first.");
                        continue;
                    }

                   

                    // ── Upsert ────────────────────────────────────────────────

                    if (existingEmployees.TryGetValue(email, out var existing))
                    {
                        // UPDATE — only profile metadata, never touch history
                        existing.FullName = fullName;
                        existing.Role = role;
                        existing.DepartmentId = deptId;
                        //existing.Salary = salary;

                        result.Updated++;
                    }
                    else
                    {
                        // INSERT
                        var newEmployee = new Employee
                        {
                            FullName = fullName,
                            Email = email,
                            Role = role,
                            DepartmentId = deptId,
                           
                            IsActive = true
                        };

                        _db.Employees.Add(newEmployee);
                        await _db.SaveChangesAsync();   // need Id for balance provisioning

                        await ProvisionLeaveBalanceAsync(newEmployee.Id);

                        // Add to dictionary to catch duplicate emails within the same sheet
                        existingEmployees[email] = newEmployee;

                        result.Inserted++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {rowNumber}: Unexpected error — {ex.Message}");
                }
            }

            // Commit all updates in one final save
            await _db.SaveChangesAsync();

            result.TotalRows = rowNumber - 1;

            TempData["AlertSeverity"] = result.Errors.Count == 0 ? "success" : "warning";
            TempData["AlertMessage"] =
                $"Upload complete. {result.Inserted} inserted, {result.Updated} updated, " +
                $"{result.Errors.Count} error(s).";

            return View("BulkUploadResult", result);
        }

        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a LeaveBalance for the current year with 18 paid days if one
        /// does not already exist. Safe to call on both insert and re-provision.
        /// </summary>
        private async Task ProvisionLeaveBalanceAsync(int employeeId)
        {
            int year = DateTime.Today.Year;

            bool exists = await _db.LeaveBalances.AnyAsync(lb =>
                lb.EmployeeId == employeeId && lb.Year == year);

            if (!exists)
            {
                _db.LeaveBalances.Add(new LeaveBalance
                {
                    EmployeeId = employeeId,
                    Year = year,
                    PaidLeaveBalance = 18.0m,
                    ShortLeaveUsedThisMonth = 0,
                    LastResetMonth = DateTime.Today.Month,
                    LastResetYear = DateTime.Today.Year
                });

                await _db.SaveChangesAsync();
            }
        }

        /// <summary>Populates the Departments dropdown for add/edit forms.</summary>
        private async Task PopulateDepartmentsAsync(dynamic vm)
        {
            var departments = await _db.Departments
                .OrderBy(d => d.DepartmentName)
                .Select(d => new SelectListItem
                {
                    Value = d.Id.ToString(),
                    Text = d.DepartmentName
                })
                .ToListAsync();

            vm.Departments = departments;
        }
    }
}