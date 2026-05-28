using System.Text.RegularExpressions;
using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Security;

public sealed record RedactionResult(string Text, IReadOnlyList<string> RedactionsApplied);

public sealed class SecretRedactor
{
    private static readonly Regex BearerRegex = new(@"(?i)\bBearer\s+[A-Za-z0-9._~+/=-]{16,}", RegexOptions.Compiled);
    private static readonly Regex AssignmentRegex = new(@"(?i)\b(password|passwd|pwd|secret|token|access_token|refresh_token|id_token|api[_-]?key|authorization|cookie)\b\s*[:=]\s*[""']?[^""'\s;&|]+", RegexOptions.Compiled);
    private static readonly Regex JwtRegex = new(@"\beyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}\b", RegexOptions.Compiled);
    private static readonly Regex ConnectionStringRegex = new(@"(?i)\b(AccountKey|SharedAccessKey|Password|Pwd)=([^;]+)", RegexOptions.Compiled);

    private readonly List<Regex> _configuredPatterns;

    public SecretRedactor(WindowsPowerUserMcpOptions? options = null)
    {
        _configuredPatterns = (options?.RedactSecretPatterns ?? [])
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => new Regex($@"(?i)\b({p})\b\s*[:=]\s*[""']?[^""'\s;&|]+", RegexOptions.Compiled))
            .ToList();
    }

    public RedactionResult Redact(string? input)
    {
        if (string.IsNullOrEmpty(input))
        {
            return new RedactionResult(string.Empty, []);
        }

        var redactions = new List<string>();
        var output = input;

        output = Replace(output, BearerRegex, "Bearer [REDACTED]", "bearer_token", redactions);
        output = Replace(output, AssignmentRegex, m => $"{m.Groups[1].Value}=[REDACTED]", "secret_assignment", redactions);
        output = Replace(output, JwtRegex, "[REDACTED_JWT]", "jwt", redactions);
        output = Replace(output, ConnectionStringRegex, m => $"{m.Groups[1].Value}=[REDACTED]", "connection_string_secret", redactions);

        foreach (var regex in _configuredPatterns)
        {
            output = Replace(output, regex, m => $"{m.Groups[1].Value}=[REDACTED]", "configured_secret_pattern", redactions);
        }

        return new RedactionResult(output, redactions.Distinct(StringComparer.OrdinalIgnoreCase).ToArray());
    }

    public IReadOnlyDictionary<string, string> RedactEnvironment(IReadOnlyDictionary<string, string?> environment)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (key, value) in environment)
        {
            if (LooksSecretKey(key))
            {
                result[key] = "[REDACTED]";
            }
            else
            {
                result[key] = Redact(value).Text;
            }
        }

        return result;
    }

    public static bool LooksSecretKey(string key) =>
        Regex.IsMatch(key, "(?i)(password|passwd|pwd|secret|token|api[_-]?key|authorization|cookie|credential)");

    private static string Replace(string input, Regex regex, string replacement, string label, List<string> redactions)
    {
        if (regex.IsMatch(input))
        {
            redactions.Add(label);
        }

        return regex.Replace(input, replacement);
    }

    private static string Replace(string input, Regex regex, MatchEvaluator replacement, string label, List<string> redactions)
    {
        if (regex.IsMatch(input))
        {
            redactions.Add(label);
        }

        return regex.Replace(input, replacement);
    }
}
