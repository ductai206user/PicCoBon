# ==============================================================================
# SPIKE OS (ASP.NET Core 10 MVC) — Render Docker Multi-Stage Build (DemoMode)
# ==============================================================================
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Restore dependencies first for layer caching
COPY PickleballClub.csproj ./
RUN dotnet restore PickleballClub.csproj

# Copy source and publish Razor views + wwwroot static assets
COPY . .
RUN dotnet publish PickleballClub.csproj -c Release -o /app/publish /p:UseAppHost=false

# Final lightweight ASP.NET Core 10 runtime image
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

# Clear default ASPNETCORE_HTTP_PORTS so Render's dynamic $PORT (default 10000) binds cleanly
ENV ASPNETCORE_ENVIRONMENT=Production \
    ASPNETCORE_HTTP_PORTS= \
    PORT=10000 \
    DemoMode__Enabled=true \
    BackgroundWorkers__Enabled=false

COPY --from=build /app/publish .

EXPOSE 10000

USER $APP_UID
ENTRYPOINT ["dotnet", "PickleballClub.dll"]
