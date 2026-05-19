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

// =============================================================================
// Employee_Leave_Portal — AdminController
// File: Controllers/AdminController.cs
// =============================================================================


using OfficeOpenXml;
using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Models;
using Employee_Leave_Portal.ViewModels;
using Microsoft.AspNetCore.Authorization;
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
                    EmployeeCode = e.EmployeeCode,
                    FullName = e.FullName,
                    Email = e.Email,
                    Role = e.Role.ToString(),
                    DepartmentName = e.Department != null ? e.Department.DepartmentName : "—",
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
                EmployeeCode = vm.EmployeeCode.Trim(),
                FullName = vm.FullName.Trim(),
                Email = vm.Email.Trim().ToLowerInvariant(),
                Role = vm.Role,
                DepartmentId = vm.DepartmentId,
                IsActive = true
            };

            _db.Employees.Add(employee);
            await _db.SaveChangesAsync();

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
                EmployeeCode = employee.EmployeeCode,
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

            employee.EmployeeCode = vm.EmployeeCode.Trim();
            employee.FullName = vm.FullName.Trim();
            employee.Role = vm.Role;
            employee.DepartmentId = vm.DepartmentId;
            employee.IsActive = vm.IsActive;

            await _db.SaveChangesAsync();

            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = $"{employee.FullName} updated successfully.";

            bool codeTaken = await _db.Employees
    .AnyAsync(e => e.EmployeeCode == vm.EmployeeCode.Trim());

            if (codeTaken)
            {
                ModelState.AddModelError(nameof(vm.EmployeeCode),
                    "This Employee ID already exists.");
                await PopulateDepartmentsAsync(vm);
                return View(vm);
            }

            return RedirectToAction(nameof(Employees));
        }

        // ── GET /Admin/EditBalance/{employeeId} ───────────────────────────────

        public async Task<IActionResult> EditBalance(int employeeId)
        {
            var employee = await _db.Employees.FindAsync(employeeId);
            if (employee is null) return NotFound();

            var balance = await _db.LeaveBalances
                .FirstOrDefaultAsync(lb =>
                    lb.EmployeeId == employeeId &&
                    lb.Year == DateTime.Today.Year);

            if (balance is null)
            {
                await ProvisionLeaveBalanceAsync(employeeId);
                balance = await _db.LeaveBalances
                    .FirstOrDefaultAsync(lb =>
                        lb.EmployeeId == employeeId &&
                        lb.Year == DateTime.Today.Year);
            }

            var vm = new EditBalanceVm
            {
                EmployeeId = employeeId,
                EmployeeName = employee.FullName,
                Year = DateTime.Today.Year,
                PaidLeaveBalance = balance!.PaidLeaveBalance,
                ShortLeaveUsedThisMonth = balance.ShortLeaveUsedThisMonth
            };

            return View(vm);
        }

        // ── POST /Admin/EditBalance ───────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> EditBalance(EditBalanceVm vm)
        {
            if (!ModelState.IsValid) return View(vm);

            var balance = await _db.LeaveBalances
                .FirstOrDefaultAsync(lb =>
                    lb.EmployeeId == vm.EmployeeId &&
                    lb.Year == vm.Year);

            if (balance is null) return NotFound();

            balance.PaidLeaveBalance = vm.PaidLeaveBalance;
            balance.ShortLeaveUsedThisMonth = vm.ShortLeaveUsedThisMonth;

            await _db.SaveChangesAsync();

            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = $"Balance updated for {vm.EmployeeName}.";

            return RedirectToAction(nameof(Employees));
        }

        // ── GET /Admin/BulkUpload ─────────────────────────────────────────────

        public IActionResult BulkUpload() => View(new BulkUploadVm());

        // ── POST /Admin/BulkUpload ────────────────────────────────────────────

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

            var departments = await _db.Departments
                .ToDictionaryAsync(
                    d => d.DepartmentName.Trim().ToLowerInvariant(),
                    d => d.Id);

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
                    string empCode = sheet.Cells[r, 5].Text.Trim();

                    // Skip blank rows silently
                    if (string.IsNullOrWhiteSpace(fullName) && string.IsNullOrWhiteSpace(email))
                        continue;

                    if (string.IsNullOrWhiteSpace(fullName) || string.IsNullOrWhiteSpace(email))
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

                    if (string.IsNullOrWhiteSpace(empCode))
                    {
                        result.Errors.Add($"Row {rowNumber}: Employee ID is required.");
                        continue;
                    }

                    if (existingEmployees.TryGetValue(email, out var existing))
                    {
                        // UPDATE — never touch leave history
                        existing.EmployeeCode = empCode;
                        existing.FullName = fullName;
                        existing.Role = role;
                        existing.DepartmentId = deptId;
                        result.Updated++;
                    }
                    else
                    {
                        // INSERT
                        var newEmployee = new Employee
                        {
                            EmployeeCode = empCode,
                            FullName = fullName,
                            Email = email,
                            Role = role,
                            DepartmentId = deptId,
                            IsActive = true
                        };

                        _db.Employees.Add(newEmployee);
                        await _db.SaveChangesAsync();

                        await ProvisionLeaveBalanceAsync(newEmployee.Id);

                        existingEmployees[email] = newEmployee;
                        result.Inserted++;
                    }
                }
                catch (Exception ex)
                {
                    result.Errors.Add($"Row {rowNumber}: Unexpected error — {ex.Message}");
                }
            }

            await _db.SaveChangesAsync();

            result.TotalRows = rowNumber - 1;

            TempData["AlertSeverity"] = result.Errors.Count == 0 ? "success" : "warning";
            TempData["AlertMessage"] =
                $"Upload complete. {result.Inserted} inserted, {result.Updated} updated, " +
                $"{result.Errors.Count} error(s).";

            return View("BulkUploadResult", result);
        }



        // ── GET /Admin/EmployeeDetail/{id} ────────────────────────────────────────────

        public async Task<IActionResult> EmployeeDetail(int id)
        {
            var employee = await _db.Employees
                .Include(e => e.Department)
                .FirstOrDefaultAsync(e => e.Id == id);

            if (employee is null) return NotFound();

            int year = DateTime.Today.Year;

            var balance = await _db.LeaveBalances
                .FirstOrDefaultAsync(lb => lb.EmployeeId == id && lb.Year == year);

            var requests = await _db.LeaveRequests
                .Where(lr => lr.EmployeeId == id)
                .OrderByDescending(lr => lr.LeaveDate)
                .ToListAsync();

            // Calculate used paid leave = 18 - remaining
            decimal paidLeaveUsed = 18.0m - (balance?.PaidLeaveBalance ?? 18.0m);

            var vm = new EmployeeDetailVm
            {
                Id = employee.Id,
                FullName = employee.FullName,
                Email = employee.Email,
                Role = employee.Role.ToString(),
                DepartmentName = employee.Department?.DepartmentName ?? "—",
                IsActive = employee.IsActive,
                Year = year,
                PaidLeaveBalance = balance?.PaidLeaveBalance ?? 18.0m,
                PaidLeaveUsed = Math.Max(0, paidLeaveUsed),
                ShortLeaveUsedThisMonth = balance?.ShortLeaveUsedThisMonth ?? 0,
                TotalRequests = requests.Count,
                ApprovedCount = requests.Count(r => r.Status == LeaveStatus.Approved),
                PendingCount = requests.Count(r => r.Status == LeaveStatus.Pending_HOD
                                                           || r.Status == LeaveStatus.Pending_HR),
                RejectedCount = requests.Count(r => r.Status == LeaveStatus.Rejected),
                LossOfPayCount = requests.Count(r => r.IsLossOfPay && r.Status == LeaveStatus.Approved),
                Requests = requests.Select(r => new LeaveRequestRowVm
                {
                    Id = r.Id,
                    LeaveType = r.LeaveType.ToString(),
                    LeaveDate = r.LeaveDate,
                    DateApplied = r.DateApplied,
                    Hours = r.Hours,
                    Status = r.Status.ToString(),
                    IsLossOfPay = r.IsLossOfPay,
                    Reason = r.Reason
                }).ToList()
            };

            return View(vm);
        }


        // ── Private helpers ───────────────────────────────────────────────────

        /// <summary>
        /// Creates a pro-rated LeaveBalance for the current year.
        /// May = month 5 → 18/12 * 8 = 12.0 days remaining.
        /// </summary>
        private async Task ProvisionLeaveBalanceAsync(int employeeId)
        {
            int year = DateTime.Today.Year;
            int month = DateTime.Today.Month;

            bool exists = await _db.LeaveBalances.AnyAsync(lb =>
                lb.EmployeeId == employeeId && lb.Year == year);

            if (!exists)
            {
                decimal proRated = Math.Round(18.0m / 12 * (13 - month), 1);

                _db.LeaveBalances.Add(new LeaveBalance
                {
                    EmployeeId = employeeId,
                    Year = year,
                    PaidLeaveBalance = proRated,
                    ShortLeaveUsedThisMonth = 0,
                    LastResetMonth = month,
                    LastResetYear = year
                });

                await _db.SaveChangesAsync();
            }
        }

        private async Task PopulateDepartmentsAsync(AddEmployeeVm vm)
        {
            vm.Departments = await GetDepartmentSelectList();
        }

        private async Task PopulateDepartmentsAsync(EditEmployeeVm vm)
        {
            vm.Departments = await GetDepartmentSelectList();
        }

        private async Task<List<SelectListItem>> GetDepartmentSelectList()
        {
            return await _db.Departments
                .OrderBy(d => d.DepartmentName)
                .Select(d => new SelectListItem
                {
                    Value = d.Id.ToString(),
                    Text = d.DepartmentName
                })
                .ToListAsync();
        }
    }
}
