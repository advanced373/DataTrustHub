using DataTrustHub.Features._Shared.Extensions;
using DataTrustHub.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddOpenApi();
// AddControllers used to register these implicitly; the endpoints rely on RequireAuthorization
builder.Services.AddAuthorization();
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddFeatures(builder.Configuration);

var app = builder.Build();

if (app.Environment.IsDevelopment())
    app.MapOpenApi();

app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();
app.MapFeatures();

app.Run();

// Required for WebApplicationFactory in integration tests
public partial class Program { }
