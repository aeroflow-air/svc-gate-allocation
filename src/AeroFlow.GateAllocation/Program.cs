using System.Reflection;
using AeroFlow.GateAllocation.Messaging;
using AeroFlow.GateAllocation.Storage;
using Microsoft.OpenApi;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// Structured logging: use the host defaults (console JSON in containers when
// configured via environment). Prefer composing here over a shared logging package.
builder.Logging.ClearProviders();
builder.Logging.AddConsole();

builder.Services.AddControllers();
builder.Services.AddProblemDetails();
builder.Services.AddHealthChecks();

// API docs: Swashbuckle generates the OpenAPI document (net8 has no built-in generator; that arrived in .NET 9),
// Scalar renders it at /scalar. Gated by ApiDocs:Enabled, which is true in appsettings.Development.json and
// false by default, so Production never exposes the docs unless someone opts in via config.
var apiDocsEnabled = builder.Configuration.GetValue("ApiDocs:Enabled", false);
if (apiDocsEnabled)
{
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(options =>
    {
        options.SwaggerDoc("v1", new OpenApiInfo
        {
            Title = "AeroFlow Gate Allocation API",
            Version = "v1",
            Description = "Gate planning for AeroFlow: list gates, allocate flights to gates (or let the planner pick), reassign, retime, release, and close or reopen gates.",
        });
        var xml = Path.Combine(AppContext.BaseDirectory, $"{Assembly.GetExecutingAssembly().GetName().Name}.xml");
        if (File.Exists(xml))
        {
            options.IncludeXmlComments(xml);
        }
    });
}

// Edges: the clock and the store are the only side effects; the domain stays pure.
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IGatePlanStore>(sp =>
    new InMemoryGatePlanStore(DemoGatePlan.Create(sp.GetRequiredService<TimeProvider>().GetUtcNow())));

// Flight events (svc-gate-allocation#4): react to the simulator on the flight-events topic. Only wired when a
// "messaging" connection string exists (the aeroflow-local AppHost supplies it via WithReference), so a plain
// `dotnet run`, the container and the tests still start without Service Bus.
builder.Services.Configure<FlightEventsOptions>(builder.Configuration.GetSection(FlightEventsOptions.SectionName));
builder.Services.AddSingleton<GateEventHandler>();
if (!string.IsNullOrWhiteSpace(builder.Configuration.GetConnectionString(FlightEventsOptions.ConnectionName)))
{
    builder.AddAzureServiceBusClient(FlightEventsOptions.ConnectionName);
    builder.Services.AddHostedService<FlightEventsProcessor>();
}

// ---------------------------------------------------------------------------
// OpenTelemetry stub — enable when the squad wires an exporter (OTLP / App Insights).
// Keep this commented until you have somewhere to send traces/metrics; no dead deps.
//
// builder.Services.AddOpenTelemetry()
//     .WithTracing(t => t
//         .AddAspNetCoreInstrumentation()
//         .AddHttpClientInstrumentation()
//         // .AddOtlpExporter()
//     )
//     .WithMetrics(m => m
//         .AddAspNetCoreInstrumentation()
//         .AddRuntimeInstrumentation()
//         // .AddOtlpExporter()
//     );
// ---------------------------------------------------------------------------

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();

if (!app.Environment.IsDevelopment())
{
    app.UseHttpsRedirection();
}

app.MapHealthChecks("/health");
app.MapControllers();

if (apiDocsEnabled)
{
    app.UseSwagger(options => options.RouteTemplate = "openapi/{documentName}.json");
    app.MapScalarApiReference(options => options.WithTitle("AeroFlow Gate Allocation API"));
}

app.Run();

// Expose for WebApplicationFactory in tests.
public partial class Program;
