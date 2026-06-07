# Ops Notes

## Drone secrets

The Drone pipeline expects these repository or organization secrets:

- `harbor_robot_username`
- `harbor_robot_password`
- `harbor_ca_cert`
  - PEM-encoded Harbor CA certificate bundle used to verify the registry TLS certificate
  - Include the full issuing chain needed for `harbor.internal.promethix.net`
  - The secret may be stored either as literal PEM newlines or as escaped `\n`; the pipeline expands escaped newlines before writing the cert file

The Harbor registry is hard-coded in the pipeline as:

- `harbor.internal.promethix.net`

The image repository path is hard-coded in the pipeline as:

- `promethix/cloudflare-tunnel-operator`

This means published images are tagged as:

- `harbor.internal.promethix.net/promethix/cloudflare-tunnel-operator:<tag>`

## Image behavior

- Pushes to `main` always publish staging tags:
  - `<major>.<minor>.<revision>-staging.<buildnumber>`
  - `staging-latest`
- Drone promotion to `production` retags an existing staging image without rebuilding it:
  - `<major>.<minor>.<revision>.<buildnumber>`
  - `latest`
- The publish step uses Kaniko so image builds can run in untrusted Drone repositories without privileged mode.
- The Harbor CA certificate is passed to Kaniko with `--registry-certificate` so TLS verification still works against the internal registry.

## Build metadata

The pipeline passes build metadata into the Docker image as build arguments and OCI labels.

Supported build arguments:

- `BUILD_MAJOR`
- `BUILD_MINOR`
- `BUILD_REVISION`
- `BUILD_NUMBER`
- `BUILD_SEMVER`
- `BUILD_COMMIT_SHA`
- `BUILD_DISPLAY`
- `BUILD_DATE`

The runtime image exposes at least these labels for later promotion logic:

- `org.opencontainers.image.version`
- `org.opencontainers.image.revision`
- `org.promethix.build.number`

## Flux follow-on

The expected next step is to reference the published Harbor image from Flux manifests, with the Helm chart remaining in Git initially.
