using System.IO.Compression;
using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
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

    public ResultEnvelope<object> ApplyUnifiedDiffPatch(string patchText, string? root = null, bool dryRun = true, bool backup = true)
    {
        var baseRoot = Path.GetFullPath(Environment.ExpandEnvironmentVariables(string.IsNullOrWhiteSpace(root) ? Environment.CurrentDirectory : root));
        var patches = UnifiedDiffParser.Parse(patchText).ToArray();
        var results = new List<object>();

        foreach (var patch in patches)
        {
            var target = ResolvePatchPath(baseRoot, patch.NewPath ?? patch.OldPath);
            if (!File.Exists(target))
            {
                return ResultEnvelope<object>.Fail("patch_target_not_found", $"Patch target '{target}' does not exist.", OperationStatus.Failed, RiskLevel.Medium);
            }

            var original = File.ReadAllLines(target).ToList();
            var updated = ApplyPatchToLines(original, patch);
            string? backupPath = null;
            if (!dryRun)
            {
                if (backup)
                {
                    backupPath = Path.Combine(layout.Patches, $"{Path.GetFileName(target)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.bak");
                    File.Copy(target, backupPath, overwrite: false);
                }

                File.WriteAllLines(target, updated, Encoding.UTF8);
            }

            results.Add(new { target, dry_run = dryRun, backup_path = backupPath, original_lines = original.Count, updated_lines = updated.Count });
        }

        return ResultEnvelope<object>.Ok(new { files = results }, dryRun ? "Patch dry-run succeeded." : "Patch applied.", RiskLevel.Medium);
    }

    public ResultEnvelope<object> GetFileAcl(string path)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        FileSystemSecurity security = File.Exists(fullPath)
            ? new FileInfo(fullPath).GetAccessControl()
            : new DirectoryInfo(fullPath).GetAccessControl();

        var rules = security.GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .Select(r => new
            {
                identity = r.IdentityReference.Value,
                rights = r.FileSystemRights.ToString(),
                access_type = r.AccessControlType.ToString(),
                inherited = r.IsInherited,
                inheritance_flags = r.InheritanceFlags.ToString(),
                propagation_flags = r.PropagationFlags.ToString()
            })
            .ToArray();

        return ResultEnvelope<object>.Ok(new
        {
            path = fullPath,
            owner = security.GetOwner(typeof(SecurityIdentifier))?.Value ?? string.Empty,
            sddl = security.GetSecurityDescriptorSddlForm(AccessControlSections.All),
            rules
        }, "File ACL read.", RiskLevel.ReadOnly);
    }

    public ResultEnvelope<object> SetFileAcl(string path, string identity, string rights, string accessType = "Allow", bool inherit = false)
    {
        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var fileSystemRights = Enum.Parse<FileSystemRights>(rights, ignoreCase: true);
        var controlType = Enum.Parse<AccessControlType>(accessType, ignoreCase: true);
        var inheritanceFlags = inherit ? InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit : InheritanceFlags.None;
        var propagationFlags = inherit ? PropagationFlags.None : PropagationFlags.NoPropagateInherit;
        var rule = new FileSystemAccessRule(identity, fileSystemRights, inheritanceFlags, propagationFlags, controlType);
        string? backupPath = null;

        if (File.Exists(fullPath))
        {
            var fileInfo = new FileInfo(fullPath);
            var security = fileInfo.GetAccessControl();
            backupPath = BackupAcl(fullPath, security);
            security.AddAccessRule(rule);
            fileInfo.SetAccessControl((FileSecurity)security);
        }
        else
        {
            var directoryInfo = new DirectoryInfo(fullPath);
            var security = directoryInfo.GetAccessControl();
            backupPath = BackupAcl(fullPath, security);
            security.AddAccessRule(rule);
            directoryInfo.SetAccessControl((DirectorySecurity)security);
        }

        return ResultEnvelope<object>.Ok(new { path = fullPath, identity, rights, access_type = accessType, backup_path = backupPath }, "ACL updated after SDDL backup.", RiskLevel.High);
    }

    public ResultEnvelope<object> TakeOwnershipIfElevated(string path)
    {
        if (!SystemOperations.IsElevated())
        {
            return ResultEnvelope<object>.Fail("not_elevated", "Taking ownership requires an elevated broker. No UAC bypass is attempted.", OperationStatus.Failed, RiskLevel.High);
        }

        var fullPath = Path.GetFullPath(Environment.ExpandEnvironmentVariables(path));
        var owner = WindowsIdentity.GetCurrent().User ?? throw new InvalidOperationException("Current user SID is unavailable.");

        if (File.Exists(fullPath))
        {
            var fileInfo = new FileInfo(fullPath);
            var security = fileInfo.GetAccessControl();
            var backupPath = BackupAcl(fullPath, security);
            security.SetOwner(owner);
            fileInfo.SetAccessControl(security);
            return ResultEnvelope<object>.Ok(new { path = fullPath, owner = owner.Value, backup_path = backupPath }, "File ownership updated.", RiskLevel.Destructive);
        }
        else
        {
            var directoryInfo = new DirectoryInfo(fullPath);
            var security = directoryInfo.GetAccessControl();
            var backupPath = BackupAcl(fullPath, security);
            security.SetOwner(owner);
            directoryInfo.SetAccessControl(security);
            return ResultEnvelope<object>.Ok(new { path = fullPath, owner = owner.Value, backup_path = backupPath }, "Directory ownership updated.", RiskLevel.Destructive);
        }
    }

    private string BackupAcl(string path, FileSystemSecurity security)
    {
        Directory.CreateDirectory(layout.Patches);
        var backupPath = Path.Combine(layout.Patches, $"{Path.GetFileName(path)}.{DateTimeOffset.UtcNow:yyyyMMddHHmmss}.sddl");
        File.WriteAllText(backupPath, security.GetSecurityDescriptorSddlForm(AccessControlSections.All), Encoding.UTF8);
        return backupPath;
    }

    private static string ResolvePatchPath(string baseRoot, string? patchPath)
    {
        if (string.IsNullOrWhiteSpace(patchPath))
        {
            throw new ArgumentException("Patch path is missing.");
        }

        var cleaned = patchPath.Replace('\\', '/');
        if (cleaned.StartsWith("a/", StringComparison.Ordinal) || cleaned.StartsWith("b/", StringComparison.Ordinal))
        {
            cleaned = cleaned[2..];
        }

        var full = Path.GetFullPath(Path.Combine(baseRoot, cleaned));
        if (!full.StartsWith(baseRoot, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException("Patch target escaped the requested root.");
        }

        return full;
    }

    private static List<string> ApplyPatchToLines(List<string> original, FilePatch patch)
    {
        var output = new List<string>();
        var cursor = 0;
        foreach (var hunk in patch.Hunks)
        {
            var hunkStart = Math.Max(0, hunk.OldStart - 1);
            while (cursor < hunkStart)
            {
                output.Add(original[cursor++]);
            }

            foreach (var line in hunk.Lines)
            {
                switch (line.Kind)
                {
                    case DiffLineKind.Context:
                        if (cursor >= original.Count || original[cursor] != line.Text)
                        {
                            throw new InvalidOperationException($"Patch context mismatch in {patch.NewPath ?? patch.OldPath} near old line {cursor + 1}.");
                        }

                        output.Add(original[cursor++]);
                        break;
                    case DiffLineKind.Removed:
                        if (cursor >= original.Count || original[cursor] != line.Text)
                        {
                            throw new InvalidOperationException($"Patch removal mismatch in {patch.NewPath ?? patch.OldPath} near old line {cursor + 1}.");
                        }

                        cursor++;
                        break;
                    case DiffLineKind.Added:
                        output.Add(line.Text);
                        break;
                }
            }
        }

        while (cursor < original.Count)
        {
            output.Add(original[cursor++]);
        }

        return output;
    }
}

internal enum DiffLineKind
{
    Context,
    Added,
    Removed
}

internal sealed record DiffLine(DiffLineKind Kind, string Text);
internal sealed record DiffHunk(int OldStart, int OldCount, int NewStart, int NewCount, IReadOnlyList<DiffLine> Lines);
internal sealed record FilePatch(string? OldPath, string? NewPath, IReadOnlyList<DiffHunk> Hunks);

internal static class UnifiedDiffParser
{
    public static IEnumerable<FilePatch> Parse(string patchText)
    {
        var lines = patchText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var index = 0;
        while (index < lines.Length)
        {
            if (!lines[index].StartsWith("--- ", StringComparison.Ordinal))
            {
                index++;
                continue;
            }

            var oldPath = lines[index][4..].Trim();
            index++;
            if (index >= lines.Length || !lines[index].StartsWith("+++ ", StringComparison.Ordinal))
            {
                throw new InvalidOperationException("Unified diff is missing +++ file header.");
            }

            var newPath = lines[index][4..].Trim();
            index++;
            var hunks = new List<DiffHunk>();
            while (index < lines.Length && lines[index].StartsWith("@@ ", StringComparison.Ordinal))
            {
                var header = lines[index++];
                var (oldStart, oldCount, newStart, newCount) = ParseHunkHeader(header);
                var hunkLines = new List<DiffLine>();
                while (index < lines.Length && !lines[index].StartsWith("@@ ", StringComparison.Ordinal) && !lines[index].StartsWith("--- ", StringComparison.Ordinal))
                {
                    var line = lines[index++];
                    if (line.Length == 0)
                    {
                        hunkLines.Add(new DiffLine(DiffLineKind.Context, string.Empty));
                        continue;
                    }

                    hunkLines.Add(line[0] switch
                    {
                        ' ' => new DiffLine(DiffLineKind.Context, line[1..]),
                        '+' => new DiffLine(DiffLineKind.Added, line[1..]),
                        '-' => new DiffLine(DiffLineKind.Removed, line[1..]),
                        '\\' => new DiffLine(DiffLineKind.Context, line),
                        _ => new DiffLine(DiffLineKind.Context, line)
                    });
                }

                hunks.Add(new DiffHunk(oldStart, oldCount, newStart, newCount, hunkLines));
            }

            yield return new FilePatch(oldPath, newPath, hunks);
        }
    }

    private static (int OldStart, int OldCount, int NewStart, int NewCount) ParseHunkHeader(string header)
    {
        var parts = header.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 3)
        {
            throw new InvalidOperationException($"Invalid hunk header: {header}");
        }

        var old = ParseRange(parts[1][1..]);
        var newer = ParseRange(parts[2][1..]);
        return (old.Start, old.Count, newer.Start, newer.Count);
    }

    private static (int Start, int Count) ParseRange(string value)
    {
        var parts = value.Split(',', 2);
        return (int.Parse(parts[0]), parts.Length == 2 ? int.Parse(parts[1]) : 1);
    }
}
