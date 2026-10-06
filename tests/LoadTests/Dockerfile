# ============================================================
# STAGE 1: BUILD
# ============================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy Directory.Packages.props (Central Package Management).
COPY Directory.Packages.props .

# Copy the csproj and restore.
COPY tests/LoadTests/LoadTests.csproj tests/LoadTests/
RUN dotnet restore tests/LoadTests/LoadTests.csproj

# Copy the remaining sources.
COPY tests/LoadTests/ tests/LoadTests/

# Publish.
RUN dotnet publish tests/LoadTests/LoadTests.csproj \
    -c Release \
    -o /app/publish \
    --no-restore \
    /p:UseAppHost=false

# ============================================================
# STAGE 2: RUNTIME
# ============================================================
FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

RUN useradd --uid 10001 --create-home --shell /bin/bash appuser
USER appuser

COPY --from=build /app/publish .

VOLUME ["/app/reports"]

ENTRYPOINT ["dotnet", "LoadTests.dll"]
