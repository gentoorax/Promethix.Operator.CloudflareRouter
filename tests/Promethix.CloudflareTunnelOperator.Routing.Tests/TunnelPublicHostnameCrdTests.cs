using FluentAssertions;

namespace Promethix.CloudflareTunnelOperator.Routing.Tests;

public sealed class TunnelPublicHostnameCrdTests
{
    [Fact]
    public void CrdShouldContainCoreAdmissionValidationRules()
    {
        var repositoryRoot = ResolveRepositoryRoot();
        var crdPath = Path.Combine(
            repositoryRoot,
            "charts",
            "promethix-cloudflare-tunnel-operator",
            "crds",
            "edge.promethix.net_tunnelpublichostnames.yaml");

        var crd = File.ReadAllText(crdPath);

        _ = crd.Should().Contain("Wildcard hostnames are not supported.");
        _ = crd.Should().Contain("Specify either spec.target or legacy spec.origin, but not both.");
        _ = crd.Should().Contain("spec.target.ingress is required when spec.target.mode is ingress.");
        _ = crd.Should().Contain("spec.target.direct must specify either service or url, but not both.");
    }

    private static string ResolveRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "Promethix.CloudflareTunnelOperator.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
               ?? throw new InvalidOperationException("Repository root could not be resolved for CRD validation tests.");
    }
}
