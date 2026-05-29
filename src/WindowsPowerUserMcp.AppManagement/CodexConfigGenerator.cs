namespace WindowsPowerUserMcp.AppManagement;

public static class CodexConfigGenerator
{
    public static string GenerateInstalled(string installRoot) =>
        $$"""
        [mcp_servers.windows_power_user]
        command = "{{Path.Combine(installRoot, ProductConstants.AppExecutableName).Replace("\\", "\\\\")}}"
        args = ["--stdio"]
        startup_timeout_sec = 20
        tool_timeout_sec = 300
        default_tools_approval_mode = "prompt"
        """;

    public static string GenerateDevelopment(string repoRoot) =>
        $$"""
        [mcp_servers.windows_power_user]
        command = "dotnet"
        args = [
          "run",
          "--project",
          "{{Path.Combine(repoRoot, "src", "WindowsPowerUserMcp.App", "WindowsPowerUserMcp.App.csproj").Replace("\\", "\\\\")}}",
          "--",
          "--stdio"
        ]
        startup_timeout_sec = 30
        tool_timeout_sec = 300
        default_tools_approval_mode = "prompt"
        """;

    public static string GenerateFallbackStdioBridge(string installRoot) =>
        $$"""
        [mcp_servers.windows_power_user]
        command = "{{Path.Combine(installRoot, "WindowsPowerUserMcp.StdioBridge.exe").Replace("\\", "\\\\")}}"
        startup_timeout_sec = 20
        tool_timeout_sec = 300
        default_tools_approval_mode = "prompt"
        """;
}
