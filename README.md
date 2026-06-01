# Promethix Cloudflare Tunnel Operator

`Promethix.CloudflareTunnelOperator` is a .NET 10 Kubernetes-oriented control-plane service for reconciling cluster-declared public hostname intent into Cloudflare Tunnel route configuration.

The first scaffold focuses on one narrow responsibility:

- reconcile explicit hostname-to-origin intent
- target an existing remotely managed Cloudflare Tunnel
- manage only routes owned by this operator instance
- stay container-friendly and ready for Kubernetes deployment

## Solution Shape

```text
src
|-- Modules
|   `-- Routing
|       |-- Promethix.CloudflareTunnelOperator.Routing.Application
|       |-- Promethix.CloudflareTunnelOperator.Routing.Domain
|       `-- Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare
`-- Bootstrap
    `-- Promethix.CloudflareTunnelOperator.Hosting

tests
`-- Promethix.CloudflareTunnelOperator.Routing.Tests
```

## Current Scope

The scaffold includes:

- explicit domain types for managed public hostname routes
- reconciliation planning that respects operator ownership
- a hosted control loop with health checks and structured logging
- configuration objects and startup validation
- a stub Cloudflare adapter and a placeholder Kubernetes intent source
- Docker and Kubernetes deployment assets

The scaffold does not yet implement:

- real CRD models or watch registration
- live Cloudflare API calls
- conflict resolution beyond ownership filtering
- status publishing back into the cluster

## Running Locally

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Bootstrap/Promethix.CloudflareTunnelOperator.Hosting
```

Health endpoints:

- `GET /health/live`
- `GET /health/ready`

## Notes

Further architecture notes are in [docs/architecture.md](/c:/Source/Git/Promethix.Operator.CloudfareRouter/docs/architecture.md).
