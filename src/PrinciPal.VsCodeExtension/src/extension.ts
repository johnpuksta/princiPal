import * as vscode from "vscode";
import * as crypto from "node:crypto";
import { OutputLogger } from "./OutputLogger.js";
import { VsCodeDebuggerAdapter } from "./adapters/VsCodeDebuggerAdapter.js";
import { HttpDebugStatePublisher } from "./adapters/HttpDebugStatePublisher.js";
import { DebugEventCoordinator } from "./services/DebugEventCoordinator.js";
import { DebugAdapterTrackerFactory } from "./services/DebugAdapterTrackerFactory.js";
import { ServerProcessManager } from "./services/ServerProcessManager.js";

let logger: OutputLogger | undefined;
let publisher: HttpDebugStatePublisher | undefined;
let processManager: ServerProcessManager | undefined;
let coordinator: DebugEventCoordinator | undefined;

export async function activate(
    context: vscode.ExtensionContext
): Promise<void> {
    logger = new OutputLogger();

    // Read configuration
    const config = vscode.workspace.getConfiguration("princiPal");
    const port = config.get<number>("port", 9229);
    const autoStart = config.get<boolean>("autoStart", true);

    // Compute session ID (SHA256 of workspace folder path, matching C# pattern)
    const workspaceFolders = vscode.workspace.workspaceFolders;
    let sessionId: string;
    let sessionName: string;
    let workspacePath: string;

    if (workspaceFolders && workspaceFolders.length > 0) {
        workspacePath = workspaceFolders[0].uri.fsPath;
        sessionName = workspaceFolders[0].name;
        const hash = crypto
            .createHash("sha256")
            .update(workspacePath.toLowerCase())
            .digest("hex");
        sessionId = hash.substring(0, 8);
    } else {
        sessionId = `vscode-${process.pid}`;
        sessionName = sessionId;
        workspacePath = "";
    }

    // Auto-start server if configured
    if (autoStart) {
        const running = await ServerProcessManager.isServerRunning(port);
        if (running) {
            logger.log(`Existing princiPal server detected on port ${port}. Reusing it.`);
        } else {
            processManager = new ServerProcessManager(logger);
            await processManager.start(port);
        }

        const mcpUrl = `http://localhost:${port}/`;
        logger.log(
            `MCP config: { "mcpServers": { "princiPal": { "url": "${mcpUrl}" } } }`
        );
    } else {
        logger.log(
            `Auto-start disabled. Start the MCP server manually on port ${port}.`
        );
    }

    // Create the adapter, publisher, coordinator
    const adapter = new VsCodeDebuggerAdapter();
    publisher = new HttpDebugStatePublisher(
        port,
        sessionId,
        sessionName,
        workspacePath
    );
    coordinator = new DebugEventCoordinator(adapter, publisher, logger);

    // Register the debug adapter tracker factory (intercepts DAP events)
    const trackerFactory = new DebugAdapterTrackerFactory(
        adapter,
        coordinator,
        logger
    );
    context.subscriptions.push(
        vscode.debug.registerDebugAdapterTrackerFactory("*", trackerFactory)
    );

    // Register session eagerly on activation (matches VS extension behavior)
    const regResult = await coordinator.register();
    if (!regResult.ok) {
        logger.log(regResult.error.description);
    }

    // Cleanup on deactivate — deregister must happen before publisher is disposed.
    context.subscriptions.push({
        dispose() {
            processManager?.dispose();
            logger?.dispose();
        },
    });

    logger.log(`Session: ${sessionName} [${sessionId}]`);
    logger.log("Extension activated.");
}

export async function deactivate(): Promise<void> {
    // Best-effort deregistration with bounded wait (matches VS extension's 3s timeout).
    // Must complete before publisher is disposed, since dispose() aborts in-flight requests.
    try {
        const timeout = new Promise<void>((resolve) => setTimeout(resolve, 3000));
        await Promise.race([coordinator?.deregister(), timeout]);
    } catch {
        // best-effort
    } finally {
        publisher?.dispose();
    }
}
