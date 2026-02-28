---
name: stop-mcp
description: Stop the VsDebugBridge MCP server
allowed-tools: Bash
---

Stop the VsDebugBridge MCP server.

## Steps

1. Find the process listening on port 9229:
   ```
   netstat -ano | findstr :9229
   ```

2. Extract the PID from the output and kill it:
   ```
   taskkill /PID <pid> /F
   ```

3. If nothing is listening on 9229, tell the user the server is not running.
