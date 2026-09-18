using Microsoft.Extensions.Options;

namespace FileStorageApi.Storage;

/// <summary>
/// <see cref="IFileStorage"/> implementation that persists bytes as plain files on local disk,
/// rooted at <see cref="LocalDiskStorageOptions.RootPath"/>. Storage keys are treated as
/// relative paths under that root (forward slashes are normalized to the OS separator).
///
/// This is one possible backend. Because everything upstream depends only on
/// <see cref="IFileStorage"/>, swapping this out for e.g. an Azure Blob Storage or S3
/// implementation later is a matter of writing that class and changing one line in
/// Program.cs's DI registration — no controller, service, or EF Core code changes.
/// </summary>
public class LocalDiskFileStorage : IFileStorage
{
    private readonly string _rootPath;
    private readonly int _copyBufferSize;
    private readonly ILogger<LocalDiskFileStorage> _logger;

    public LocalDiskFileStorage(
        IOptions<LocalDiskStorageOptions> options,
        IHostEnvironment environment,
        ILogger<LocalDiskFileStorage> logger)
    {
        _logger = logger;
        var configuredRoot = options.Value.RootPath;
        _rootPath = Path.IsPathRooted(configuredRoot)
            ? configuredRoot
            : Path.Combine(environment.ContentRootPath, configuredRoot);
        _copyBufferSize = options.Value.CopyBufferSize;

        Directory.CreateDirectory(_rootPath);
    }

    public async Task<long> SaveAsync(string storageKey, Stream content, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        var directory = Path.GetDirectoryName(fullPath)
            ?? throw new InvalidOperationException($"Storage key '{storageKey}' does not resolve to a valid path.");
        Directory.CreateDirectory(directory);

        // Stream-to-stream copy in chunks: the file is never fully materialized in memory,
        // which is what lets this handle multi-gigabyte uploads on modest hardware.
        await using var destination = new FileStream(
            fullPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None,
            _copyBufferSize,
            useAsync: true);

        await content.CopyToAsync(destination, _copyBufferSize, cancellationToken);
        await destination.FlushAsync(cancellationToken);

        _logger.LogInformation("Saved {Bytes} bytes to {StorageKey}", destination.Length, storageKey);
        return destination.Length;
    }

    public Task<Stream> OpenReadAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (!File.Exists(fullPath))
        {
            throw new FileNotFoundException($"No stored object found for key '{storageKey}'.", fullPath);
        }

        Stream stream = new FileStream(
            fullPath,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            _copyBufferSize,
            useAsync: true);

        return Task.FromResult(stream);
    }

    public Task DeleteAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        var fullPath = ResolvePath(storageKey);
        if (File.Exists(fullPath))
        {
            File.Delete(fullPath);
            _logger.LogInformation("Deleted stored object {StorageKey}", storageKey);
        }

        // Local-disk housekeeping only: if the containing folder (e.g. a per-file-entry
        // directory) is now empty, remove it too so deleted files don't leave clutter behind.
        // This is not part of the IFileStorage contract — a blob-based implementation has no
        // equivalent concept of an empty directory.
        var directory = Path.GetDirectoryName(fullPath);
        var rootFull = Path.GetFullPath(_rootPath);
        if (!string.IsNullOrEmpty(directory)
            && !string.Equals(Path.GetFullPath(directory), rootFull, StringComparison.OrdinalIgnoreCase)
            && Directory.Exists(directory)
            && !Directory.EnumerateFileSystemEntries(directory).Any())
        {
            Directory.Delete(directory);
        }

        return Task.CompletedTask;
    }

    public Task<bool> ExistsAsync(string storageKey, CancellationToken cancellationToken = default)
    {
        return Task.FromResult(File.Exists(ResolvePath(storageKey)));
    }

    /// <summary>
    /// Resolves a storage key to an absolute path and guards against path traversal
    /// (e.g. a key containing "..") escaping the configured root folder.
    /// </summary>
    private string ResolvePath(string storageKey)
    {
        if (string.IsNullOrWhiteSpace(storageKey))
        {
            throw new ArgumentException("Storage key must not be empty.", nameof(storageKey));
        }

        var normalized = storageKey.Replace('/', Path.DirectorySeparatorChar);
        var combined = Path.GetFullPath(Path.Combine(_rootPath, normalized));
        var rootWithSeparator = Path.GetFullPath(_rootPath) + Path.DirectorySeparatorChar;

        if (!combined.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Storage key '{storageKey}' resolves outside the storage root.");
        }

        return combined;
    }
}
