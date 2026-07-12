# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore first (csproj only) for layer caching. Restoring the Server pulls in
# the Client and Shared project references.
COPY src/EternalReddit.Shared/EternalReddit.Shared.csproj src/EternalReddit.Shared/
COPY src/EternalReddit.Client/EternalReddit.Client.csproj src/EternalReddit.Client/
COPY src/EternalReddit.Server/EternalReddit.Server.csproj src/EternalReddit.Server/
RUN dotnet restore src/EternalReddit.Server/EternalReddit.Server.csproj

# Build + publish the host (which bundles the WASM client's static assets).
COPY src/ src/
RUN dotnet publish src/EternalReddit.Server/EternalReddit.Server.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# curl for the container HEALTHCHECK.
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# LiteDB writes to /app/data (mounted as a volume); make it writable by the
# non-root app user.
RUN mkdir -p /app/data && chown $APP_UID /app/data

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "EternalReddit.Server.dll"]

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1
