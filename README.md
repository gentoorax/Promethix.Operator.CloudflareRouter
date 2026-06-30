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

For multi-tenant safety, direct service targets are restricted to the same namespace as the `TunnelPublicHostname` by default. Per-resource ingress service overrides are also disabled by default; ingress mode normally uses the operator's configured `ingressTargetUrl`.
Namespace hostname ownership policy is enabled by default. Namespaces must be annotated with the hostname suffixes they are allowed to claim, for example `edge.promethix.net/allowed-hostname-suffixes: apps.example.com, internal.example.com`.
For shared clusters, set `operator.allowedHostnameSuffixes` as an outer operator-wide allowlist as well. A hostname must satisfy both the operator-wide suffix allowlist and the namespace-specific suffix annotation before it will be accepted.
For shared clusters, tenant users should usually get namespace-scoped RBAC for `TunnelPublicHostname` in their own namespace rather than broad cluster access.

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
- [tenant-rbac.yaml](examples/tenant-rbac.yaml)
- [flux](examples/flux)
- [rate-limited-ingress-app.yaml](examples/rate-limited-ingress-app.yaml)

The examples are intentionally generic and use placeholder values. Replace hostnames, tunnel names, secrets, and ingress details for your cluster.

## Quick Start

This is the minimum setup for a first deployment.

Before you start:

1. Create and manage a Cloudflare Tunnel separately.
2. Know the tunnel name you want this operator to manage.
3. Have an ingress controller, or plan to use direct service targets.

Add the Helm repository:

```powershell
helm repo add promethix https://promethixlabs.github.io/charts
helm repo update
```

Create a namespace and Cloudflare Secret:

```powershell
kubectl create namespace cloudflare-tunnel-operator-system

kubectl create secret generic cloudflare-tunnel-operator `
  --namespace cloudflare-tunnel-operator-system `
  --from-literal=CLOUDFLARE_API_TOKEN=replace-me `
  --from-literal=CLOUDFLARE_ACCOUNT_ID=replace-me `
  --from-literal=CLOUDFLARE_TUNNEL_ID=replace-me
```

Cloudflare token requirement:

- read-only / dry-run: account-scoped `Cloudflare Tunnel Read`
- normal apply mode: account-scoped `Cloudflare Tunnel Write`

The operator manages an existing Cloudflare Tunnel configuration. It does not require Zone DNS permissions for its current behavior.

Install the operator:

```powershell
helm upgrade --install cloudflare-tunnel-operator `
  promethix/promethix-cloudflare-tunnel-operator `
  --namespace cloudflare-tunnel-operator-system `
  --create-namespace `
  --set image.repository=ghcr.io/promethixlabs/cloudflare-tunnel-operator `
  --set cloudflare.existingSecretName=cloudflare-tunnel-operator `
  --set operator.managedTunnelName=public-tunnel `
  --set operator.managedIngressClassName=traefik-cloudflare-tunnel `
  --set operator.ingressTargetUrl=https://traefik-cloudflare-tunnel.traefik.svc.cluster.local `
  --set operator.applyChanges=false
```

Create a test app:

- ingress-backed example: [examples/ingress-backed-app.yaml](examples/ingress-backed-app.yaml)
- direct-service example: [examples/direct-origin-app.yaml](examples/direct-origin-app.yaml)

For a first run, keep `operator.applyChanges=false`. That lets you confirm the CRs are valid and see planned behavior without writing Cloudflare config. After that, set `operator.applyChanges=true`.

If you use Flux instead of manual Helm, start with:

- [examples/flux/README.md](examples/flux/README.md)
- [examples/flux/helmrelease.yaml](examples/flux/helmrelease.yaml)

CRD usage notes are in [docs/crds.md](docs/crds.md).

## Manual Helm Deployment

If you want more control than the quick start, use the full Helm configuration below.

Add the public Helm repository:

```powershell
helm repo add promethix https://promethixlabs.github.io/charts
helm repo update
```

Charts are published from Git release tags. Release-candidate promotion creates prerelease chart tags such as `1.0.44-rc1`, and production promotion creates the stable release tag.
Stable chart versions are plain SemVer, for example `1.0.43`. Prerelease chart versions are only shown by Helm when using `--devel`.

Create a namespace and a Secret containing Cloudflare credentials:

```powershell
kubectl create namespace cloudflare-tunnel-operator-system

kubectl create secret generic cloudflare-tunnel-operator `
  --namespace cloudflare-tunnel-operator-system `
  --from-literal=CLOUDFLARE_API_TOKEN=replace-me `
  --from-literal=CLOUDFLARE_ACCOUNT_ID=replace-me `
  --from-literal=CLOUDFLARE_TUNNEL_ID=replace-me `
  --from-literal=CLOUDFLARE_ZONE_ID=replace-me
```

Annotate each tenant namespace with the hostname suffixes it is allowed to claim:

```powershell
kubectl annotate namespace my-apps `
  edge.promethix.net/allowed-hostname-suffixes=apps.example.com,internal.example.com
```

Install the chart:

```powershell
helm upgrade --install cloudflare-tunnel-operator `
  promethix/promethix-cloudflare-tunnel-operator `
  --namespace cloudflare-tunnel-operator-system `
  --create-namespace `
  --set image.repository=ghcr.io/promethixlabs/cloudflare-tunnel-operator `
  --set cloudflare.existingSecretName=cloudflare-tunnel-operator `
  --set operator.managedTunnelName=public-tunnel `
  --set operator.ingressTargetUrl=https://traefik-cloudflare-tunnel.traefik.svc.cluster.local `
  --set operator.allowedHostnameSuffixes=apps.example.com,internal.example.com `
  --set operator.applyChanges=false
```

Keep `operator.applyChanges=false` for the first run. The operator will plan reconciliation and update CRD status without writing Cloudflare tunnel config. Set it to `true` only after validating logs, status, and ownership behavior.

By default, the chart deploys the image tag matching the chart `appVersion`. Override `image.tag` only when you intentionally want a different image version.

## Optional Hardening

The operator can be deployed with a very small baseline, then tightened later.

Useful next steps for shared clusters:

- namespace hostname ownership policy
- operator-wide hostname suffix allowlist
- namespace-scoped RBAC for tenant users
- validating admission webhook
- restrictive ingress override handling
- cross-namespace direct targets disabled

For a stricter shared-cluster example, see:

- [examples/flux/helmrelease.shared-cluster.yaml](examples/flux/helmrelease.shared-cluster.yaml)
- [examples/tenant-rbac.yaml](examples/tenant-rbac.yaml)

The validating admission webhook is optional. For shared clusters it is a good next step once namespace hostname policy and any operator-wide suffix allowlist are in place. When enabled, supply a working cert-manager issuer name and keep `failurePolicy=Fail` only after confirming certificate issuance and webhook reachability in your cluster.
The operator keeps its normal HTTP management and health endpoint on port `8080` even when the webhook TLS listener is enabled on `8443`, so the standard liveness and readiness probes remain valid in both modes.
Use a dedicated webhook certificate secret, and when changing issuers prefer rotating by changing `webhook.certificate.secretName` rather than reusing an old secret issued by a different signer. The chart now derives the managed `Certificate` object identity from that secret name so a secret rotation creates a fresh cert-manager certificate resource instead of trying to patch an older one in place.

Example:

```powershell
helm upgrade --install cloudflare-tunnel-operator `
  promethix/promethix-cloudflare-tunnel-operator `
  --namespace cloudflare-tunnel-operator-system `
  --set webhook.enabled=true `
  --set webhook.certificate.issuerRef.name=platform-ca `
  --set webhook.failurePolicy=Fail
```

For shared clusters, the recommended ingress posture is:

- `operator.allowIngressServiceOverride=false`
- set `operator.ingressTargetUrl` to the approved shared ingress service
- if a CR supplies `spec.target.ingress.service`, it is accepted only when it resolves to that same configured shared ingress target

That preserves compatibility with explicit shared-ingress declarations while denying arbitrary ingress service overrides.

Per-hostname Cloudflare rate-limit reconciliation is also disabled by default. To enable it, set `operator.securityPolicies.enabled=true`. This is an advanced feature and requires additional Cloudflare zone/ruleset permissions beyond the basic tunnel-only token used for route reconciliation.
The `log` rate-limit action is treated as Enterprise-only and is rejected by default; enable `operator.allowEnterpriseOnlyRateLimitActions=true` only if your Cloudflare zone supports it.

To install from a local checkout instead, replace the chart reference with `./charts/promethix-cloudflare-tunnel-operator`.

For GitOps deployments, use the sample manifests in [examples/flux](examples/flux) as a starting point.

## Verifying Images

Container images are signed with cosign. The public verification key is published at:

```text
https://trust.promethix.dev/cosign/promethix-code-signing.pub
```

Verify an image by digest:

```powershell
curl.exe -fsSLO https://trust.promethix.dev/cosign/promethix-code-signing.pub

cosign verify `
  --key promethix-code-signing.pub `
  ghcr.io/promethixlabs/cloudflare-tunnel-operator@sha256:<digest>
```

Use immutable digests for verification rather than mutable tags such as `latest`.

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

Architecture notes are in [docs/architecture.md](docs/architecture.md).

## License

Licensed under the GNU General Public License v2. See `LICENSE`.
