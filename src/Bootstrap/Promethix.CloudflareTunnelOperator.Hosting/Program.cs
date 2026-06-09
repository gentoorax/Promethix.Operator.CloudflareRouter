using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using k8s;
using Promethix.CloudflareTunnelOperator.Hosting;
using Promethix.CloudflareTunnelOperator.Hosting.Health;
using Promethix.CloudflareTunnelOperator.Hosting.Options;
using Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

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

builder.Services
    .AddOptions<KubernetesOperatorOptions>()
    .Bind(builder.Configuration.GetSection(KubernetesOperatorOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<KubernetesOperatorOptions>, KubernetesOperatorOptionsValidator>();

builder.Services.AddSingleton<IClock, SystemClock>();
builder.Services.AddSingleton<RouteReconciler>();
builder.Services.AddSingleton<OperatorState>();
builder.Services.AddSingleton<RouteIntentWorkQueue>();
builder.Services.AddSingleton<IKubernetes>(_ => KubernetesClientFactory.Create());
builder.Services.AddSingleton<IIngressTargetValidator, KubernetesIngressTargetValidator>();
builder.Services.AddSingleton<KubernetesTunnelPublicHostnameClient>();
builder.Services.AddSingleton<IClusterRouteIntentSource, KubernetesRouteIntentSource>();
builder.Services.AddSingleton<IManagedRouteOwnershipStore, KubernetesOwnershipStore>();
builder.Services.AddSingleton<IRouteIntentStatusUpdater, KubernetesRouteIntentStatusUpdater>();
builder.Services.AddHttpClient<ICloudflareTunnelRouteClient, CloudflareTunnelRouteClient>(
    (serviceProvider, httpClient) =>
    {
        var tunnelOptions = serviceProvider.GetRequiredService<IOptions<CloudflareTunnelOptions>>();
        httpClient.BaseAddress = new Uri("https://api.cloudflare.com/client/v4/");
        httpClient.DefaultRequestHeaders.Authorization =
            new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", tunnelOptions.Value.ApiToken);
    });
builder.Services.AddHostedService<RouteIntentWatchService>();
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
