namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed class RoutingOperatorOptions
{
    public const string SectionName = "RoutingOperator";

    public string OwnershipTag { get; set; } = "promethix-cloudflare-tunnel-operator";

    public int ReconciliationIntervalSeconds { get; set; } = 30;

    public bool ApplyChanges { get; set; }

    public string MutationMode { get; set; } = "Full";

    public bool StartupProtectionEnabled { get; set; } = true;

    public int MaxDeleteCount { get; set; } = 5;

    public int MaxDeletePercentage { get; set; } = 50;

    public bool SecurityPoliciesEnabled { get; set; }

    public bool AllowEnterpriseOnlyRateLimitActions { get; set; }

    public string ManagedByLabelValue { get; set; } = "promethix-cloudflare-tunnel-operator";
}
