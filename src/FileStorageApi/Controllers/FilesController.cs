using FileStorageApi.DTOs;
using FileStorageApi.Services;
using Microsoft.AspNetCore.Mvc;

namespace FileStorageApi.Controllers;

/// <summary>
/// Upload, download, version-history, and delete endpoints for stored files. All logic lives
/// in <see cref="FileVersioningService"/>; this controller only translates HTTP in and out.
/// </summary>
[ApiController]
[Route("api/files")]
public class FilesController : ControllerBase
{
    private readonly FileVersioningService _files;

    public FilesController(FileVersioningService files)
    {
        _files = files;
    }

    /// <summary>
    /// Uploads a file as a new version of the logical file <paramref name="name"/>. If
    /// <paramref name="name"/> hasn't been used before, this creates version 1; otherwise it
    /// appends the next version and the previous ones remain downloadable.
    /// The upload is streamed straight to storage — it is not buffered into memory or into a
    /// single byte array, so this scales to large files.
    /// </summary>
    [HttpPost("{name}")]
    [DisableRequestSizeLimit] // large uploads are streamed to disk, not buffered — see Program.cs FormOptions
    [Consumes("multipart/form-data")]
    [ProducesResponseType(typeof(FileVersionDto), StatusCodes.Status201Created)]
    public async Task<ActionResult<FileVersionDto>> Upload(
        [FromRoute] string name,
        [FromForm] IFormFile file,
        [FromForm] string? uploadedBy,
        CancellationToken cancellationToken)
    {
        if (file.Length == 0)
        {
            return BadRequest("Uploaded file is empty.");
        }

        await using var stream = file.OpenReadStream();
        var result = await _files.UploadAsync(
            name,
            stream,
            file.FileName,
            file.ContentType,
            uploadedBy,
            cancellationToken);

        return CreatedAtAction(nameof(ListVersions), new { name }, result);
    }

    /// <summary>Lists all logical files with a summary of their latest version.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<FileSummaryDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<List<FileSummaryDto>>> ListFiles(CancellationToken cancellationToken)
    {
        return Ok(await _files.ListFilesAsync(cancellationToken));
    }

    /// <summary>Lists every version of a logical file, newest first.</summary>
    [HttpGet("{name}/versions")]
    [ProducesResponseType(typeof(List<FileVersionDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<List<FileVersionDto>>> ListVersions(string name, CancellationToken cancellationToken)
    {
        var versions = await _files.ListVersionsAsync(name, cancellationToken);
        return versions is null ? NotFound() : Ok(versions);
    }

    /// <summary>Downloads the latest version of a logical file.</summary>
    [HttpGet("{name}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadLatest(string name, CancellationToken cancellationToken)
    {
        var download = await _files.DownloadLatestAsync(name, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        // File(Stream, ...) streams the response body directly from the source stream/disk —
        // ASP.NET Core does not buffer it into memory first.
        return File(download.Content, download.Version.ContentType, download.Version.OriginalFileName);
    }

    /// <summary>Downloads a specific historical version of a logical file.</summary>
    [HttpGet("{name}/versions/{version:int}")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DownloadVersion(string name, int version, CancellationToken cancellationToken)
    {
        var download = await _files.DownloadVersionAsync(name, version, cancellationToken);
        if (download is null)
        {
            return NotFound();
        }

        return File(download.Content, download.Version.ContentType, download.Version.OriginalFileName);
    }

    /// <summary>Deletes a logical file and every version of it.</summary>
    [HttpDelete("{name}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteFile(string name, CancellationToken cancellationToken)
    {
        var deleted = await _files.DeleteFileAsync(name, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }

    /// <summary>Deletes a single version. If it's the last version, the logical file is removed too.</summary>
    [HttpDelete("{name}/versions/{version:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteVersion(string name, int version, CancellationToken cancellationToken)
    {
        var deleted = await _files.DeleteVersionAsync(name, version, cancellationToken);
        return deleted ? NoContent() : NotFound();
    }
}
