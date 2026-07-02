using FluentAssertions;
using System.Diagnostics;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class HelmChartRenderingTests
{
    [Fact]
    public void MultiZoneSecurityPolicyConfigShouldRenderInlineZoneMappingsWithoutLegacyZoneSecretRef()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var chartPath = Path.Combine(repositoryRoot, "charts", "promethix-cloudflare-tunnel-operator");
        var rendered = RenderChart(
            repositoryRoot,
            chartPath,
            [
                "operator.securityPolicies.enabled=true",
                "cloudflare.zoneIdKey=",
                "cloudflare.zoneMappings[0].hostnameSuffix=promethix.net",
                "cloudflare.zoneMappings[0].zoneId=zone-promethix-net",
                "cloudflare.zoneMappings[1].hostnameSuffix=grid53.io",
                "cloudflare.zoneMappings[1].zoneId=zone-grid53-io",
            ]);

        _ = rendered.Should().Contain("name: CloudflareTunnel__ZoneMappings__0__HostnameSuffix");
        _ = rendered.Should().Contain("value: \"promethix.net\"");
        _ = rendered.Should().Contain("name: CloudflareTunnel__ZoneMappings__0__ZoneId");
        _ = rendered.Should().Contain("value: \"zone-promethix-net\"");
        _ = rendered.Should().Contain("name: CloudflareTunnel__ZoneMappings__1__HostnameSuffix");
        _ = rendered.Should().Contain("value: \"grid53.io\"");
        _ = rendered.Should().Contain("name: CloudflareTunnel__ZoneMappings__1__ZoneId");
        _ = rendered.Should().Contain("value: \"zone-grid53-io\"");
        _ = rendered.Should().NotContain("name: CloudflareTunnel__ZoneId");
        _ = rendered.Should().NotContain("key: \"\"");
    }

    private static string ResolveRepositoryRoot()
    {
        return Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
    }

    private static string RenderChart(string workingDirectory, string chartPath, IReadOnlyList<string> setArguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "helm",
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        psi.ArgumentList.Add("template");
        psi.ArgumentList.Add("chart-under-test");
        psi.ArgumentList.Add(chartPath);

        foreach (var argument in setArguments)
        {
            psi.ArgumentList.Add("--set");
            psi.ArgumentList.Add(argument);
        }

        using var process = Process.Start(psi);
        _ = process.Should().NotBeNull();
        var runningProcess = process ?? throw new InvalidOperationException("Failed to start helm template process.");

        var standardOutput = runningProcess.StandardOutput.ReadToEnd();
        var standardError = runningProcess.StandardError.ReadToEnd();
        runningProcess.WaitForExit();

        _ = runningProcess.ExitCode.Should().Be(
            0,
            $"helm template should succeed for multi-zone rendering validation. stderr: {standardError}");

        return standardOutput;
    }
}
