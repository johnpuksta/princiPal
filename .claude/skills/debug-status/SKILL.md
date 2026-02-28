---
name: debug-status
description: Check if the MCP server is running and if debug state is available
allowed-tools: Bash
---

Check the status of VsDebugBridge.

## Steps

1. Check if the MCP server is running:
   ```
   curl -s http://localhost:9229/api/health
   ```
   If it fails, the server is not running. Tell the user to run `/start-mcp`.

2. If the server is running, check for the PID:
   ```
   netstat -ano | findstr :9229
   ```
   Report the PID.

3. Report the overall status: server running/stopped, PID if running.
