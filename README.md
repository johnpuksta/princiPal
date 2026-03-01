# princiPal

Bridge Visual Studio debugger state to AI-powered editors (Claude Code, Cursor) via the Model Context Protocol (MCP).

## What It Does

When you're debugging in Visual Studio and hit a breakpoint, princiPal automatically captures the debug state (local variables, call stack, source context, breakpoints) and makes it available as MCP tools. Your agentic editor can then query this state to help you understand what's happening in your code.

```
VS Debugger ──(auto push)──▶ MCP Server ──(MCP tools)──▶ Claude Code / Cursor
```

## Architecture

Two components work together:

| Component | Runtime | Role |
|---|---|---|
| **VsExtension** (VSIX) | .NET Framework 4.8 | Reads debugger state via EnvDTE, pushes to MCP server on breakpoint |
| **Server** | .NET 10 (ASP.NET Core) | Receives state, exposes MCP tools for AI editors |

## MCP Tools

| Tool | Description |
|---|---|
| `get_debug_state` | Full debug state: location, locals, call stack |
| `get_locals` | Local variables with types, values, and nested members |
| `get_call_stack` | Call stack frames with module and file location |
| `get_source_context` | ~30 lines of source code around the breakpoint |
| `get_breakpoints` | All breakpoints with conditions and status |
| `get_expression_result` | Last evaluated expression result |
| `explain_current_state` | Combined view ideal for AI explanations |

## Quick Start

### 1. Start the MCP Server

```bash
cd src/PrinciPal.Server
dotnet run
```

The server starts on `http://localhost:9229`.

### 2. Configure Your Editor

**Claude Code** (`~/.claude.json`):
```json
{
  "mcpServers": {
    "princiPal": {
      "url": "http://localhost:9229/sse"
    }
  }
}
```

**Cursor** (MCP settings):
```json
{
  "mcpServers": {
    "princiPal": {
      "url": "http://localhost:9229/sse"
    }
  }
}
```

### 3. Install the VSIX Extension

Build and install the VSIX:
```bash
# Build the VSIX project
cd src/PrinciPal.VsExtension
dotnet build

# Install the .vsix file from the output directory into VS 2022
```

### 4. Debug with AI

1. Open your project in Visual Studio
2. Set a breakpoint and start debugging
3. When the breakpoint hits, the extension automatically pushes state to the MCP server
4. In Claude Code or Cursor, ask about the debug state:

```
> Call explain_current_state and tell me why maxVal has this value
```

## Building

```bash
dotnet build         # Build all projects
dotnet test          # Run tests (39 tests)
```

## Project Structure

```
princiPal/
  src/
    PrinciPal.Contracts/      # Shared DTOs (netstandard2.0)
    PrinciPal.Server/      # MCP server (ASP.NET Core)
    PrinciPal.VsExtension/    # VS 2022 extension (VSIX)
  tests/
    PrinciPal.Server.Tests/  # Unit + integration tests
```

## Testing Plan

### Automated Tests (39 tests)
- **DebugStateStoreTests** (8): Thread safety, CRUD, clear/idempotent
- **DebugToolsTests** (24): All MCP tools, error cases, formatting
- **McpServerIntegrationTests** (7): REST endpoints via WebApplicationFactory

### Manual Testing
1. **MCP Server standalone**: `dotnet run`, then POST sample debug state to `/api/debug-state`, verify tools return data
2. **Claude Code integration**: Add MCP config, verify tools appear in tool list, call `get_debug_state`
3. **VSIX in VS**: Install extension, debug a project, verify state pushes to MCP server
4. **End-to-end**: Breakpoint in VS + Claude Code calling `explain_current_state`

## Tech Stack

- MCP C# SDK v1.0.0 (`ModelContextProtocol.AspNetCore`)
- Visual Studio 2022 SDK (EnvDTE)
- ASP.NET Core (.NET 10)
- xUnit + NSubstitute
