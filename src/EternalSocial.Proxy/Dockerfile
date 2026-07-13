# --- build ---
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/EternalSocial.Proxy/EternalSocial.Proxy.csproj src/EternalSocial.Proxy/
RUN dotnet restore src/EternalSocial.Proxy/EternalSocial.Proxy.csproj

COPY src/EternalSocial.Proxy/ src/EternalSocial.Proxy/
RUN dotnet publish src/EternalSocial.Proxy/EternalSocial.Proxy.csproj \
    -c Release -o /app/publish /p:UseAppHost=false

# --- runtime ---
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

COPY --from=build /app/publish .

# Route table + DataProtection keys live on the mounted volume.
RUN mkdir -p /app/data && chown $APP_UID /app/data

ENV ASPNETCORE_URLS=http://+:8080
ENV LITEDB_PATH=/app/data/eternalsocial.db
EXPOSE 8080
USER $APP_UID

ENTRYPOINT ["dotnet", "EternalSocial.Proxy.dll"]

HEALTHCHECK --interval=30s --timeout=3s --start-period=10s --retries=3 \
  CMD curl -f http://localhost:8080/_gateway/health || exit 1
