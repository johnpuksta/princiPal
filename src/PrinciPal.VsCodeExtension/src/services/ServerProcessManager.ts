import * as fs from "node:fs";
import * as path from "node:path";
import { spawn, type ChildProcess } from "node:child_process";
import type { IExtensionLogger } from "../abstractions/IExtensionLogger.js";
import { ServerLockFile } from "./ServerLockFile.js";
import { type Result, success, failure, ServerBinaryNotFoundError } from "../types.js";

const MAX_RESTARTS = 5;
const SERVER_EXE_RELATIVE_PATH = path.join("Server", "PrinciPal.Server.exe");
const DEV_MARKER_RELATIVE_PATH = path.join("Server", ".devproject");

interface ResolvedStartInfo {
    command: string;
    args: string[];
}

/**
 * Manages the MCP server child process lifecycle.
 * Port of C# ServerProcessManager — same ambassador pattern.
 *
 * Resolution order (matches C# ResolveStartInfo):
 * 1. Release: bundled Server/PrinciPal.Server.exe next to extension
 * 2. Debug:   Server/.devproject marker containing .csproj path → dotnet run
 * 3. Error:   no server found
 */
export class ServerProcessManager {
    private readonly _logger: IExtensionLogger;
    private readonly _extensionDir: string;
    private _process: ChildProcess | null = null;
    private _port: number = 0;
    private _restartCount: number = 0;
    private _disposed: boolean = false;

    constructor(logger: IExtensionLogger, extensionDir?: string) {
        this._logger = logger;
        // __dirname is out/ at runtime; extension root is one level up
        this._extensionDir = extensionDir ?? path.resolve(__dirname, "..");
    }

    get port(): number {
        return this._port;
    }

    async start(port: number): Promise<void> {
        if (this._disposed) return;
        this._port = port;
        this._restartCount = 0;

        const lockResult = ServerLockFile.tryAcquire(port);
        if (!lockResult.ok) {
            this._logger.log(`${lockResult.error.description} Waiting for health...`);
            const healthy = await this.waitForHealth(port, 10_000);
            if (healthy) {
                this._logger.log("MCP server is ready (started by another instance).");
            } else {
                this._logger.log("Timed out waiting for server started by another instance.");
            }
            return;
        }

        this.startProcess();

        if (this._process && !this._process.killed) {
            ServerLockFile.writeAndRelease(lockResult.value, this._process.pid!, port);
            // Wait for the server to be ready before returning so that
            // callers (e.g. session registration) can use it immediately.
            const healthy = await this.waitForHealth(port, 10_000);
            if (healthy) {
                this._logger.log("MCP server is ready.");
            } else {
                this._logger.log("Timed out waiting for MCP server to become healthy.");
            }
        } else {
            lockResult.value.close();
            ServerLockFile.remove(port);
        }
    }

    private startProcess(): void {
        const startInfoResult = this.resolveStartInfo();
        if (!startInfoResult.ok) {
            this._logger.log(startInfoResult.error.description);
            return;
        }

        const { command, args } = startInfoResult.value;
        this._logger.log(`Starting MCP server: ${command} ${args.join(" ")}`);

        this._process = spawn(command, args, {
            stdio: ["ignore", "pipe", "pipe"],
            detached: true,
        });

        this._process.stdout?.on("data", (data: Buffer) => {
            this._logger.log(data.toString().trimEnd());
        });

        this._process.stderr?.on("data", (data: Buffer) => {
            this._logger.log(`[stderr] ${data.toString().trimEnd()}`);
        });

        this._process.on("exit", (code) => {
            this.onProcessExited(code);
        });

        // Detach so the server survives VS Code exiting — matches C# Process.Start
        // behavior where the server is an independent process. The Quartz watchdog
        // is the sole authority on server shutdown.
        this._process.unref();

        this._logger.log(
            `MCP server started (PID ${this._process.pid}) on http://localhost:${this._port}/`
        );
    }

    /**
     * Two-tier server resolution matching C# ResolveStartInfo():
     * 1. Release — bundled Server/PrinciPal.Server.exe
     * 2. Debug  — Server/.devproject marker → dotnet run
     */
    private resolveStartInfo(): Result<ResolvedStartInfo> {
        const exePath = path.join(this._extensionDir, SERVER_EXE_RELATIVE_PATH);
        const devMarkerPath = path.join(this._extensionDir, DEV_MARKER_RELATIVE_PATH);
        const portArgs = ["--port", String(this._port)];

        // Release: bundled self-contained exe
        if (fs.existsSync(exePath)) {
            this._logger.log(`Using bundled server: ${exePath}`);
            return success<ResolvedStartInfo>({
                command: exePath,
                args: portArgs,
            });
        }

        // Debug: dev marker contains the .csproj path, use dotnet run
        if (fs.existsSync(devMarkerPath)) {
            const projectPath = fs.readFileSync(devMarkerPath, "utf-8").trim();
            if (fs.existsSync(projectPath)) {
                this._logger.log(`Dev mode: using dotnet run --project ${projectPath}`);
                return success<ResolvedStartInfo>({
                    command: "dotnet",
                    args: ["run", "--project", projectPath, "--no-build", "--", ...portArgs],
                });
            }
        }

        return failure(new ServerBinaryNotFoundError(
            `Looked for bundled exe at ${exePath} and dev marker at ${devMarkerPath}. Neither found.`
        ));
    }

    private onProcessExited(code: number | null): void {
        if (this._disposed) return;

        if (code === 0) {
            this._logger.log("MCP server exited normally.");
            return;
        }

        this._restartCount++;
        if (this._restartCount > MAX_RESTARTS) {
            this._logger.log(
                `MCP server crashed ${this._restartCount} times. Giving up.`
            );
            return;
        }

        this._logger.log(
            `MCP server crashed (exit code ${code}). Restarting (attempt ${this._restartCount}/${MAX_RESTARTS})...`
        );
        this.startProcess();
    }

    private async waitForHealth(port: number, timeoutMs: number): Promise<boolean> {
        const deadline = Date.now() + timeoutMs;
        while (Date.now() < deadline) {
            if (await ServerProcessManager.isServerRunning(port)) return true;
            await new Promise((r) => setTimeout(r, 500));
        }
        return false;
    }

    static async isServerRunning(port: number): Promise<boolean> {
        try {
            const resp = await fetch(`http://localhost:${port}/api/health`, {
                signal: AbortSignal.timeout(2000),
            });
            return resp.ok;
        } catch {
            return false;
        }
    }

    dispose(): void {
        if (this._disposed) return;
        this._disposed = true;

        // Ambassador pattern: the server's Quartz watchdog is the sole authority
        // on shutdown. Extensions just detach — the server will self-terminate
        // after all sessions deregister and the grace period expires.
        this._logger.log("Detaching from MCP server.");
        this._process = null;
    }
}
