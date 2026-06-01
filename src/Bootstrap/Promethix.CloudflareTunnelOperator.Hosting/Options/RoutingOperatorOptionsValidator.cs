using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Application;

namespace Promethix.CloudflareTunnelOperator.Hosting.Options;

internal sealed class RoutingOperatorOptionsValidator : IValidateOptions<RoutingOperatorOptions>
{
    public ValidateOptionsResult Validate(string? name, RoutingOperatorOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.OwnershipTag))
        {
            failures.Add("RoutingOperator:OwnershipTag is required.");
        }

        if (options.ReconciliationIntervalSeconds <= 0)
        {
            failures.Add("RoutingOperator:ReconciliationIntervalSeconds must be greater than zero.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
