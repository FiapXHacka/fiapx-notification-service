using FiapX.Notification.Api.Auth;
using FiapX.Notification.Api.Observability;
using FiapX.Notification.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddObservability();
builder.Services.AddProblemDetails();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddJwtAuthentication(builder.Configuration);

var app = builder.Build();

if (app.Configuration.GetValue<bool>("Database:MigrateOnStartup"))
{
    await app.Services.MigrateDatabaseAsync();
}

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();

await app.RunAsync();

/// <summary>Entry point, public so WebApplicationFactory can host the API in integration tests.</summary>
public partial class Program;
