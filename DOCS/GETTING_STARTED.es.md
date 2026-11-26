# Ejecutar la API localmente

[English](GETTING_STARTED.md)

Requiere SDK .NET 10 y Front 3.0 compatible. El recorrido pareado está en el
[repositorio del Front](https://github.com/David-H-Afonso/GamesDatabase.Front).

1. Restaura/build/test de la solución según el README.
2. Configura `JWT_SECRET_KEY` privada y robusta, `JWT_ISSUER`/`JWT_AUDIENCE`
   deliberados, `GAMESDATABASE_DB_PATH` explícita y `ExportSettings__DefaultExportPath`
   privado. Define `HOUSEHOLD_REDIRECT_URIS` con callbacks exactos separados por
   comas. El arranque exige esta lista; sin Household reserva
   `https://household-client.invalid/callback`, no una URL permisiva. Cambia
   `HOUSEHOLD_CLIENT_ID` solo si también cambia el identificador de cliente.
3. Elige escucha loopback y origen Front explícito. Los secretos no van al
   navegador ni a `.env*`/Development versionados.
4. Arranca la API:

```sh
dotnet run --project GamesDatabase.Api.csproj --no-launch-profile --urls http://127.0.0.1:5011
```

5. Revisa `/health` y versión 3.0, después arranca el Front.
6. Entra con el administrador inicial y establece contraseña antes de ampliar
   acceso. La guía es opcional; los ejemplos requieren otra aceptación explícita.

## Inicializar SQLite

En un archivo realmente vacío, el arranque crea el modelo EF y registra su
baseline. Las migraciones históricas parten del esquema original: un EF
database-update directo no es la inicialización de una instalación nueva.

Tablas/historial existentes no se consideran una base vacía; se aplican sus
migraciones pendientes reales. Antes de actualizar datos existentes, acuerda
copia consistente con WAL y restauración aislada. No ejecutes Down ni restaures
sobre cambios posteriores automáticamente.

## Herramientas de desarrollo

`dotnet tool restore` obtiene el CLI EF10 fijado por el repositorio. Genera las
migraciones con él, revisa Designer/Snapshot y prueba solo en bases aisladas.
Build/test no necesita el CLI EF.

El proyecto de tests usa `Testing`, no carga env y crea bases descartables. No
uses datos personales ni credenciales reales para corregir fixtures. Consulta
[Contribuir](../CONTRIBUTING.es.md) y [Recuperación](DEPLOYMENT.md).
