using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Hosting;
using Promethix.CloudflareTunnelOperator.Hosting.Health;
using Promethix.CloudflareTunnelOperator.Hosting.Options;
using Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<RoutingOperatorOptions>()
    .Bind(builder.Configuration.GetSection(RoutingOperatorOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<RoutingOperatorOptions>, RoutingOperatorOptionsValidator>();

builder.Services
    .AddOptions<CloudflareTunnelOptions>()
    .Bind(builder.Configuration.GetSection(CloudflareTunnelOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<CloudflareTunnelOptions>, CloudflareTunnelOptionsValidator>();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<RouteReconciler>();
builder.Services.AddSingleton<OperatorState>();
builder.Services.AddSingleton<IClusterRouteIntentSource, ClusterRouteIntentSource>();
builder.Services.AddSingleton<ICloudflareTunnelRouteClient, CloudflareTunnelRouteClient>();
builder.Services.AddHostedService<OperatorWorker>();

builder.Services
    .AddHealthChecks()
    .AddCheck("live", () => HealthCheckResult.Healthy(), tags: ["live"])
    .AddCheck<OperatorReadinessHealthCheck>("ready", tags: ["ready"]);

var app = builder.Build();

app.MapGet("/", () => Results.Ok(new
{
    service = "Promethix.CloudflareTunnelOperator",
    status = "running",
}));

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live", StringComparer.Ordinal),
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready", StringComparer.Ordinal),
});

app.Run();
