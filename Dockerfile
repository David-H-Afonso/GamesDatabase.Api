# Build stage
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

# Copy csproj and restore dependencies
COPY GamesDatabase.Api.csproj .
RUN dotnet restore GamesDatabase.Api.csproj

# Copy everything else and build
COPY . .
RUN dotnet publish GamesDatabase.Api.csproj -c Release -o /app/publish --no-restore

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

# Install curl for healthcheck
RUN apt-get update && apt-get install -y curl && rm -rf /var/lib/apt/lists/*

# Create directories for data persistence
RUN mkdir -p /app/data
RUN mkdir -p /app/exports

# Copy published app
COPY --from=build /app/publish .

# Environment variables for configuration
ENV ASPNETCORE_URLS=http://+:8080
ENV ASPNETCORE_ENVIRONMENT=Production
ENV ASPNETCORE_ALLOWEDHOSTS=*
ENV DatabaseSettings__DatabasePath=/app/data/gamesdatabase.db
ENV ExportSettings__DefaultExportPath=/app/exports

# Configure volumes for data persistence
VOLUME ["/app/data", "/app/exports"]

# Expose port
EXPOSE 8080

# Health check
HEALTHCHECK --interval=30s --timeout=3s --start-period=15s --retries=3 \
  CMD curl -f http://localhost:8080/health || exit 1

ENTRYPOINT ["dotnet", "GamesDatabase.Api.dll"]