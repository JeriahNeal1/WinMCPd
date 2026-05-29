# Codex Config

Use stdio as the primary Codex path. The installed all-in-one app is preferred:

```toml
[mcp_servers.windows_power_user]
command = "C:\\Tools\\WindowsPowerUserMcp\\WindowsPowerUserMcp.App.exe"
args = ["--stdio"]
startup_timeout_sec = 20
tool_timeout_sec = 300
default_tools_approval_mode = "prompt"
```

Development:

```toml
[mcp_servers.windows_power_user]
command = "dotnet"
args = [
  "run",
  "--project",
  "C:\\Users\\Natal\\OneDrive\\Documents\\WinMCPd\\src\\WindowsPowerUserMcp.App\\WindowsPowerUserMcp.App.csproj",
  "--",
  "--stdio"
]
startup_timeout_sec = 30
tool_timeout_sec = 300
default_tools_approval_mode = "prompt"
```

Fallback standalone bridge:

```toml
[mcp_servers.windows_power_user]
command = "C:\\Tools\\WindowsPowerUserMcp\\WindowsPowerUserMcp.StdioBridge.exe"
startup_timeout_sec = 20
tool_timeout_sec = 300
default_tools_approval_mode = "prompt"
```

Stdio mode exposes direct generated MCP methods for every broker tool descriptor. `broker_call` remains as a deprecated compatibility fallback; prefer direct tool calls after refreshing Codex's MCP tool cache.

Optional HTTP:

```toml
[mcp_servers.windows_power_user_http]
url = "http://127.0.0.1:49321/mcp"
startup_timeout_sec = 20
tool_timeout_sec = 300
default_tools_approval_mode = "prompt"
```

HTTP is disabled unless explicitly started with `WindowsPowerUserMcp.App.exe --http` or the standalone HttpHost. When HTTP auth is required, set `WINDOWS_POWER_USER_MCP_HTTP_TOKEN`.

High-trust personal lab mode can use a more permissive Codex approval setting, but the platform still emits redacted audit logs:

```toml
[mcp_servers.windows_power_user_lab]
command = "C:\\Tools\\WindowsPowerUserMcp\\WindowsPowerUserMcp.App.exe"
args = ["--stdio"]
startup_timeout_sec = 20
tool_timeout_sec = 300
default_tools_approval_mode = "on-failure"
```
