# Day 5 Task 2 — Notes

## What SDK container publishing does, and why no Dockerfile is needed

.NET's own SDK (since .NET 7/8) can build a real, runnable OCI container image directly from `dotnet publish`, without a Dockerfile. Passing `/t:PublishContainer` tells the build to, after publishing the app normally, assemble a container image layer containing that published output on top of a chosen base image, and load it straight into the local Docker daemon under a tag you control. For the common case -- a plain ASP.NET Core app with no unusual OS packages or multi-stage build logic -- this replaces the Dockerfile entirely: no `FROM`, no `COPY`, no `ENTRYPOINT` to hand-write and keep in sync with the project.

## The csproj properties used, and what each one controls

```xml
<ContainerImageName>quotes-api</ContainerImageName>
<ContainerImageTag>0.1.0</ContainerImageTag>
<ContainerBaseImage>mcr.microsoft.com/dotnet/aspnet:10.0-alpine</ContainerBaseImage>
<ContainerEnvironmentVariable Include="ConnectionStrings__QuotesApi" Value="Data Source=/tmp/quotesapi.db" />
```
- `ContainerImageName` -- the repository name the built image is tagged with (`quotes-api`). The .NET 10 SDK printed `warning CONTAINER003` saying this property is obsolete in favor of `ContainerRepository`; it still works (the publish succeeded and the image was named correctly), so it was left exactly as specified rather than changed unnecessarily.
- `ContainerImageTag` -- the tag (`0.1.0`), giving the final image reference `quotes-api:0.1.0`.
- `ContainerBaseImage` -- the base image the app is layered on top of. Verified before use: `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` is a real, currently published tag with genuine `linux/amd64`, `linux/arm/v7`, and `linux/arm64` manifest variants (checked via `docker manifest inspect`).
- `ContainerEnvironmentVariable` -- bakes an environment variable into the image itself. This one was required, not optional: the official aspnet image runs as a non-root user, which can't write to its own `/app` working directory, so SQLite couldn't create its database file there. `/tmp` is world-writable in this image (verified by exec-ing into it), so this variable redirects the connection string there -- but only inside the container; local `dotnet run` still uses the ordinary relative path from `appsettings.json` and is unaffected.

## Why arm64 was built instead of the x64 in the task text

The task text says `--os linux --arch x64`, but my Mac's hardware is Apple Silicon (`uname -m` reports `arm64`). Building `arm64` instead means the container's own CPU instructions match the Mac's actual processor, so it runs natively. Had I built `x64` and run it on this Mac, Docker Desktop would have to emulate an Intel/AMD CPU in software (via Rosetta) just to run it -- noticeably slower, and it would defeat the entire point of this exercise, which is to prove genuine architecture with the `/health` endpoint. The `x64` command below is documented for completeness, for deploying to actual x64 infrastructure (most cloud VMs and Azure Container Apps nodes today), not for running locally on this machine.

## Both publish commands

Local (Apple Silicon, what was actually run):
```
dotnet publish day-5/task-2/QuotesApi/QuotesApi.csproj -r linux-musl-arm64 --self-contained false /t:PublishContainer
```
Note this uses `-r linux-musl-arm64`, not `--os linux --arch arm64`. The first attempt with `--os linux --arch arm64` selected the glibc `linux-arm64` runtime identifier, which bundles a `libe_sqlite3.so` built against glibc's dynamic linker (`ld-linux-aarch64.so.1`). Alpine uses musl, not glibc, and has no such linker -- the container crashed with `DllNotFoundException` on startup. Explicitly requesting `linux-musl-arm64` selects the musl-compatible native SQLite library instead, which is what actually matches an Alpine base image.

x64 deployment target (not run here, documented for reference):
```
dotnet publish day-5/task-2/QuotesApi/QuotesApi.csproj -r linux-musl-x64 --self-contained false /t:PublishContainer
```
Same musl reasoning applies for x64 against this same Alpine base image.

## Why alpine produces a smaller image -- real measured sizes

Alpine Linux uses musl libc and a minimal busybox userland instead of a full Debian userland, so the base image itself is much smaller. Measured directly on this machine (`docker image inspect`, arm64, bytes):
- `mcr.microsoft.com/dotnet/aspnet:10.0` (Debian-based, default): **92,308,978 bytes** (~92.3 MB)
- `mcr.microsoft.com/dotnet/aspnet:10.0-alpine`: **51,887,286 bytes** (~51.9 MB)

That's about 43.8% smaller for the Alpine base alone. The final built app image, `quotes-api:0.1.0`, measured **55,056,960 bytes** (~55.1 MB content size, 195MB disk usage per `docker images`) -- close to the Alpine base plus roughly 3MB of the app's own published output.

## How `/health` proves the response came from inside the container

```json
{"service":"QuotesApi","machineName":"80ec5066778d","architecture":"Arm64","utc":"2026-08-14T06:06:24.4011163Z"}
```
- `machineName` is `80ec5066778d` -- a container-generated hostname, not the Mac's real hostname (`devanshs-MacBook-Air`, confirmed separately when the same endpoint was hit locally outside any container). Seeing this value proves the HTTP response was produced by the process running inside the container, not a local process on the Mac.
- `architecture` is `Arm64` -- matching `docker image inspect`'s `Architecture=arm64` for the built image. This proves the container is genuinely executing arm64 instructions natively, not running an x64 image under emulation.

## Teardown commands

```
docker stop quotes-api-test && docker rm quotes-api-test
```
This is a local-only container: no cloud resource was created and no cost was incurred. The `quotes-api:0.1.0` image itself stays cached in the local Docker image store until explicitly removed with `docker rmi quotes-api:0.1.0`.
