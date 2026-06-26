# Promethix Cloudflare Tunnel Operator

[![Build Status](https://drone.promethix.net/api/badges/PromethixLabs/Promethix.Operator.CloudflareRouter/status.svg)](https://drone.promethix.net/PromethixLabs/Promethix.Operator.CloudflareRouter)

Promethix Cloudflare Tunnel Operator is a Kubernetes operator for publishing cluster services through an existing Cloudflare Tunnel.

The current focus is HTTP and HTTPS public hostname management for remotely managed tunnels. The operator is intended to work with explicit cluster-declared intent, predictable ownership, and safe reconciliation.

## What it does

- watches `TunnelPublicHostname` resources
- reconciles public hostnames into Cloudflare Tunnel configuration
- supports ingress-backed publication through a dedicated Traefik path
- keeps a direct origin mode for workloads that should not traverse Traefik
- updates resource status and respects ownership when reconciling shared tunnel config

## Current model

The preferred path is ingress-backed:

- workloads use normal Kubernetes `Ingress`
- Traefik handles routing, middleware, and TLS
- the operator publishes the hostname through Cloudflare Tunnel
- for HTTPS ingress targets, the operator sets Cloudflare `originRequest.originServerName` to the public hostname so cloudflared can verify the Traefik certificate correctly

Direct origin publication is also supported for cases where going through ingress is not appropriate.

## Project layout

```text
src
|-- Modules
|   `-- Routing
|       |-- Promethix.CloudflareTunnelOperator.Routing.Application
|       |-- Promethix.CloudflareTunnelOperator.Routing.Domain
|       |-- Promethix.CloudflareTunnelOperator.Routing.Integrations.Cloudflare
|       `-- Promethix.CloudflareTunnelOperator.Routing.Integrations.Kubernetes
`-- Bootstrap
    `-- Promethix.CloudflareTunnelOperator.Hosting

tests
`-- Promethix.CloudflareTunnelOperator.Routing.Tests
```

## Examples

Example manifests are in [examples](examples):

- [ingress-backed-app.yaml](examples/ingress-backed-app.yaml)
- [direct-origin-app.yaml](examples/direct-origin-app.yaml)
- [flux](examples/flux)

The Flux example is intentionally generic and uses placeholder secrets. Replace values for your cluster, tunnel, and ingress controller.

## Manual Helm Deployment

Add the public Helm repository:

```powershell
helm repo add promethix https://promethixlabs.github.io/charts
helm repo update
```

Charts are published from release tags. Production promotion creates the release tag used for chart publishing.

Create a namespace and a Secret containing Cloudflare credentials:

```powershell
kubectl create namespace cloudflare-tunnel-operator-system

kubectl create secret generic cloudflare-tunnel-operator `
  --namespace cloudflare-tunnel-operator-system `
  --from-literal=CLOUDFLARE_API_TOKEN=replace-me `
  --from-literal=CLOUDFLARE_ACCOUNT_ID=replace-me `
  --from-literal=CLOUDFLARE_TUNNEL_ID=replace-me
```

Install the chart:

```powershell
helm upgrade --install cloudflare-tunnel-operator `
  promethix/promethix-cloudflare-tunnel-operator `
  --namespace cloudflare-tunnel-operator-system `
  --create-namespace `
  --set image.repository=ghcr.io/promethixlabs/cloudflare-tunnel-operator `
  --set image.tag=latest `
  --set cloudflare.existingSecretName=cloudflare-tunnel-operator `
  --set operator.managedTunnelName=public-tunnel `
  --set operator.ingressTargetUrl=https://traefik-cloudflare-tunnel.traefik.svc.cluster.local `
  --set operator.applyChanges=false
```

Keep `operator.applyChanges=false` for the first run. The operator will plan reconciliation and update CRD status without writing Cloudflare tunnel config. Set it to `true` only after validating logs, status, and ownership behavior.

To install from a local checkout instead, replace the chart reference with `./charts/promethix-cloudflare-tunnel-operator`.

For GitOps deployments, use the sample manifests in [examples/flux](examples/flux) as a starting point.

## Development

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/Bootstrap/Promethix.CloudflareTunnelOperator.Hosting
```

Health endpoints:

- `GET /health/live`
- `GET /health/ready`

## Status

This project is still under active development. The core reconciliation and Kubernetes integration are in place, but the operator is not yet feature-complete.

Design notes are in [docs/architecture.md](docs/architecture.md).

## License

Licensed under the GNU General Public License v2. See `LICENSE`.
