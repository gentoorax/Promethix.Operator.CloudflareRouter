using Microsoft.Extensions.Options;
using Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare;

namespace Promethix.CloudflareTunnelOperator.Hosting.Options;

internal sealed class CloudflareTunnelOptionsValidator : IValidateOptions<CloudflareTunnelOptions>
{
    public ValidateOptionsResult Validate(string? name, CloudflareTunnelOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var failures = new List<string>();

        if (string.IsNullOrWhiteSpace(options.AccountId))
        {
            failures.Add("CloudflareTunnel:AccountId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.TunnelId))
        {
            failures.Add("CloudflareTunnel:TunnelId is required.");
        }

        if (string.IsNullOrWhiteSpace(options.ApiToken))
        {
            failures.Add("CloudflareTunnel:ApiToken is required.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
