using WindowsPowerUserMcp.Core;

namespace WindowsPowerUserMcp.Security;

public sealed class RiskClassifier
{
    public RiskLevel ClassifyTool(string toolName)
    {
        var name = toolName.ToLowerInvariant();
        if (name.Contains("password") || name.Contains("credential") || name.Contains("cookie") || name.Contains("token"))
        {
            return RiskLevel.CredentialSensitive;
        }

        if (name.Contains("delete") || name.Contains("kill") || name.Contains("uninstall") || name.Contains("format"))
        {
            return RiskLevel.Destructive;
        }

        if (name.Contains("registry_write") || name.Contains("registry_delete") || name.Contains("service") || name.Contains("scheduled_task") || name.Contains("firewall"))
        {
            return RiskLevel.High;
        }

        if (name.StartsWith("run_", StringComparison.Ordinal) || name.Contains("install") || name.Contains("start_process"))
        {
            return RiskLevel.Medium;
        }

        if (name.StartsWith("get_", StringComparison.Ordinal) || name.StartsWith("list_", StringComparison.Ordinal) || name.StartsWith("read_", StringComparison.Ordinal))
        {
            return RiskLevel.ReadOnly;
        }

        return RiskLevel.Low;
    }

    public RiskLevel ClassifyCommand(string fileName, string arguments)
    {
        var command = $"{fileName} {arguments}".ToLowerInvariant();
        if (command.Contains("bypass") && command.Contains("uac"))
        {
            return RiskLevel.SecuritySensitive;
        }

        if (command.Contains("remove-item") || command.Contains(" del ") || command.Contains(" rmdir ") || command.Contains("format "))
        {
            return RiskLevel.Destructive;
        }

        if (command.Contains("set-itemproperty") || command.Contains("new-service") || command.Contains("sc.exe") || command.Contains("schtasks"))
        {
            return RiskLevel.High;
        }

        return RiskLevel.Medium;
    }
}
