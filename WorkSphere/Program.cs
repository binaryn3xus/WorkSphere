using MudBlazor.Services;
using WorkSphere.Components;
using WorkSphere.Services;
using WorkSphere.Data;
using Serilog;

DapperTypeHandlers.Register();

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    // Remove default logging providers to prevent duplicate logs
    builder.Logging.ClearProviders();

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .ReadFrom.Configuration(context.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext());

    // Add MudBlazor services
    builder.Services.AddMudServices();

    // Add services to the container.
    builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();

    builder.Services.AddScoped<WorkLogService>();
    builder.Services.AddScoped<MigrationService>();
    builder.Services.AddScoped<WorkSphere.Tools.LogAuditTool>();

    var app = builder.Build();

    // Check for --audit flag
    if (args.Contains("--audit"))
    {
        using (var scope = app.Services.CreateScope())
        {
            var auditTool = scope.ServiceProvider.GetRequiredService<WorkSphere.Tools.LogAuditTool>();
            await auditTool.RunAuditAsync();
        }
        return;
    }

    app.UseSerilogRequestLogging();

    // Initialize Database
    using (var scope = app.Services.CreateScope())
    {
        var config = scope.ServiceProvider.GetRequiredService<IConfiguration>();
        var connectionString = config.GetConnectionString("DefaultConnection") ?? "";
        
        if (!string.IsNullOrEmpty(connectionString))
        {
            try 
            {
                await SchemaInitializer.EnsureSchemaAsync(connectionString);
                Log.Information("Database schema initialized successfully.");
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Database initialization failed.");
            }
        }
    }

    // Configure the HTTP request pipeline.
    if (!app.Environment.IsDevelopment())
    {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        // The default HSTS value is 30 days. You may want to change this for production scenarios, see https://aka.ms/aspnetcore-hsts.
        app.UseHsts();
    }

    app.UseHttpsRedirection();


    app.UseAntiforgery();

    app.MapStaticAssets();
    app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

    Log.Information("Starting WorkSphere web host");
    app.Run();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
