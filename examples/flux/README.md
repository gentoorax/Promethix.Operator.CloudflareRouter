# Flux Example

These manifests show one way to deploy the operator with Flux using the Helm chart from this Git repository.

They are intentionally generic. Replace:

- Cloudflare account, tunnel, and token values
- namespace names
- ingress target service URL
- image tag
- Git branch or tag

Files:

- `namespace.yaml`
- `cloudflare-secret.example.yaml`
- `source.yaml`
- `helmrelease.yaml`
- `kustomization.yaml`

Do not commit real Cloudflare credentials in plain text. Use SOPS, External Secrets, Vault Secrets Operator, or another secret management approach for production.
