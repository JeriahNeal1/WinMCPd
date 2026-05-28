using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using Microsoft.VisualBasic.FileIO;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Windows;

public sealed class FileSystemOperations(StorageLayout layout)
{
    public ResultEnvelope<object> ListDirectory(string path, string? searchPattern = null, bool recursive = false, int limit = 500)
    {
        var directory = Environment.ExpandEnvironmentVariables(path);
        if (!Directory.Exists(directory))
        {
            return ResultEnvelope<object>.Fail("directory_not_found", $"Directory '{directory}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
        var entries = Directory.EnumerateFileSystemEntries(directory, searchPattern ?? "*", options)
            .Take(Math.Max(1, limit))
            .Select(p =>
            {
                var attrs = File.GetAttributes(p);
                return new
                {
                    path = p,
                    name = Path.GetFileName(p),
                    is_directory = attrs.HasFlag(FileAttributes.Directory),
                    attributes = attrs.ToString(),
                    length = File.Exists(p) ? new FileInfo(p).Length : (long?)null
                };
            })
            .ToArray();

        return ResultEnvelope<object>.Ok(entries, "Directory listing.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> ReadFileAsync(string path, int maxBytes = 262144, CancellationToken cancellationToken = default)
    {
        var file = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(file))
        {
            return ResultEnvelope<object>.Fail("file_not_found", $"File '{file}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        await using var stream = File.Open(file, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        var bytesToRead = (int)Math.Min(maxBytes, stream.Length);
        var buffer = new byte[bytesToRead];
        var read = await stream.ReadAsync(buffer, cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new
        {
            path = file,
            bytes_read = read,
            truncated = stream.Length > read,
            text = Encoding.UTF8.GetString(buffer, 0, read)
        }, "File read.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> WriteFileAsync(string path, string content, bool overwrite = false, CancellationToken cancellationToken = default)
    {
        var file = Environment.ExpandEnvironmentVariables(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        if (File.Exists(file) && !overwrite)
        {
            return ResultEnvelope<object>.Fail("file_exists", $"File '{file}' already exists. Set overwrite=true to replace it.", OperationStatus.Failed, RiskLevel.Medium);
        }

        await File.WriteAllTextAsync(file, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new { path = file, bytes = Encoding.UTF8.GetByteCount(content) }, "File written.", RiskLevel.Medium);
    }

    public async Task<ResultEnvelope<object>> AppendFileAsync(string path, string content, CancellationToken cancellationToken = default)
    {
        var file = Environment.ExpandEnvironmentVariables(path);
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
        await File.AppendAllTextAsync(file, content, Encoding.UTF8, cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new { path = file, bytes = Encoding.UTF8.GetByteCount(content) }, "File appended.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> CreateDirectory(string path)
    {
        var directory = Directory.CreateDirectory(Environment.ExpandEnvironmentVariables(path));
        return ResultEnvelope<object>.Ok(new { path = directory.FullName }, "Directory created.", RiskLevel.Low);
    }

    public ResultEnvelope<object> SearchFiles(string root, string pattern, bool recursive = true, int limit = 500)
    {
        var directory = Environment.ExpandEnvironmentVariables(root);
        if (!Directory.Exists(directory))
        {
            return ResultEnvelope<object>.Fail("directory_not_found", $"Directory '{directory}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
        var files = Directory.EnumerateFiles(directory, pattern, options).Take(Math.Max(1, limit)).ToArray();
        return ResultEnvelope<object>.Ok(files, "File search.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> SearchTextAsync(string root, string pattern, string text, bool recursive = true, int limit = 200, CancellationToken cancellationToken = default)
    {
        var directory = Environment.ExpandEnvironmentVariables(root);
        if (!Directory.Exists(directory))
        {
            return ResultEnvelope<object>.Fail("directory_not_found", $"Directory '{directory}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        var options = new EnumerationOptions { RecurseSubdirectories = recursive, IgnoreInaccessible = true };
        var hits = new List<object>();
        foreach (var file in Directory.EnumerateFiles(directory, pattern, options))
        {
            cancellationToken.ThrowIfCancellationRequested();
            try
            {
                var lineNo = 0;
                await foreach (var line in File.ReadLinesAsync(file, cancellationToken).ConfigureAwait(false))
                {
                    lineNo++;
                    if (line.Contains(text, StringComparison.OrdinalIgnoreCase))
                    {
                        hits.Add(new { file, line = lineNo, preview = line.Length > 500 ? line[..500] : line });
                        if (hits.Count >= limit)
                        {
                            return ResultEnvelope<object>.Ok(hits, "Text search hits.", RiskLevel.ReadOnly);
                        }
                    }
                }
            }
            catch
            {
                // Ignore unreadable/binary files.
            }
        }

        return ResultEnvelope<object>.Ok(hits, "Text search hits.", RiskLevel.ReadOnly);
    }

    public async Task<ResultEnvelope<object>> ComputeHashAsync(string path, string algorithm = "SHA256", CancellationToken cancellationToken = default)
    {
        var file = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(file))
        {
            return ResultEnvelope<object>.Fail("file_not_found", $"File '{file}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        await using var stream = File.OpenRead(file);
        var hash = algorithm.Equals("SHA1", StringComparison.OrdinalIgnoreCase)
            ? await SHA1.HashDataAsync(stream, cancellationToken).ConfigureAwait(false)
            : await SHA256.HashDataAsync(stream, cancellationToken).ConfigureAwait(false);
        return ResultEnvelope<object>.Ok(new { path = file, algorithm, hash = Convert.ToHexString(hash).ToLowerInvariant() }, "Hash computed.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> CopyFile(string source, string destination, bool overwrite = false)
    {
        File.Copy(Environment.ExpandEnvironmentVariables(source), Environment.ExpandEnvironmentVariables(destination), overwrite);
        return ResultEnvelope<object>.Ok(new { source, destination, overwrite }, "File copied.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> MoveFile(string source, string destination, bool overwrite = false)
    {
        File.Move(Environment.ExpandEnvironmentVariables(source), Environment.ExpandEnvironmentVariables(destination), overwrite);
        return ResultEnvelope<object>.Ok(new { source, destination, overwrite }, "File moved.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> DeleteFileToRecycleBin(string path)
    {
        FileSystem.DeleteFile(Environment.ExpandEnvironmentVariables(path), UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
        return ResultEnvelope<object>.Ok(new { path }, "File moved to recycle bin.", RiskLevel.Destructive);
    }

    public ResultEnvelope<object> DeleteFilePermanent(string path)
    {
        File.Delete(Environment.ExpandEnvironmentVariables(path));
        return ResultEnvelope<object>.Ok(new { path }, "File permanently deleted.", RiskLevel.Destructive);
    }

    public ResultEnvelope<object> CreateBackup(string path)
    {
        var source = Environment.ExpandEnvironmentVariables(path);
        if (!File.Exists(source))
        {
            return ResultEnvelope<object>.Fail("file_not_found", $"File '{source}' does not exist.", OperationStatus.Failed, RiskLevel.ReadOnly);
        }

        var backup = Path.Combine(layout.Patches, $"{Path.GetFileName(source)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak");
        File.Copy(source, backup, overwrite: false);
        return ResultEnvelope<object>.Ok(new { source, backup }, "Backup created.", RiskLevel.Low);
    }

    public ResultEnvelope<object> CreateZipArchive(string sourceDirectory, string destinationZip, bool overwrite = false)
    {
        var source = Environment.ExpandEnvironmentVariables(sourceDirectory);
        var destination = Environment.ExpandEnvironmentVariables(destinationZip);
        if (File.Exists(destination) && overwrite)
        {
            File.Delete(destination);
        }

        ZipFile.CreateFromDirectory(source, destination, CompressionLevel.Optimal, includeBaseDirectory: false);
        return ResultEnvelope<object>.Ok(new { source, destination }, "Zip archive created.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> ExtractZipArchive(string zipPath, string destinationDirectory, bool overwrite = false)
    {
        ZipFile.ExtractToDirectory(Environment.ExpandEnvironmentVariables(zipPath), Environment.ExpandEnvironmentVariables(destinationDirectory), overwrite);
        return ResultEnvelope<object>.Ok(new { zipPath, destinationDirectory, overwrite }, "Zip archive extracted.", RiskLevel.Medium);
    }
}
