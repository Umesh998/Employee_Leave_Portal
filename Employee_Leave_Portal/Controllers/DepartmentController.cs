// =============================================================================
// Employee_Leave_Portal — DepartmentController
// File: Controllers/DepartmentController.cs
// =============================================================================

using System.Linq;
using System.Threading.Tasks;
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
    public class DepartmentController : Controller
    {
        private readonly AppDbContext _db;
        public DepartmentController(AppDbContext db) => _db = db;

        // ── GET /Department/Index ─────────────────────────────────────────────

        public async Task<IActionResult> Index()
        {
            var departments = await _db.Departments
                .Include(d => d.HOD)
                .OrderBy(d => d.DepartmentName)
                .Select(d => new DepartmentRowVm
                {
                    Id = d.Id,
                    DepartmentName = d.DepartmentName,
                    HODName = d.HOD != null ? d.HOD.FullName : "— Not Assigned —",
                    EmployeeCount = d.Employees.Count
                })
                .ToListAsync();

            return View(departments);
        }

        // ── GET /Department/Create ────────────────────────────────────────────

        public async Task<IActionResult> Create()
        {
            var vm = new DepartmentFormVm();
            await PopulateHodListAsync(vm);
            return View(vm);
        }

        // ── POST /Department/Create ───────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create(DepartmentFormVm vm)
        {
            if (!ModelState.IsValid)
            {
                await PopulateHodListAsync(vm);
                return View(vm);
            }

            bool exists = await _db.Departments
                .AnyAsync(d => d.DepartmentName == vm.DepartmentName.Trim());

            if (exists)
            {
                ModelState.AddModelError(nameof(vm.DepartmentName),
                    "A department with this name already exists.");
                await PopulateHodListAsync(vm);
                return View(vm);
            }

            _db.Departments.Add(new Department
            {
                DepartmentName = vm.DepartmentName.Trim(),
                HOD_EmployeeId = vm.HOD_EmployeeId == 0 ? null : vm.HOD_EmployeeId
            });

            await _db.SaveChangesAsync();

            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = $"Department '{vm.DepartmentName}' created.";

            return RedirectToAction(nameof(Index));
        }

        // ── GET /Department/Edit/{id} ─────────────────────────────────────────

        public async Task<IActionResult> Edit(int id)
        {
            var dept = await _db.Departments.FindAsync(id);
            if (dept is null) return NotFound();

            var vm = new DepartmentFormVm
            {
                Id = dept.Id,
                DepartmentName = dept.DepartmentName,
                HOD_EmployeeId = dept.HOD_EmployeeId ?? 0
            };

            await PopulateHodListAsync(vm);
            return View(vm);
        }

        // ── POST /Department/Edit/{id} ────────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, DepartmentFormVm vm)
        {
            if (id != vm.Id) return BadRequest();

            if (!ModelState.IsValid)
            {
                await PopulateHodListAsync(vm);
                return View(vm);
            }

            var dept = await _db.Departments.FindAsync(id);
            if (dept is null) return NotFound();

            dept.DepartmentName = vm.DepartmentName.Trim();
            dept.HOD_EmployeeId = vm.HOD_EmployeeId == 0 ? null : vm.HOD_EmployeeId;

            await _db.SaveChangesAsync();

            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = $"Department '{dept.DepartmentName}' updated.";

            return RedirectToAction(nameof(Index));
        }

        // ── POST /Department/Delete/{id} ──────────────────────────────────────

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Delete(int id)
        {
            var dept = await _db.Departments
                .Include(d => d.Employees)
                .FirstOrDefaultAsync(d => d.Id == id);

            if (dept is null) return NotFound();

            if (dept.Employees.Any())
            {
                TempData["AlertSeverity"] = "danger";
                TempData["AlertMessage"] =
                    $"Cannot delete '{dept.DepartmentName}' — it has {dept.Employees.Count} employee(s) assigned.";
                return RedirectToAction(nameof(Index));
            }

            _db.Departments.Remove(dept);
            await _db.SaveChangesAsync();

            TempData["AlertSeverity"] = "success";
            TempData["AlertMessage"] = $"Department '{dept.DepartmentName}' deleted.";

            return RedirectToAction(nameof(Index));
        }

        // ── Private ───────────────────────────────────────────────────────────

        private async Task PopulateHodListAsync(DepartmentFormVm vm)
        {
            var hods = await _db.Employees
                .Where(e => e.IsActive &&
                           (e.Role == EmployeeRole.HOD || e.Role == EmployeeRole.Admin))
                .OrderBy(e => e.FullName)
                .Select(e => new SelectListItem
                {
                    Value = e.Id.ToString(),
                    Text = $"{e.FullName} ({e.Role})"
                })
                .ToListAsync();

            hods.Insert(0, new SelectListItem { Value = "0", Text = "— Not Assigned —" });
            vm.HODOptions = hods;
        }
    }
}