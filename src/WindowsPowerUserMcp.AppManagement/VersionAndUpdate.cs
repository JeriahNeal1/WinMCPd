using System.Net.Http.Headers;
using System.Text.Json;

namespace WindowsPowerUserMcp.AppManagement;

public static class SemanticVersionComparer
{
    public static int Compare(string left, string right)
    {
        var l = Parse(left);
        var r = Parse(right);
        for (var i = 0; i < 3; i++)
        {
            var comparison = l.Numbers[i].CompareTo(r.Numbers[i]);
            if (comparison != 0)
            {
                return comparison;
            }
        }

        if (string.Equals(l.Prerelease, r.Prerelease, StringComparison.OrdinalIgnoreCase)) return 0;
        if (string.IsNullOrWhiteSpace(l.Prerelease)) return 1;
        if (string.IsNullOrWhiteSpace(r.Prerelease)) return -1;
        return string.Compare(l.Prerelease, r.Prerelease, StringComparison.OrdinalIgnoreCase);
    }

    private static (int[] Numbers, string? Prerelease) Parse(string version)
    {
        var cleaned = version.Trim().TrimStart('v', 'V');
        var parts = cleaned.Split('-', 2);
        var numbers = parts[0].Split('.')
            .Select(p => int.TryParse(new string(p.TakeWhile(char.IsDigit).ToArray()), out var n) ? n : 0)
            .Concat([0, 0, 0])
            .Take(3)
            .ToArray();
        return (numbers, parts.Length == 2 ? parts[1] : null);
    }
}

public sealed class UpdateService(HttpClient? httpClient = null)
{
    private readonly HttpClient _http = httpClient ?? new HttpClient();

    public async Task<AppOperationResult<UpdateCheckResult>> CheckForUpdatesAsync(
        string repository,
        string currentVersion,
        bool includePrerelease,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var endpoint = repository.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                ? repository
                : $"https://api.github.com/repos/{repository}/releases";
            using var request = new HttpRequestMessage(HttpMethod.Get, endpoint);
            request.Headers.UserAgent.Add(new ProductInfoHeaderValue(ProductConstants.ProductName, currentVersion));
            using var response = await _http.SendAsync(request, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                return AppOperationResult<UpdateCheckResult>.Fail("update_check_failed", $"Update check failed: HTTP {(int)response.StatusCode} {response.ReasonPhrase}");
            }

            await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var document = await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken).ConfigureAwait(false);
            var releases = ParseReleases(document.RootElement)
                .Where(r => includePrerelease || !r.Prerelease)
                .OrderByDescending(r => r.Version, Comparer<string>.Create(SemanticVersionComparer.Compare))
                .ToArray();
            var latest = releases.FirstOrDefault();
            if (latest is null)
            {
                return AppOperationResult<UpdateCheckResult>.Ok(new UpdateCheckResult(false, currentVersion, null, "No releases found."), "No releases found.");
            }

            var available = SemanticVersionComparer.Compare(latest.Version, currentVersion) > 0;
            return AppOperationResult<UpdateCheckResult>.Ok(
                new UpdateCheckResult(available, currentVersion, latest, available ? $"Update {latest.Version} is available." : "You are up to date."),
                available ? "Update available." : "Already current.");
        }
        catch (Exception ex)
        {
            return AppOperationResult<UpdateCheckResult>.Fail("update_exception", ex.Message);
        }
    }

    public async Task<AppOperationResult<string>> DownloadAssetAsync(UpdateReleaseAsset asset, string stagingRoot, CancellationToken cancellationToken = default)
    {
        try
        {
            Directory.CreateDirectory(stagingRoot);
            var destination = Path.Combine(stagingRoot, asset.Name);
            await using var stream = await _http.GetStreamAsync(asset.BrowserDownloadUrl, cancellationToken).ConfigureAwait(false);
            await using var file = File.Open(destination, FileMode.Create, FileAccess.Write, FileShare.Read);
            await stream.CopyToAsync(file, cancellationToken).ConfigureAwait(false);
            return AppOperationResult<string>.Ok(destination, "Update asset downloaded.");
        }
        catch (Exception ex)
        {
            return AppOperationResult<string>.Fail("download_failed", ex.Message);
        }
    }

    public static IReadOnlyList<UpdateRelease> ParseReleases(JsonElement root)
    {
        var releases = root.ValueKind == JsonValueKind.Array
            ? root.EnumerateArray().ToArray()
            : [root];
        var result = new List<UpdateRelease>();
        foreach (var release in releases)
        {
            var tag = GetString(release, "tag_name") ?? GetString(release, "name") ?? "0.0.0";
            var assets = release.TryGetProperty("assets", out var assetArray) && assetArray.ValueKind == JsonValueKind.Array
                ? assetArray.EnumerateArray().Select(a => new UpdateReleaseAsset(
                    GetString(a, "name") ?? "asset",
                    GetString(a, "browser_download_url") ?? string.Empty,
                    GetInt64(a, "size") ?? 0,
                    GetString(a, "sha256"))).ToArray()
                : [];

            result.Add(new UpdateRelease(
                tag.TrimStart('v', 'V'),
                GetString(release, "name") ?? tag,
                GetString(release, "body") ?? string.Empty,
                DateTimeOffset.TryParse(GetString(release, "published_at"), out var published) ? published : null,
                GetBool(release, "prerelease"),
                assets));
        }

        return result;
    }

    private static string? GetString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.String ? property.GetString() : null;

    private static bool GetBool(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind is JsonValueKind.True or JsonValueKind.False && property.GetBoolean();

    private static long? GetInt64(JsonElement element, string name) =>
        element.TryGetProperty(name, out var property) && property.ValueKind == JsonValueKind.Number && property.TryGetInt64(out var value) ? value : null;
}
