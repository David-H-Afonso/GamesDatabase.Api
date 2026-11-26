# Runtime and image compatibility

Games Database API targets **.NET10 LTS** with Microsoft/EF10.0.15,
Swashbuckle10.3.1 and MIT SkiaSharp4.157.0. API and tests use `net10.0`;
container SDK/runtime stages use .NET10. Front CI uses Node22.

## Images

`ImageThumbnailEncoder` produces transparent, aspect-preserving static WebP at
quality90. Limits:40M decoded pixels,16M output pixels, requested dimensions2560
and produced dimensions8192. Invalid/oversized conversions follow the controller's
safe original/error fallback. These are per-conversion limits, not a load benchmark.

Animated GIFs remain original. Logo, Hero and Cover are independent. Disk/ETag
identity includes `skia-webp-v1-q90`, preventing reuse of an older encoder variant.
ImageSharp is not a runtime dependency.

## Verify

```sh
dotnet restore GamesDatabase.Api.sln -p:NuGetAuditMode=all -warnaserror:NU1902,NU1903,NU1904
dotnet build GamesDatabase.Api.sln --configuration Release --no-restore
dotnet test tests/GamesDatabase.Api.Tests/GamesDatabase.Api.Tests.csproj --configuration Release --no-build
```

An optional isolated-output run uses the same `--artifacts-path` for build/test.
Never point tests at working/production SQLite. The `Testing` host does not load
private env files. Windows encoder tests do not substitute for a Linux container
smoke on the intended architecture; verify native decoding/WebP in that final image.

## Startup and recovery

The runtime/image-library change itself adds no migration. Collection/onboarding
schema changes and older pending migrations still require review. Startup creates
the current model only for a genuinely empty DB; existing data uses migrations.

Before adopting a production image, agree the actual paths, consistent SQLite
backup with WAL handling, integrity/FK check, isolated restore and prior image
digest. Prefer a compatible image rollback retaining additive data. Down drops new
fields; restoring over newer writes needs an explicit reconciliation decision.

See [Collection contracts](COLLECTION_EXPERIENCE.md),
[Guided library](GUIDED_LIBRARY.md) and [Deployment/recovery](DEPLOYMENT.md).
