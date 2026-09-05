using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using WebApplication1.Data;
using WebApplication1.Models;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // FIX for Render inotify limit (128) — do not watch json files for reload; polling watcher
    // This prevents: System.IO.IOException: The configured user limit (128) on the number of inotify instances has been reached
    WebRootPath = "wwwroot"
});
// Disable reloadOnChange for all config sources (Render's inotify limit)
foreach (var src in builder.Configuration.Sources.ToList())
{
    if (src is Microsoft.Extensions.Configuration.Json.JsonConfigurationSource jsonSrc)
        jsonSrc.ReloadOnChange = false;
}

// Render sets PORT dynamically; bind to 0.0.0.0
var port = Environment.GetEnvironmentVariable("PORT") ?? "5000";
builder.WebHost.UseUrls($"http://0.0.0.0:{port}");

// Also tell PhysicalFileProvider to use polling instead of inotify where needed
Environment.SetEnvironmentVariable("DOTNET_USE_POLLING_FILE_WATCHER", "true");

// Helper: Neon gives postgresql:// URL, Npgsql prefers Host=...; also support Render's DATABASE_URL
// PRIORITY: DATABASE_URL (Render/Neon) > ConnectionStrings__DefaultConnection > appsettings DefaultConnection
// otherwise placeholder in appsettings would shadow Neon on Render
string? rawConn = Environment.GetEnvironmentVariable("DATABASE_URL")
    ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
    ?? builder.Configuration.GetConnectionString("DefaultConnection");

string? connectionString = null;
if (!string.IsNullOrWhiteSpace(rawConn))
{
    rawConn = rawConn.Trim();
    // If it's a postgres URL, convert to Npgsql key=value format
    if (rawConn.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
        rawConn.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
    {
        try
        {
            var uri = new Uri(rawConn);
            var userInfo = uri.UserInfo.Split(':', 2);
            var npgBuilder = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.Port > 0 ? uri.Port : 5432,
                Database = uri.AbsolutePath.Trim('/'),
                Username = userInfo.Length > 0 ? Uri.UnescapeDataString(userInfo[0]) : "",
                Password = userInfo.Length > 1 ? Uri.UnescapeDataString(userInfo[1]) : "",
                SslMode = SslMode.Require
            };
            // Parse query for channel_binding (simple, avoid System.Web dependency)
            if (!string.IsNullOrEmpty(uri.Query))
            {
                var query = uri.Query.TrimStart('?');
                foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
                {
                    var kv = part.Split('=', 2);
                    if (kv.Length == 2 && kv[0].Equals("channel_binding", StringComparison.OrdinalIgnoreCase)
                        && kv[1].Equals("require", StringComparison.OrdinalIgnoreCase))
                    {
                        npgBuilder.ChannelBinding = ChannelBinding.Require;
                        break;
                    }
                }
            }
            connectionString = npgBuilder.ConnectionString;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[DB] Failed to parse DATABASE_URL, using raw: {ex.Message}");
            connectionString = rawConn;
        }
    }
    else
    {
        connectionString = rawConn;
    }
}

// Fallback to placeholder if still null (will fail visibly on startup, logs host-sanitized)
if (string.IsNullOrWhiteSpace(connectionString))
{
    connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
}

// Log sanitized host for debugging on Render (never log password)
try
{
    var sanitized = new NpgsqlConnectionStringBuilder(connectionString).Host;
    Console.WriteLine($"[DB] Using host: {sanitized}");
}
catch { Console.WriteLine("[DB] Connection string configured (host parse failed)"); }

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseNpgsql(connectionString));

// Render runs behind proxy - forward headers so https detection works; avoid redirect loop
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownIPNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
{
    options.Password.RequireDigit = true;
    options.Password.RequireLowercase = false; // kept false for compatibility (suchi@iubat.edu uses Passw0rd)
    options.Password.RequireUppercase = true;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredLength = 6;
    options.User.RequireUniqueEmail = true;
    options.SignIn.RequireConfirmedEmail = false;
})
.AddEntityFrameworkStores<ApplicationDbContext>()
.AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath = "/Account/Login";
    options.AccessDeniedPath = "/Account/Login";
});

builder.Services.AddControllersWithViews();

var app = builder.Build();

// Forward headers must run early when behind Render
app.UseForwardedHeaders();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<ApplicationDbContext>();
        // Retry once for Neon cold-start
        var retries = 0;
        while (true)
        {
            try
            {
                await context.Database.EnsureCreatedAsync();
                break;
            }
            catch (Exception retryEx) when (retries < 1)
            {
                retries++;
                Console.WriteLine($"[DB] EnsureCreated retry {retries}: {retryEx.Message}");
                await Task.Delay(2000);
            }
        }
        await SeedData.InitializeAsync(services);
        Console.WriteLine("[DB] Database ready");
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while initializing the database. Connection host: {Host}", "see previous [DB] log");
        // Don't crash app - allow health check to respond; user will see error in logs
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

// Disable https redirect on Render (Render terminates TLS, causes loop). Only redirect locally.
var isRender = !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("RENDER"));
if (!isRender)
{
    app.UseHttpsRedirection();
}
app.UseStaticFiles();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();
