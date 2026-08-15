using System.Diagnostics;
using LicenciasCarpetas.Configuration;
using LicenciasCarpetas.Dashboard.Auth;
using LicenciasCarpetas.Import;
using LicenciasCarpetas.Persistence;
using LicenciasCarpetas.Reporting;
using LicenciasCarpetas.Statistics;
using Microsoft.AspNetCore.Authentication.Cookies;

// ContentRootPath pinned to the exe's own folder (not the process's current directory) so every
// relative path in config (SqliteDbPath, ExportDirectory, workbook path) resolves the same way no
// matter how the app is launched — double-click, shortcut, or Task Scheduler.
var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory
});

var options = builder.Configuration.GetSection(CarpetasOptions.SectionName).Get<CarpetasOptions>()
    ?? throw new InvalidOperationException($"Missing '{CarpetasOptions.SectionName}' configuration section.");
builder.Services.AddSingleton(options);

// SQLite resolves a relative path against the process's working directory, which changes with how
// the app was launched (shortcut, Task Scheduler, dotnet run). Pin it to the app folder instead so
// the same database is always the one opened.
var databasePath = Path.IsPathRooted(options.SqliteDbPath)
    ? options.SqliteDbPath
    : Path.Combine(AppContext.BaseDirectory, options.SqliteDbPath);
var connectionString = $"Data Source={databasePath}";
builder.Services.AddSingleton<IFolderCaseRepository>(_ => new FolderCaseRepository(connectionString));
builder.Services.AddSingleton<IDailyCounterRepository>(_ => new DailyCounterRepository(connectionString));
builder.Services.AddSingleton<IComunaContactRepository>(_ => new ComunaContactRepository(connectionString));
builder.Services.AddSingleton<IUserRepository>(_ => new UserRepository(connectionString));
builder.Services.AddSingleton<ILoginService, LoginService>();
builder.Services.AddSingleton<IExcelWorkbookImporter, ExcelWorkbookImporter>();
builder.Services.AddSingleton<IExcelCaseExporter, ExcelCaseExporter>();
builder.Services.AddSingleton<StatisticsService>();

builder.Services
    .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(cookieOptions =>
    {
        cookieOptions.LoginPath = "/Login";
        cookieOptions.Cookie.SecurePolicy = CookieSecurePolicy.Always; // dashboard is HTTPS-only
        cookieOptions.ExpireTimeSpan = TimeSpan.FromHours(8);
        cookieOptions.SlidingExpiration = true;
    });
builder.Services.AddAuthorization();
builder.Services.AddRazorPages(razorOptions => razorOptions.RootDirectory = "/Dashboard/Pages");

var app = builder.Build();

EnsureDataDirectory(databasePath);

// Copy before the schema migrations touch anything, so the copy is the last known-good state.
var backupPath = new DatabaseBackup(
    databasePath,
    Path.Combine(Path.GetDirectoryName(databasePath)!, "backups"),
    options.BackupsToKeep).Run(DateTimeOffset.Now);
if (backupPath is not null)
{
    Console.WriteLine($"Respaldo: {backupPath}");
}

EnsureSchemas(app.Services);

// Password recovery: there is no mail transport here, so recovery is an operation run on the
// machine itself (see the /ForgotPassword screen, which points the operator at this command).
if (args.Contains("--reset-password") || args.Contains("--list-users"))
{
    var users = app.Services.GetRequiredService<IUserRepository>();

    if (args.Contains("--list-users"))
    {
        var names = users.AllUsernames();
        Console.WriteLine(names.Count == 0
            ? "No hay usuarios creados. Cree uno con: --add-user <usuario>"
            : $"Usuarios: {string.Join(", ", names)}");
        return;
    }

    var targetIndex = Array.IndexOf(args, "--reset-password") + 1;
    if (targetIndex >= args.Length || args[targetIndex].StartsWith("--", StringComparison.Ordinal))
    {
        Console.Error.WriteLine("Falta el usuario: --reset-password <usuario>");
        Environment.ExitCode = 1;
        return;
    }

    var targetUsername = args[targetIndex];
    if (users.FindByUsername(targetUsername) is not { } targetUser)
    {
        Console.Error.WriteLine($"No existe el usuario '{targetUsername}'. Vea la lista con --list-users.");
        Environment.ExitCode = 1;
        return;
    }

    var newPassword = Environment.GetEnvironmentVariable("LC_ADMIN_PASSWORD");
    if (string.IsNullOrEmpty(newPassword))
    {
        Console.Write("Contraseña nueva: ");
        newPassword = ReadPasswordMasked();
    }

    if (newPassword.Length < 8)
    {
        Console.Error.WriteLine("La contraseña debe tener al menos 8 caracteres. No se cambió nada.");
        Environment.ExitCode = 1;
        return;
    }

    var reset = PasswordHasher.Hash(newPassword);
    users.ResetPassword(targetUser.Id, reset.Hash, reset.Salt, reset.Iterations);
    Console.WriteLine($"Contraseña de '{targetUsername}' restablecida. La cuenta quedó desbloqueada.");
    return;
}

// One-time admin CLI commands, run instead of starting the host.
if (args.Contains("--add-user") || args.Contains("--remove-user"))
{
    var users = app.Services.GetRequiredService<IUserRepository>();

    if (args.Contains("--add-user"))
    {
        var username = args[Array.IndexOf(args, "--add-user") + 1];
        var password = Environment.GetEnvironmentVariable("LC_ADMIN_PASSWORD");
        if (string.IsNullOrEmpty(password))
        {
            Console.Write("Contraseña: ");
            password = ReadPasswordMasked();
        }
        var (hash, salt, iterations) = PasswordHasher.Hash(password);
        users.Insert(new DashboardUser { Username = username, PasswordHash = hash, PasswordSalt = salt, Iterations = iterations });
        Console.WriteLine($"Usuario '{username}' creado.");
    }
    else
    {
        var username = args[Array.IndexOf(args, "--remove-user") + 1];
        users.Delete(username);
        Console.WriteLine($"Usuario '{username}' eliminado.");
    }

    return;
}

// Bulk import from the command line — the same code path the /Importar screen uses.
if (args.Contains("--import"))
{
    var pathIndex = Array.IndexOf(args, "--import") + 1;
    var workbookPath = pathIndex < args.Length && !args[pathIndex].StartsWith("--", StringComparison.Ordinal)
        ? args[pathIndex]
        : options.DefaultWorkbookPath;

    if (string.IsNullOrWhiteSpace(workbookPath))
    {
        Console.Error.WriteLine("Falta la ruta del Excel: --import \"<ruta>\" (o configure Carpetas:DefaultWorkbookPath).");
        Environment.ExitCode = 1;
        return;
    }

    var importer = app.Services.GetRequiredService<IExcelWorkbookImporter>();
    var summary = importer.Import(workbookPath);

    Console.WriteLine($"Hojas leídas:        {summary.SheetsRead}");
    Console.WriteLine($"Filas leídas:        {summary.RowsRead}");
    Console.WriteLine($"Casos nuevos:        {summary.CasesInserted}");
    Console.WriteLine($"Casos actualizados:  {summary.CasesUpdated}");
    Console.WriteLine($"Requieren revisión:  {summary.CasesNeedingReview}");
    Console.WriteLine($"Días con contadores: {summary.CountersImported}");
    Console.WriteLine($"Correos de comunas:  {summary.ContactsImported}");
    foreach (var warning in summary.Warnings)
    {
        Console.WriteLine($"AVISO: {warning}");
    }

    return;
}

_ = Task.Run(async () =>
{
    try
    {
        // Small delay to let the server bind before opening the browser
        await Task.Delay(2000);
        var url = "https://localhost:5011";
        Console.WriteLine($"Abriendo dashboard: {url}");
        Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
    }
    catch (Exception ex)
    {
        Console.WriteLine($"No se pudo abrir el navegador automáticamente: {ex.Message}");
        Console.WriteLine("Abre https://localhost:5011 manualmente en tu navegador.");
    }
});

app.UseHttpsRedirection();
app.UseStaticFiles();
app.UseAuthentication();
app.UseAuthorization();
app.MapRazorPages();

app.Run();

static void EnsureDataDirectory(string sqliteDbPath)
{
    var directory = Path.GetDirectoryName(Path.GetFullPath(sqliteDbPath));
    if (!string.IsNullOrEmpty(directory))
    {
        Directory.CreateDirectory(directory);
    }
}

static void EnsureSchemas(IServiceProvider services)
{
    services.GetRequiredService<IFolderCaseRepository>().EnsureSchema();
    services.GetRequiredService<IDailyCounterRepository>().EnsureSchema();
    services.GetRequiredService<IComunaContactRepository>().EnsureSchema();
    services.GetRequiredService<IUserRepository>().EnsureSchema();
}

static string ReadPasswordMasked()
{
    var password = new System.Text.StringBuilder();
    ConsoleKeyInfo key;
    while ((key = Console.ReadKey(intercept: true)).Key != ConsoleKey.Enter)
    {
        if (key.Key == ConsoleKey.Backspace && password.Length > 0)
        {
            password.Remove(password.Length - 1, 1);
        }
        else if (!char.IsControl(key.KeyChar))
        {
            password.Append(key.KeyChar);
        }
    }
    Console.WriteLine();
    return password.ToString();
}

/// <summary>Exposed so the test project can reference the web application assembly.</summary>
public partial class Program;
