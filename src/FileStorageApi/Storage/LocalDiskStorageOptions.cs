namespace FileStorageApi.Storage;

/// <summary>Configuration for <see cref="LocalDiskFileStorage"/>, bound from appsettings.</summary>
public class LocalDiskStorageOptions
{
    public const string SectionName = "LocalDiskStorage";

    /// <summary>
    /// Root folder that holds all stored file bytes. Relative paths are resolved against
    /// the content root of the application.
    /// </summary>
    public string RootPath { get; set; } = "storage-data";

    /// <summary>Buffer size (in bytes) used when streaming copies to/from disk.</summary>
    public int CopyBufferSize { get; set; } = 81920;
}
