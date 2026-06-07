FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

ARG BUILD_MAJOR=0
ARG BUILD_MINOR=0
ARG BUILD_REVISION=0
ARG BUILD_NUMBER=0
ARG BUILD_SEMVER=0.0.0
ARG BUILD_COMMIT_SHA=unknown
ARG BUILD_DISPLAY=v0.0.0
ARG BUILD_DATE=unknown

COPY . .
RUN dotnet publish src/Bootstrap/Promethix.CloudflareTunnelOperator.Hosting/Promethix.CloudflareTunnelOperator.Hosting.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

ARG BUILD_MAJOR=0
ARG BUILD_MINOR=0
ARG BUILD_REVISION=0
ARG BUILD_NUMBER=0
ARG BUILD_SEMVER=0.0.0
ARG BUILD_COMMIT_SHA=unknown
ARG BUILD_DISPLAY=v0.0.0
ARG BUILD_DATE=unknown

LABEL org.opencontainers.image.title="Promethix Cloudflare Tunnel Operator"
LABEL org.opencontainers.image.description="Kubernetes operator for reconciling cluster-declared public hostnames into Cloudflare Tunnel configuration."
LABEL org.opencontainers.image.version="${BUILD_SEMVER}"
LABEL org.opencontainers.image.revision="${BUILD_COMMIT_SHA}"
LABEL org.opencontainers.image.created="${BUILD_DATE}"
LABEL org.promethix.build.major="${BUILD_MAJOR}"
LABEL org.promethix.build.minor="${BUILD_MINOR}"
LABEL org.promethix.build.revision="${BUILD_REVISION}"
LABEL org.promethix.build.number="${BUILD_NUMBER}"
LABEL org.promethix.build.display="${BUILD_DISPLAY}"

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "Promethix.CloudflareTunnelOperator.Hosting.dll"]
