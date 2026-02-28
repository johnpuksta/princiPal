# VsDebugBridge

MCP server + VSIX extension that bridges VS 2022 debugger state to AI editors.

## Commands

| Command | What it does |
|---|---|
| `/start-mcp` | Start the MCP server on localhost:9229 (background) |
| `/stop-mcp` | Kill the MCP server process |
| `/build-vsix` | Build the VS extension |
| `/debug-status` | Check if server is running + show PID |

## Process Management

```powershell
# Find what's on port 9229
netstat -ano | findstr :9229

# Kill by PID
taskkill /PID <pid> /F

# Start server manually (foreground)
dotnet run --project src/VsDebugBridge.McpServer/VsDebugBridge.McpServer.csproj

# Start server (background, returns PID)
Start-Process -NoNewWindow -FilePath "dotnet" -ArgumentList "run --project src/VsDebugBridge.McpServer/VsDebugBridge.McpServer.csproj" -PassThru
```

## Build & Test

```bash
dotnet build                    # Build all
dotnet test                     # Run 39 tests
dotnet build -c Release         # Release build
```

## MCP Config (add to ~/.claude.json)

```json
{
  "mcpServers": {
    "vs-debug-bridge": {
      "url": "http://localhost:9229/"
    }
  }
}
```
