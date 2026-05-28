using System.Text.RegularExpressions;

namespace WindowsPowerUserMcp.Security;

public sealed record PatchSafetyFinding(string Severity, string Code, string Message, string? Path, int? Line);

public sealed record PatchSafetyResult(
    bool Safe,
    IReadOnlyList<PatchSafetyFinding> Findings,
    IReadOnlyList<string> RedactionsApplied,
    string RedactedPreview);

public sealed class PatchSafetyScanner(SecretRedactor? redactor = null)
{
    private static readonly Regex DiffPathRegex = new(@"^(---|\+\+\+)\s+(?<path>\S+)", RegexOptions.Compiled);
    private static readonly Regex AddedLineRegex = new(@"^\+(?!\+\+)(?<text>.*)$", RegexOptions.Compiled);

    private static readonly (Regex Pattern, string Code, string Message)[] UnsafeContentPatterns =
    [
        (new Regex(@"(?i)\bmimikatz\b", RegexOptions.Compiled), "credential_tool_reference", "Patch references credential dumping tooling."),
        (new Regex(@"(?i)\bsekurlsa\b", RegexOptions.Compiled), "credential_dump_reference", "Patch references LSASS credential extraction primitives."),
        (new Regex(@"(?i)\blsass\b.*\b(dump|memory|minidump)\b", RegexOptions.Compiled), "lsass_dump_reference", "Patch appears to reference LSASS memory dumping."),
        (new Regex(@"(?i)\b(browser\s+cookies|cookie\s+extraction|Login\s+Data)\b", RegexOptions.Compiled), "browser_secret_extraction", "Patch appears to reference browser cookie/password extraction."),
        (new Regex(@"(?i)\b(uac\s*bypass|bypassuac)\b", RegexOptions.Compiled), "uac_bypass_reference", "Patch appears to reference UAC bypass behavior."),
        (new Regex(@"(?i)\b(disable\s+defender|Set-MpPreference\s+-DisableRealtimeMonitoring\s+\$?true)\b", RegexOptions.Compiled), "security_tool_evasion", "Patch appears to disable or evade security tooling."),
        (new Regex(@"(?i)\b(hidden|stealth)\s+(service|task|persistence)\b", RegexOptions.Compiled), "stealth_persistence", "Patch appears to create stealthy persistence.")
    ];

    private static readonly Regex SensitivePathRegex = new(@"(?i)(^|[\\/])(\.env|id_rsa|id_dsa|credentials|cookies|login\s*data)(\.|$|[\\/])", RegexOptions.Compiled);

    private readonly SecretRedactor _redactor = redactor ?? new SecretRedactor();

    public PatchSafetyResult Scan(string patchText, int previewBytes = 8192)
    {
        var findings = new List<PatchSafetyFinding>();
        var redacted = _redactor.Redact(patchText);
        string? currentPath = null;
        var lineNumber = 0;

        foreach (var rawLine in patchText.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            lineNumber++;
            var pathMatch = DiffPathRegex.Match(rawLine);
            if (pathMatch.Success)
            {
                currentPath = pathMatch.Groups["path"].Value;
                if (SensitivePathRegex.IsMatch(currentPath))
                {
                    findings.Add(new PatchSafetyFinding("high", "sensitive_path", "Patch touches a path that commonly contains secrets.", currentPath, lineNumber));
                }
            }

            var added = AddedLineRegex.Match(rawLine);
            if (!added.Success)
            {
                continue;
            }

            var text = added.Groups["text"].Value;
            foreach (var (pattern, code, message) in UnsafeContentPatterns)
            {
                if (pattern.IsMatch(text))
                {
                    findings.Add(new PatchSafetyFinding("critical", code, message, currentPath, lineNumber));
                }
            }
        }

        if (redacted.RedactionsApplied.Count > 0)
        {
            findings.Add(new PatchSafetyFinding("high", "secret_material_detected", "Patch contains token/password/key-looking material and was redacted.", currentPath, null));
        }

        var preview = redacted.Text.Length > previewBytes
            ? redacted.Text[..previewBytes] + "\n[TRUNCATED]"
            : redacted.Text;

        return new PatchSafetyResult(
            Safe: findings.All(f => !string.Equals(f.Severity, "critical", StringComparison.OrdinalIgnoreCase) && !string.Equals(f.Severity, "high", StringComparison.OrdinalIgnoreCase)),
            Findings: findings,
            RedactionsApplied: redacted.RedactionsApplied,
            RedactedPreview: preview);
    }
}
