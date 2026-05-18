// =============================================================================
// Employee_Leave_Portal — DbContext
// File: Data/AppDbContext.cs
// =============================================================================

using Employee_Leave_Portal.Models;
using Microsoft.EntityFrameworkCore;

namespace Employee_Leave_Portal.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        // ── DbSets ────────────────────────────────────────────────────────────
        public DbSet<Department> Departments => Set<Department>();
        public DbSet<Employee> Employees => Set<Employee>();
        public DbSet<LeaveBalance> LeaveBalances => Set<LeaveBalance>();
        public DbSet<LeaveRequest> LeaveRequests => Set<LeaveRequest>();
        public DbSet<ApprovalLog> ApprovalLogs => Set<ApprovalLog>();

        // ── Fluent API ────────────────────────────────────────────────────────
        protected override void OnModelCreating(ModelBuilder mb)
        {
            base.OnModelCreating(mb);

            // ── Department ────────────────────────────────────────────────────

            mb.Entity<Department>(d =>
            {
                d.HasIndex(x => x.DepartmentName).IsUnique();

                // Self-referencing HOD — restrict delete to prevent cascade loops
                d.HasOne(x => x.HOD)
                 .WithMany()
                 .HasForeignKey(x => x.HOD_EmployeeId)
                 .OnDelete(DeleteBehavior.Restrict);
            });

            // ── Employee ──────────────────────────────────────────────────────

            mb.Entity<Employee>(e =>
            {
                // Email is the unique business key used for Excel upserts
                e.HasIndex(x => x.Email).IsUnique();

                e.HasOne(x => x.Department)
                 .WithMany(d => d.Employees)
                 .HasForeignKey(x => x.DepartmentId)
                 .OnDelete(DeleteBehavior.Restrict);

                e.Property(x => x.Role)
                 .HasConversion<string>()     // store enum as string for readability
                 .HasMaxLength(20);
            });

            // ── LeaveBalance ──────────────────────────────────────────────────

            mb.Entity<LeaveBalance>(lb =>
            {
                // One balance row per employee per year
                lb.HasIndex(x => new { x.EmployeeId, x.Year }).IsUnique();

                lb.HasOne(x => x.Employee)
                  .WithMany(e => e.LeaveBalances)
                  .HasForeignKey(x => x.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);

                lb.Property(x => x.PaidLeaveBalance)
                  .HasColumnType("decimal(5,1)")
                  .HasDefaultValue(18.0m);
            });

            // ── LeaveRequest ──────────────────────────────────────────────────

            mb.Entity<LeaveRequest>(lr =>
            {
                lr.HasOne(x => x.Employee)
                  .WithMany(e => e.LeaveRequests)
                  .HasForeignKey(x => x.EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);

                lr.Property(x => x.LeaveType)
                  .HasConversion<string>()
                  .HasMaxLength(20);

                lr.Property(x => x.Status)
                  .HasConversion<string>()
                  .HasMaxLength(20);
            });

            // ── ApprovalLog ───────────────────────────────────────────────────

            mb.Entity<ApprovalLog>(al =>
            {
                al.HasOne(x => x.LeaveRequest)
                  .WithMany(lr => lr.ApprovalLogs)
                  .HasForeignKey(x => x.LeaveRequestId)
                  .OnDelete(DeleteBehavior.Cascade);   // log disappears with request

                // Reviewer FK — restrict to preserve audit trail
                al.HasOne(x => x.ActionBy)
                  .WithMany(e => e.ApprovalLogs)
                  .HasForeignKey(x => x.ActionBy_EmployeeId)
                  .OnDelete(DeleteBehavior.Restrict);
            });
        }
    }
}
