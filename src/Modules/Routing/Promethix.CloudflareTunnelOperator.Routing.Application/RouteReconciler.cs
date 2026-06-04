namespace Promethix.CloudflareTunnelOperator.Routing.Application;

public sealed class RouteReconciler(
    IClusterRouteIntentSource intentSource,
    ICloudflareTunnelRouteClient routeClient,
    IClock clock)
{
    public async Task<ReconciliationResult> ReconcileAsync(RoutingOperatorOptions options, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(options);

        var startedAt = clock.UtcNow;
        var intent = await intentSource.GetDesiredRoutesAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var actualRoutes = await routeClient.GetRoutesAsync(cancellationToken).ConfigureAwait(false);
            var plan = RoutePlanner.BuildPlan(intent.Routes, actualRoutes, options.OwnershipTag);

            if (options.ApplyChanges && plan.HasChanges && plan.Conflicts.Count == 0)
            {
                await routeClient.ApplyAsync(plan, cancellationToken).ConfigureAwait(false);
            }

            return new ReconciliationResult(
                startedAt,
                clock.UtcNow,
                intent,
                plan,
                options.ApplyChanges && plan.HasChanges && plan.Conflicts.Count == 0);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
#pragma warning disable CA1031
        catch (Exception ex)
#pragma warning restore CA1031
        {
            throw new ReconciliationFailedException(intent, ex);
        }
    }
}
