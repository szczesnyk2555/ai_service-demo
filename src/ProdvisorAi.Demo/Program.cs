using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting.WindowsServices;
using ProdvisorAi.Demo.Data;
using ProdvisorAi.Demo.Endpoints;
using ProdvisorAi.Demo.Services;
using ProdvisorAi.Demo.Prodvisor;
using Serilog;
using Serilog.Events;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    // A Windows Service starts in C:\Windows\System32 - use the executable's folder as content root instead.
    ContentRootPath = WindowsServiceHelpers.IsWindowsService() ? AppContext.BaseDirectory : default,
});

// Runs as a normal console app with `dotnet run`, or as a Windows Service when started by the SCM.
builder.Host.UseWindowsService(o => o.ServiceName = "ProdvisorAi");

// ---- Storage -------------------------------------------------------------------------------
var dataDir = Path.GetFullPath(Path.Combine(
    builder.Environment.ContentRootPath, builder.Configuration["Storage:DataDirectory"] ?? "App_Data"));
var paths = new StoragePaths(dataDir);
builder.Services.AddSingleton(paths);
builder.Services.AddDbContext<AppDbContext>(o => o.UseSqlite($"Data Source={paths.Database}"));

// ---- Logging (console + rolling file in App_Data/logs) ------------------------------------
// HttpClient logging is kept at Warning so request headers (Authorization) can never end up in the log.
builder.Host.UseSerilog((_, cfg) => cfg
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.Hosting.Lifetime", LogEventLevel.Information)
    .MinimumLevel.Override("System.Net.Http", LogEventLevel.Warning)
    .WriteTo.Console()
    .WriteTo.File(Path.Combine(paths.Logs, "prodvisor-ai-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 30));

// ---- Secrets -------------------------------------------------------------------------------
// Data Protection is the non-Windows fallback for encrypting the API key (Windows uses DPAPI directly).
var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("ProdvisorAi.Demo")
    .PersistKeysToFileSystem(new DirectoryInfo(paths.Keys));
if (OperatingSystem.IsWindows())
    dataProtection.ProtectKeysWithDpapi(protectToLocalMachine: true);
builder.Services.AddSingleton<SecretProtector>();

// ---- Application services ------------------------------------------------------------------
builder.Services.AddScoped<SettingsService>();
builder.Services.AddScoped<TechnologyGenerator>();
builder.Services.AddSingleton<MockTechnologyProvider>();
builder.Services.AddSingleton<DxfDrawingConverter>();
builder.Services.AddSingleton<ProdvisorSource>();
builder.Services.AddScoped<ProdvisorImporter>();
builder.Services.AddSingleton<JobQueue>();
builder.Services.AddHostedService<JobWorker>();

builder.Services.AddHttpClient<OpenAiResponsesClient>(client =>
{
    client.BaseAddress = new Uri(builder.Configuration["OpenAI:BaseUrl"] ?? "https://api.openai.com/v1/");
    client.Timeout = TimeSpan.FromSeconds(builder.Configuration.GetValue("OpenAI:TimeoutSeconds", 180));
});

// ---- Upload limits (app-level check is Upload:MaxSizeMb; transport limit is a bit higher) -
var maxBody = (builder.Configuration.GetValue("Upload:MaxSizeMb", 20) + 5) * 1024L * 1024L;
builder.WebHost.ConfigureKestrel(k => k.Limits.MaxRequestBodySize = maxBody);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = maxBody);

var app = builder.Build();

// ---- Database: create + seed on first start (demo: no migrations) --------------------------
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.EnsureCreated();
    SchemaUpgrade.Apply(db);
    SeedData.Seed(db);

    // Machines + materials come from PRODVISOR. First start (or first start after an upgrade): import them
    // from the configured database, or from the embedded snapshot when no database is configured / reachable.
    if (!db.Machines.Any(m => m.ProdvisorId != null) || !db.Materials.Any())
    {
        var source = scope.ServiceProvider.GetRequiredService<ProdvisorSource>();
        var importer = scope.ServiceProvider.GetRequiredService<ProdvisorImporter>();
        ProdvisorData data;
        try { data = source.LoadAsync().GetAwaiter().GetResult(); }
        catch (Exception ex)
        {
            app.Logger.LogWarning("PRODVISOR database not reachable ({Error}) - using the embedded snapshot", ex.Message);
            data = ProdvisorSource.LoadSnapshot();
        }
        importer.ImportAsync(data).GetAwaiter().GetResult();
    }
}

// ---- HTTP pipeline -------------------------------------------------------------------------
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapSettingsEndpoints();
app.MapMachineEndpoints();
app.MapPromptEndpoints();
app.MapJobEndpoints();

app.Logger.LogInformation("PRODVISOR AI demo started. Data directory: {DataDir}", dataDir);
app.Run();
