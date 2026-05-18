// =============================================================================
// Employee_Leave_Portal — DbSeeder
// File: Data/DbSeeder.cs
// =============================================================================
// Seeds one Admin employee + one default Department on first run.
// Safe to call on every startup — checks before inserting.
// =============================================================================

using System;
using System.Linq;
using System.Threading.Tasks;
using Employee_Leave_Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Data
{
    public static class DbSeeder
    {
        public static async Task SeedAsync(AppDbContext db)
        {
            // ── Department ────────────────────────────────────────────────────
            if (!await db.Departments.AnyAsync())
            {
                db.Departments.Add(new Department
                {
                    DepartmentName = "Administration",
                    HOD_EmployeeId = null   // set after admin is created
                });
                await db.SaveChangesAsync();
            }

            // ── Admin employee ────────────────────────────────────────────────
            const string adminEmail = "admin@company.com";

            if (!await db.Employees.AnyAsync(e => e.Email == adminEmail))
            {
                var dept = await db.Departments.FirstAsync();

                var admin = new Employee
                {
                    FullName = "System Admin",
                    Email = adminEmail,
                    Role = EmployeeRole.Admin,
                    DepartmentId = dept.Id,
                   
                    IsActive = true
                };

                db.Employees.Add(admin);
                await db.SaveChangesAsync();

                // Provision leave balance for admin
                db.LeaveBalances.Add(new LeaveBalance
                {
                    EmployeeId = admin.Id,
                    Year = DateTime.Today.Year,
                    PaidLeaveBalance = 18.0m,
                    ShortLeaveUsedThisMonth = 0,
                    LastResetMonth = DateTime.Today.Month,
                    LastResetYear = DateTime.Today.Year
                });

                await db.SaveChangesAsync();
            }
        }
    }
}
