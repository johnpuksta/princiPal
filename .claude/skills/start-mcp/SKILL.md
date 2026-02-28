---
name: start-mcp
description: Start the VsDebugBridge MCP server in the background
allowed-tools: Bash
---

Start the VsDebugBridge MCP server as a background process.

## Steps

1. Check if port 9229 is already in use:
   ```
   netstat -ano | findstr :9229
   ```
   If in use, tell the user and show the PID.

2. If not running, start it in the background:
   ```
   Start-Process -NoNewWindow -FilePath "dotnet" -ArgumentList "run --project C:/Repos/VsDebugBridge/src/VsDebugBridge.McpServer/VsDebugBridge.McpServer.csproj" -PassThru
   ```
   This returns the PID.

3. Wait 2 seconds, then verify with:
   ```
   curl -s http://localhost:9229/api/health
   ```

4. Report the PID to the user. Tell them to run `/stop-mcp` or `taskkill /PID <pid> /F` to stop it.
