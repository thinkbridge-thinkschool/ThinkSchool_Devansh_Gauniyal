# Day 5 Task 2 — Real Verification Output

All output below is copied verbatim from actual commands run on this machine (Apple Silicon Mac, Docker Desktop). Nothing here is invented.

## `docker publish` (arm64, corrected to the musl runtime identifier)

The first publish attempt used `--os linux --arch arm64`, which selects the glibc `linux-arm64` runtime identifier. The container crashed on startup (see "First attempt failure" below) because the base image `mcr.microsoft.com/dotnet/aspnet:10.0-alpine` is musl-based (Alpine), not glibc. The corrected, working publish command is:

```
$ dotnet publish day-5/task-2/QuotesApi/QuotesApi.csproj -r linux-musl-arm64 --self-contained false /t:PublishContainer

  Determining projects to restore...
  Restored /Users/devansh/thinkschool/day-5/task-2/QuotesApi/QuotesApi.csproj (in 3.72 sec).
  QuotesApi -> /Users/devansh/thinkschool/day-5/task-2/QuotesApi/bin/Release/net10.0/linux-musl-arm64/QuotesApi.dll
  QuotesApi -> /Users/devansh/thinkschool/day-5/task-2/QuotesApi/bin/Release/net10.0/linux-musl-arm64/publish/
  warning CONTAINER003: The property 'ContainerImageName' was set but is obsolete - please use 'ContainerRepository' instead.
  Building image 'quotes-api' with tags '0.1.0' on top of base image 'mcr.microsoft.com/dotnet/aspnet:10.0-alpine'.
  Pushed image 'quotes-api:0.1.0' to local registry via 'docker'.
```

## First attempt failure (glibc RID on a musl/Alpine base image) — for the record

```
$ docker run -d --name quotes-api-test -p 8080:8080 quotes-api:0.1.0
$ docker logs quotes-api-test
Unhandled exception. System.TypeInitializationException: The type initializer for 'Microsoft.Data.Sqlite.SqliteConnection' threw an exception.
 ---> System.DllNotFoundException: Unable to load shared library 'e_sqlite3' or one of its dependencies.
Error loading shared library ld-linux-aarch64.so.1: No such file or directory (needed by /app/libe_sqlite3.so)
   at SQLitePCL.SQLite3Provider_e_sqlite3.NativeMethods.sqlite3_libversion_number()
   ...
   at QuotesApi.Data.SeedData.EnsureSeeded(AppDbContext db)
```
Diagnosis: `ld-linux-aarch64.so.1` is the glibc dynamic linker; Alpine uses musl and has no such file. Fixed by publishing with `-r linux-musl-arm64` instead of `--os linux --arch arm64`.

## Second attempt failure (non-root user, unwritable working directory) — for the record

After fixing the RID, the native library loaded, but the container still exited:
```
$ docker logs quotes-api-test
[06:04:22 ERR] () An error occurred using the connection to database 'main' on server 'quotesapi.db'.
Unhandled exception. Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 14: 'unable to open database file'.
   at QuotesApi.Data.SeedData.EnsureSeeded(AppDbContext db)
```
Diagnosis (verified by exec-ing into the image):
```
$ docker run --rm --entrypoint sh quotes-api:0.1.0 -c "id; ls -la /app | head -5; touch /app/testwrite && echo WRITABLE || echo NOT_WRITABLE"
uid=1654(app) gid=1654(app) groups=1654(app),1654(app)
drwxr-xr-x 2 root root 4096 Aug 14 06:04 .
touch: /app/testwrite: Permission denied
NOT_WRITABLE

$ docker run --rm --entrypoint sh quotes-api:0.1.0 -c "ls -ld /tmp; touch /tmp/testwrite && echo TMP_WRITABLE"
drwxrwxrwt 2 root root 4096 Jun 13 16:39 /tmp
TMP_WRITABLE
```
Fixed by baking `<ContainerEnvironmentVariable Include="ConnectionStrings__QuotesApi" Value="Data Source=/tmp/quotesapi.db" />` into the csproj (container-only, does not affect local `dotnet run`).

## `docker images` (real, final)

```
IMAGE              ID             DISK USAGE   CONTENT SIZE
quotes-api:0.1.0   8514b80092ce   195MB        55.1MB
```

## `docker image inspect` — architecture proof

```
$ docker image inspect quotes-api:0.1.0 --format 'Architecture={{.Architecture}}\nOs={{.Os}}\nSize(bytes)={{.Size}}'
Architecture=arm64
Os=linux
Size(bytes)=55056960
```

## Final working container run

```
$ docker run -d --name quotes-api-test -p 8080:8080 quotes-api:0.1.0
80ec5066778d54ffe75f9c1062e2ef78d62943694c8dc3eec5d9ec0fb2b9975e

$ docker ps --filter name=quotes-api-test
CONTAINER ID   IMAGE              COMMAND                  CREATED          STATUS          PORTS                                         NAMES
80ec5066778d   quotes-api:0.1.0   "dotnet /app/QuotesA…"   17 seconds ago   Up 16 seconds   0.0.0.0:8080->8080/tcp, [::]:8080->8080/tcp   quotes-api-test
```

```
$ docker logs quotes-api-test
[06:05:47 INF] () Executed DbCommand (3ms) [Parameters=[], CommandType='Text', CommandTimeout='30']
PRAGMA journal_mode = 'wal';
... (schema creation and seed insert statements omitted for brevity, all succeeded) ...
[06:05:47 INF] () Now listening on: http://[::]:8080
[06:05:47 INF] () Application started. Press Ctrl+C to shut down.
[06:05:47 INF] () Hosting environment: Production
[06:05:47 INF] () Content root path: /app
[06:06:03 INF] (ccd3e3eba23ec898a1c81c8ff06756fa) HTTP GET /health responded 200 in 9.9286 ms
[06:06:04 INF] (b16530e74f7206517f964b25dc702f7c) HTTP GET / responded 200 in 0.6280 ms
```

```
$ curl -i http://localhost:8080/health
HTTP/1.1 200 OK
Content-Type: application/json; charset=utf-8
Date: Fri, 14 Aug 2026 06:06:23 GMT
Server: Kestrel
Transfer-Encoding: chunked

{"service":"QuotesApi","machineName":"80ec5066778d","architecture":"Arm64","utc":"2026-08-14T06:06:24.4011163Z"}
```

`machineName` (`80ec5066778d`) is the container's own generated hostname, not the Mac's -- proof this response came from inside the container. `architecture` (`Arm64`) matches the `docker image inspect` architecture above -- proof the container is genuinely running arm64, not under emulation.

```
$ curl -s http://localhost:8080/
{"message":"QuotesApi is running."}
```

## Teardown

```
$ docker stop quotes-api-test && docker rm quotes-api-test
```

This is a local-only container: no cloud resource was created, no cost was incurred. The `quotes-api:0.1.0` image remains in the local Docker image store until explicitly removed with `docker rmi quotes-api:0.1.0`.
