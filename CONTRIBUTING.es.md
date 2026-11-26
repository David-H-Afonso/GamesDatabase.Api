# Contribuir a la API

[English](CONTRIBUTING.md) · [Instalación](DOCS/GETTING_STARTED.es.md)

Respeta las capas existentes: HTTP en controladores, reglas en servicios, transporte
en DTOs y persistencia en EF. Propiedad y transacciones deben quedar explícitas;
no añadas un repositorio redundante sobre EF.

## Comprobar

```sh
dotnet restore GamesDatabase.Api.sln -p:NuGetAuditMode=all -warnaserror:NU1902,NU1903,NU1904
dotnet build GamesDatabase.Api.sln --configuration Release --no-restore
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release --no-build
node --test scripts/versioning/core.test.mjs scripts/versioning/release.test.mjs
```

Los tests usan bases `Testing` aisladas y no cargan env. No uses bases personales/
producción ni credenciales reales. Un mock externo no certifica disponibilidad real.

## Esquema y contratos

```sh
dotnet tool restore
dotnet ef migrations add NombreDescriptivo
```

Usa EF10. No escribas migraciones a mano; incluye Designer/Snapshot generado.
Revisa Up/Down y prueba copias con integridad/FKs. Down/restaurar producción no
son rollback automático.

Las instalaciones vacías se inicializan al arrancar. Datos existentes usan sus
migraciones pendientes, no un baseline inventado. Acuerda copia con WAL y
restauración aislada antes de actualizar.

Actualiza DTO/servicio/Front, EN/ES y docs juntos. Prueba anónimo, normal/admin,
A/B, null/límites y conflictos. Errores públicos genéricos y logs sin secretos.
Conserva ceros, roles de imagen independientes y sincronización explícita.

## Revisión

Explica alcance, enfoque, comandos/resultados e impacto de recuperación. Usa
Conventional Commits y el [kit](scripts/versioning/README.md). Un hito de producto
usa `Release-As` explícito, no una incompatibilidad ficticia. No edites versiones/
tags publicados durante trabajo rutinario.

Versiona tests, migraciones EF y contratos útiles. No env/Development privados,
bases, exports, cachés, builds o informes personales. Comunica hallazgos sensibles
privadamente, sin publicar claves o datos de explotación. Se mantiene la GPL.
