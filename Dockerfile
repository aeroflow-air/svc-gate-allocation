# syntax=docker/dockerfile:1

FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY nuget.config ./
COPY Directory.Build.props ./
COPY AeroFlow.GateAllocation.sln ./
COPY src/AeroFlow.GateAllocation/AeroFlow.GateAllocation.csproj src/AeroFlow.GateAllocation/
COPY tests/AeroFlow.GateAllocation.Tests/AeroFlow.GateAllocation.Tests.csproj tests/AeroFlow.GateAllocation.Tests/

# GitHub Packages requires auth even for public packages.
# docker build --secret id=nuget_auth_token,env=NUGET_AUTH_TOKEN -t <image> .
# Use a PAT (or GITHUB_TOKEN in CI) with read:packages. The token is applied on
# a tmpfs copy of nuget.config and is not written into an image layer.
RUN --mount=type=secret,id=nuget_auth_token \
    --mount=type=tmpfs,target=/tmp/nuget \
    cp nuget.config /tmp/nuget/nuget.config \
    && dotnet nuget update source aeroflow-air \
         --username USERNAME \
         --password "$(cat /run/secrets/nuget_auth_token)" \
         --store-password-in-clear-text \
         --configfile /tmp/nuget/nuget.config \
    && dotnet restore AeroFlow.GateAllocation.sln --configfile /tmp/nuget/nuget.config

COPY src/ src/
COPY tests/ tests/

RUN dotnet publish src/AeroFlow.GateAllocation/AeroFlow.GateAllocation.csproj \
    -c Release \
    -o /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_EnableDiagnostics=0

RUN adduser --disabled-password --gecos "" appuser && chown -R appuser /app
USER appuser

COPY --from=build /app/publish .

EXPOSE 8080
ENTRYPOINT ["dotnet", "AeroFlow.GateAllocation.dll"]
