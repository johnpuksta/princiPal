---
name: build-vsix
description: Build the princiPal VSIX extension for Visual Studio 2022
allowed-tools: Bash
---

Build the VSIX extension.

## Steps

1. Build the VSIX project:
   ```
   dotnet build C:/Repos/VsDebugBridge/src/PrinciPal.VsExtension/PrinciPal.VsExtension.csproj -c Release
   ```

2. Report the build result. The output DLL is at:
   ```
   C:/Repos/VsDebugBridge/src/PrinciPal.VsExtension/bin/Release/net48/PrinciPal.VsExtension.dll
   ```

3. Tell the user: To install in VS 2022, they need to generate a `.vsix` package. For development/testing, they can use the VS Experimental Instance by opening the `.csproj` in VS and pressing F5. The extension auto-loads when a solution is opened and pushes debug state to `localhost:9229` on every breakpoint hit.
