using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Options;
using k8s;
using Promethix.CloudflareTunnelOperator.Hosting.Admission;
using Promethix.CloudflareTunnelOperator.Hosting;
using Promethix.CloudflareTunnelOperator.Hosting.Health;
using Promethix.CloudflareTunnelOperator.Hosting.Options;
using Promethix.CloudflareTunnelOperator.Hosting.Reconciliation;
using Promethix.CloudflareTunnelOperator.Routing.Application;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes;

var builder = WebApplication.CreateBuilder(args);

builder.Services
    .AddOptions<AdmissionWebhookOptions>()
    .Bind(builder.Configuration.GetSection(AdmissionWebhookOptions.SectionName))
    .ValidateOnStart();
builder.Services.AddSingleton<IValidateOptions<AdmissionWebhookOptions>, AdmissionWebhookOptionsValidator>();

var admissionWebhookOptions = builder.Configuration
    .GetSection(AdmissionWebhookOptions.SectionName)
    .Get<AdmissionWebhookOptions>() ?? new AdmissionWebhookOptions();

if (admissionWebhookOptions.Enabled)
{
    _ = builder.WebHost.ConfigureKestrel(options =>
    {
        options.ListenAnyIP(admissionWebhookOptions.Port, listenOptions =>
        {
            var certificate = X509Certificate2.CreateFromPemFile(
                admissionWebhookOptions.CertificatePath,
                admissionWebhookOptions.PrivateKeyPath);

            _ = listenOptions.UseHttps(certificate);
        });
    });
}

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
builder.Services.AddSingleton(_ => KubernetesClientFactory.Create());
builder.Services.AddSingleton<IIngressTargetValidator, KubernetesIngressTargetValidator>();
builder.Services.AddSingleton<IKubernetesNamespaceReader, KubernetesNamespaceReader>();
builder.Services.AddSingleton<IHostnameOwnershipValidator, KubernetesHostnameOwnershipValidator>();
builder.Services.AddSingleton<IManagedTunnelPublicHostnameValidator, ManagedTunnelPublicHostnameValidator>();
builder.Services.AddSingleton<KubernetesTunnelPublicHostnameClient>();
builder.Services.AddSingleton<TunnelPublicHostnameAdmissionService>();
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

if (admissionWebhookOptions.Enabled)
{
    _ = app.MapPost(admissionWebhookOptions.Path, async (
        AdmissionReview review,
        TunnelPublicHostnameAdmissionService admissionService,
        CancellationToken cancellationToken) =>
    {
        var response = await admissionService.ValidateAsync(review, cancellationToken).ConfigureAwait(false);
        return Results.Json(response);
    });
}

app.MapHealthChecks("/health/live", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("live", StringComparer.Ordinal),
});

app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    Predicate = registration => registration.Tags.Contains("ready", StringComparer.Ordinal),
});

app.Run();
