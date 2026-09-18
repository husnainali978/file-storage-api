using FileStorageApi.Data;
using FileStorageApi.DTOs;
using FileStorageApi.Models;
using FileStorageApi.Storage;
using Microsoft.EntityFrameworkCore;

namespace FileStorageApi.Services;

/// <summary>Result of a download lookup: an open, readable stream plus the version metadata it belongs to.</summary>
public record FileDownload(Stream Content, FileVersion Version) : IAsyncDisposable
{
    // Stream implements IAsyncDisposable itself, so this just forwards to it.
    public ValueTask DisposeAsync() => Content.DisposeAsync();
}

/// <summary>
/// Orchestrates uploads/downloads/versioning/deletes by combining EF Core (metadata) with
/// <see cref="IFileStorage"/> (bytes). Controllers depend only on this service, so neither the
/// database schema nor the storage backend leaks into the HTTP layer.
/// </summary>
public class FileVersioningService
{
    private readonly AppDbContext _db;
    private readonly IFileStorage _storage;
    private readonly ILogger<FileVersioningService> _logger;

    public FileVersioningService(AppDbContext db, IFileStorage storage, ILogger<FileVersioningService> logger)
    {
        _db = db;
        _storage = storage;
        _logger = logger;
    }

    /// <summary>
    /// Saves <paramref name="content"/> as a new version of the logical file <paramref name="name"/>.
    /// Creates the logical file if it doesn't exist yet. The stream is copied straight through to
    /// the configured <see cref="IFileStorage"/> without being buffered in memory.
    /// </summary>
    public async Task<FileVersionDto> UploadAsync(
        string name,
        Stream content,
        string originalFileName,
        string? contentType,
        string? uploadedBy,
        CancellationToken cancellationToken = default)
    {
        var entry = await _db.FileEntries
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Name == name, cancellationToken);

        if (entry is null)
        {
            entry = new FileEntry
            {
                Id = Guid.NewGuid(),
                Name = name,
                CreatedAt = DateTimeOffset.UtcNow,
            };
            _db.FileEntries.Add(entry);
        }

        var nextVersion = entry.Versions.Count == 0 ? 1 : entry.Versions.Max(v => v.VersionNumber) + 1;
        var extension = Path.GetExtension(originalFileName);
        var storageKey = $"{entry.Id}/v{nextVersion}{extension}";

        // Write bytes first. Only record metadata once the bytes are safely persisted, so a
        // failed upload never leaves behind a "version" that points at nothing.
        long size;
        try
        {
            size = await _storage.SaveAsync(storageKey, content, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to persist bytes for {Name} storage key {StorageKey}", name, storageKey);
            throw;
        }

        var fileVersion = new FileVersion
        {
            Id = Guid.NewGuid(),
            FileEntryId = entry.Id,
            VersionNumber = nextVersion,
            OriginalFileName = originalFileName,
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            SizeBytes = size,
            UploadedAt = DateTimeOffset.UtcNow,
            StorageKey = storageKey,
            UploadedBy = uploadedBy,
        };

        _db.FileVersions.Add(fileVersion);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch
        {
            // Compensate: don't leave an orphaned blob on disk if the metadata write failed.
            await _storage.DeleteAsync(storageKey, cancellationToken);
            throw;
        }

        return ToDto(entry.Name, fileVersion);
    }

    public async Task<List<FileSummaryDto>> ListFilesAsync(CancellationToken cancellationToken = default)
    {
        var entries = await _db.FileEntries
            .Include(f => f.Versions)
            .OrderBy(f => f.Name)
            .ToListAsync(cancellationToken);

        return entries
            .Where(f => f.Versions.Count > 0)
            .Select(f =>
            {
                var latest = f.Versions.OrderByDescending(v => v.VersionNumber).First();
                return new FileSummaryDto(
                    f.Id,
                    f.Name,
                    latest.VersionNumber,
                    f.Versions.Count,
                    latest.SizeBytes,
                    latest.ContentType,
                    f.CreatedAt,
                    latest.UploadedAt);
            })
            .ToList();
    }

    /// <summary>Version history for a logical file, newest first. Returns null if the file doesn't exist.</summary>
    public async Task<List<FileVersionDto>?> ListVersionsAsync(string name, CancellationToken cancellationToken = default)
    {
        var entry = await _db.FileEntries
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Name == name, cancellationToken);

        if (entry is null)
        {
            return null;
        }

        return entry.Versions
            .OrderByDescending(v => v.VersionNumber)
            .Select(v => ToDto(entry.Name, v))
            .ToList();
    }

    public async Task<FileDownload?> DownloadLatestAsync(string name, CancellationToken cancellationToken = default)
    {
        var entry = await _db.FileEntries
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Name == name, cancellationToken);

        var latest = entry?.Versions.OrderByDescending(v => v.VersionNumber).FirstOrDefault();
        if (latest is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(latest.StorageKey, cancellationToken);
        return new FileDownload(stream, latest);
    }

    public async Task<FileDownload?> DownloadVersionAsync(string name, int versionNumber, CancellationToken cancellationToken = default)
    {
        var entry = await _db.FileEntries
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Name == name, cancellationToken);

        var version = entry?.Versions.FirstOrDefault(v => v.VersionNumber == versionNumber);
        if (version is null)
        {
            return null;
        }

        var stream = await _storage.OpenReadAsync(version.StorageKey, cancellationToken);
        return new FileDownload(stream, version);
    }

    /// <summary>Deletes a logical file and every version of it, including the underlying bytes.</summary>
    public async Task<bool> DeleteFileAsync(string name, CancellationToken cancellationToken = default)
    {
        var entry = await _db.FileEntries
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Name == name, cancellationToken);

        if (entry is null)
        {
            return false;
        }

        foreach (var version in entry.Versions)
        {
            await _storage.DeleteAsync(version.StorageKey, cancellationToken);
        }

        _db.FileEntries.Remove(entry); // cascades to FileVersions
        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Deletes a single version. If it was the last remaining version, the logical file itself
    /// is removed too.
    /// </summary>
    public async Task<bool> DeleteVersionAsync(string name, int versionNumber, CancellationToken cancellationToken = default)
    {
        var entry = await _db.FileEntries
            .Include(f => f.Versions)
            .FirstOrDefaultAsync(f => f.Name == name, cancellationToken);

        var version = entry?.Versions.FirstOrDefault(v => v.VersionNumber == versionNumber);
        if (entry is null || version is null)
        {
            return false;
        }

        await _storage.DeleteAsync(version.StorageKey, cancellationToken);
        _db.FileVersions.Remove(version);

        if (entry.Versions.Count == 1) // only the one we're removing
        {
            _db.FileEntries.Remove(entry);
        }

        await _db.SaveChangesAsync(cancellationToken);
        return true;
    }

    private static FileVersionDto ToDto(string name, FileVersion version) => new(
        version.FileEntryId,
        name,
        version.VersionNumber,
        version.OriginalFileName,
        version.ContentType,
        version.SizeBytes,
        version.UploadedAt,
        version.UploadedBy);
}
