// =============================================================================
// Employee_Leave_Portal — Program.cs
// =============================================================================

using Employee_Leave_Portal.Data;
using Employee_Leave_Portal.Services;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ── MVC ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews();

// ── Entity Framework Core ─────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ── Cookie Authentication ─────────────────────────────────────────────────────
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Account/Login";
        options.AccessDeniedPath = "/Account/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromHours(8);
        options.SlidingExpiration = true;
        options.Cookie.HttpOnly = true;
        options.Cookie.SecurePolicy = Microsoft.AspNetCore.Http.CookieSecurePolicy.SameAsRequest;
    });

// ── Application Services ──────────────────────────────────────────────────────
builder.Services.AddScoped<ILeaveService, LeaveService>();
builder.Services.AddScoped<IApprovalService, ApprovalService>();

// ── EPPlus license ────────────────────────────────────────────────────────────
OfficeOpenXml.ExcelPackage.License.SetNonCommercialPersonal("Employee_Leave_Portal");

// ── TempData (cookie-based, no session needed) ────────────────────────────────
builder.Services.AddHttpContextAccessor();

var app = builder.Build();

// ── Middleware pipeline ───────────────────────────────────────────────────────
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();   // must be before UseAuthorization
app.UseAuthorization();

// ── Default route ─────────────────────────────────────────────────────────────
app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Account}/{action=Login}/{id?}");

// ── Auto-migrate on startup (dev convenience) ─────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Seed initial data if empty
    await DbSeeder.SeedAsync(db);
}

app.Run();
