# File Storage API

A file storage and versioning API built to practice two things that come up constantly in
real backend work: designing a storage layer behind an abstraction so the physical backend
(disk today, a cloud blob store tomorrow) is swappable, and handling file uploads/downloads
as streams instead of loading whole files into memory.

## Architecture

**`IFileStorage` abstraction.** All file bytes are read and written through a single
interface — `SaveAsync`, `OpenReadAsync`, `DeleteAsync`, `ExistsAsync` — defined in
`Storage/IFileStorage.cs`. Controllers and the versioning service never touch the filesystem
directly; they depend only on this interface. The only implementation included is
`LocalDiskFileStorage`, which writes objects under a configured root folder, addressed by an
opaque `storageKey`. Because nothing above the interface knows *how* or *where* bytes are
stored, a future `AzureBlobFileStorage` or `S3FileStorage` implementing the same interface
could be dropped in and registered with a one-line change in `Program.cs`
(`AddSingleton<IFileStorage, ...>()`) — no controller, service, or EF Core code would change.

**Metadata vs. bytes are split.** EF Core + SQLite store *metadata only*: logical file name,
version number, content type, size, upload timestamp, and the `storageKey` that points at the
bytes. The bytes themselves live on disk under `storage-data/`. This separation is what makes
the storage backend swappable in the first place — the database schema doesn't care whether
`storageKey` resolves to a local path or a blob URL.

**Versioning.** A `FileEntry` is a logical file identified by a unique name (e.g. `"report"`).
Each upload against that name creates a new `FileVersion` row (`v1`, `v2`, `v3`, ...) rather
than overwriting the previous one; the next version number is computed as
`max(existing versions) + 1` inside `FileVersioningService.UploadAsync`. Every version keeps
its own storage key, size, content type, and timestamp, so any prior version stays downloadable
and the full history can be listed. Deleting a version removes just that version (and the
logical file itself if it was the last one); deleting a file removes all versions and their
bytes.

**Streaming, not buffering.** Uploads arrive as `IFormFile` and are copied straight to a
`FileStream` on disk via `Stream.CopyToAsync` in `LocalDiskFileStorage.SaveAsync` — the request
body is never materialized as a single `byte[]` or `MemoryStream`. Downloads work the same way
in reverse: `FilesController` returns a `FileStreamResult` backed by an open `FileStream`, so
ASP.NET Core streams the response directly from disk. Request size limits and Kestrel's max
body size are relaxed specifically so this scales to large files without hitting artificial
caps.

## Features

- `IFileStorage` abstraction with a `LocalDiskFileStorage` implementation, registered via DI
- Streamed upload and download — flat memory usage regardless of file size
- Automatic file versioning: same logical name -> new version, not an overwrite
- Full version history per file, with download of the latest or any specific version
- Metadata (name, content type, size, uploaded-at, version number, uploaded-by) in SQLite via EF Core
- Delete a whole file (all versions) or a single version
- Path-traversal guard on storage keys in `LocalDiskFileStorage`
- OpenAPI document via `Microsoft.AspNetCore.OpenApi` (`/openapi/v1.json` in Development)

## Endpoints

| Method | Route | Description |
|---|---|---|
| `POST` | `/api/files/{name}` | Upload a file as the next version of `{name}` (multipart form: `file`, optional `uploadedBy`) |
| `GET` | `/api/files` | List all logical files with their latest version summary |
| `GET` | `/api/files/{name}/versions` | List version history for `{name}`, newest first |
| `GET` | `/api/files/{name}` | Download the latest version of `{name}` |
| `GET` | `/api/files/{name}/versions/{version}` | Download a specific version |
| `DELETE` | `/api/files/{name}` | Delete `{name}` and all of its versions |
| `DELETE` | `/api/files/{name}/versions/{version}` | Delete a single version |

See `src/FileStorageApi/FileStorageApi.http` for runnable examples, including a working
multipart upload.

## How to run it

Requires the .NET 10 SDK.

```bash
cd src/FileStorageApi
dotnet restore
dotnet run
```

The API listens on `http://localhost:5279` (see `Properties/launchSettings.json`). On first
run it applies EF Core migrations automatically (creating `filestorage.db`) and creates the
`storage-data/` folder for uploaded bytes — no manual setup needed.

Try it with the included `.http` file (VS Code REST Client or Visual Studio / Rider's HTTP
client), or with curl:

```bash
curl -F "file=@sample-files/sample.txt" -F "uploadedBy=demo-user" \
  http://localhost:5279/api/files/report

curl http://localhost:5279/api/files/report/versions
curl -OJ http://localhost:5279/api/files/report
```

### Regenerating migrations

If you change the EF Core models, regenerate the migration from `src/FileStorageApi`:

```bash
dotnet ef migrations add <Name> -o Data/Migrations
```

Migrations apply automatically at startup, so no separate `dotnet ef database update` step is
required when running the app.

## Tech stack

ASP.NET Core Web API (net10.0, controllers) · EF Core 10 + SQLite for metadata · local-disk
implementation of a swappable `IFileStorage` abstraction for file bytes.
