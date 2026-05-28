# Codex Config

Use stdio for Codex as the primary path.

StdioBridge exposes direct generated MCP methods for every broker tool descriptor. `broker_call` exists only as a deprecated compatibility fallback; prefer direct tools after refreshing the MCP server in Codex.

```toml
[mcp_servers.windows_power_user]
command = "C:\\Tools\\WindowsPowerUserMcp\\WindowsPowerUserMcp.StdioBridge.exe"
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
  "C:\\dev\\WindowsPowerUserMcp\\src\\WindowsPowerUserMcp.StdioBridge\\WindowsPowerUserMcp.StdioBridge.csproj"
]
startup_timeout_sec = 30
tool_timeout_sec = 300
default_tools_approval_mode = "prompt"
```

Optional HTTP:

```toml
[mcp_servers.windows_power_user_http]
url = "http://127.0.0.1:49321/mcp"
startup_timeout_sec = 20
tool_timeout_sec = 300
default_tools_approval_mode = "prompt"
```

HTTP is disabled by default. Start `HttpHost` with `--enable-http` and set `WINDOWS_POWER_USER_MCP_HTTP_TOKEN` when auth is required.
