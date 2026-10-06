# ============================================================
# STAGE 1: BUILD
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build-env
WORKDIR /app

# Central Package Management.
COPY Directory.Packages.props .

# Copy csproj files for restore caching.
COPY src/InfraStream.Core/InfraStream.Core.csproj src/InfraStream.Core/
COPY src/InfraStream.CollectorEngine/InfraStream.CollectorEngine.csproj src/InfraStream.CollectorEngine/
COPY src/InfraStream.Host/InfraStream.Host.csproj src/InfraStream.Host/

RUN dotnet restore src/InfraStream.Host/InfraStream.Host.csproj

# Copy all sources.
COPY src/ src/

# Publish the host (pulls CollectorEngine as dependency).
RUN dotnet publish src/InfraStream.Host/InfraStream.Host.csproj \
    -c Release \
    -o /app/out \
    --no-restore

# ============================================================
# STAGE 2: RUNTIME
# ============================================================
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

RUN useradd --uid 10001 --create-home --shell /bin/bash appuser
USER appuser

COPY --from=build-env /app/out .

EXPOSE 5001
EXPOSE 5002

ENTRYPOINT ["dotnet", "InfraStream.Host.dll"]