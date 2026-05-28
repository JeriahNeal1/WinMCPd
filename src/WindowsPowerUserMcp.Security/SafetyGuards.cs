namespace WindowsPowerUserMcp.Security;

public sealed class SafetyGuards
{
    private static readonly string[] BlockedCommandFragments =
    [
        "mimikatz",
        "lsass",
        "sekurlsa",
        "uac bypass",
        "bypassuac",
        "stealth persistence",
        "disable defender",
        "dump credentials",
        "browser cookies",
        "login data"
    ];

    public (bool Allowed, string? Reason) CheckCommandAllowed(string commandLine)
    {
        var lower = commandLine.ToLowerInvariant();
        foreach (var fragment in BlockedCommandFragments)
        {
            if (lower.Contains(fragment, StringComparison.Ordinal))
            {
                return (false, $"Blocked credential/security operation pattern: {fragment}");
            }
        }

        return (true, null);
    }
}
