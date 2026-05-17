using MudBlazor.Services;
using WorkSphere.Components;
using WorkSphere.Services;
using WorkSphere.Data;

DapperTypeHandlers.Register();

var builder = WebApplication.CreateBuilder(args);

// Add MudBlazor services
builder.Services.AddMudServices();

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents();

builder.Services.AddScoped<WorkLogService>();
builder.Services.AddScoped<MigrationService>();

var app = builder.Build();

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
            Console.WriteLine("Database schema initialized successfully.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Critical: Database initialization failed: {ex.Message}");
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

app.Run();
