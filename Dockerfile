# Usar la imagen oficial de .NET 9 como base
FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

# Usar la imagen SDK para compilar
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src
COPY ["GamesDatabase.Api.csproj", "."]
RUN dotnet restore "./GamesDatabase.Api.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "GamesDatabase.Api.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "GamesDatabase.Api.csproj" -c Release -o /app/publish

# Imagen final
FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Crear directorio para la base de datos
RUN mkdir -p /app/data

# Configurar variables de entorno para Docker
ENV ASPNETCORE_ENVIRONMENT=Production
ENV DatabaseSettings__DatabasePath=/app/data/gamesdatabase.db
ENV ASPNETCORE_URLS=http://+:80

ENTRYPOINT ["dotnet", "GamesDatabase.Api.dll"]