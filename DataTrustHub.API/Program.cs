using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Infrastructure;

// Liveness only: no database check, so probing it never wakes the paused database (ADR-0021)
const string HealthRoute = "/health";

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
// AddControllers used to register these implicitly; the endpoints rely on RequireAuthorization
builder.Services.AddAuthorization();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFeatures(builder.Configuration);
builder.Services.AddHealthChecks();

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapFeatures();
app.MapHealthChecks(HealthRoute).AllowAnonymous();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
