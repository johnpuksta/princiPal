using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using PrinciPal.Common.Errors.Extension;
using PrinciPal.Common.Results;

namespace PrinciPal.VsExtension
{
    /// <summary>
    /// Coordinates MCP server startup across multiple VS extension instances
    /// using a lock file at %LOCALAPPDATA%\princiPal\server-{port}.lock.
    /// </summary>
    internal sealed class ServerLockFile
    {
        private static string GetLockFilePath(int port)
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "princiPal");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"server-{port}.lock");
        }

        /// <summary>
        /// Attempts to acquire the startup lock for the given port.
        /// If a stale lock file exists (PID dead), it is removed first.
        /// Returns a FileStream handle on success, or a typed error describing why the lock was not acquired.
        /// </summary>
        public static Result<FileStream> TryAcquire(int port)
        {
            var path = GetLockFilePath(port);

            // Check for stale lock
            if (File.Exists(path))
            {
                try
                {
                    var content = File.ReadAllText(path);
                    var pidStart = content.IndexOf("\"pid\":", StringComparison.Ordinal);
                    if (pidStart >= 0)
                    {
                        pidStart += 6;
                        var pidEnd = content.IndexOfAny(new[] { ',', '}' }, pidStart);
                        if (pidEnd > pidStart &&
                            int.TryParse(content.Substring(pidStart, pidEnd - pidStart).Trim(), out var pid))
                        {
                            try
                            {
                                Process.GetProcessById(pid);
                                // Process is alive — lock is valid
                                return new LockHeldError(port, pid);
                            }
                            catch (ArgumentException)
                            {
                                // Process is dead — stale lock
                                File.Delete(path);
                            }
                        }
                    }
                }
                catch
                {
                    // Can't read/parse — try to delete
                    try { File.Delete(path); }
                    catch { return new LockFileCorruptError(port); }
                }
            }

            try
            {
                return new FileStream(path, FileMode.CreateNew, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                // Another instance created the file between our check and CreateNew
                return new LockHeldError(port, 0);
            }
        }

        /// <summary>
        /// Writes the server PID/port info to the lock file and releases the exclusive handle
        /// so other instances can read it.
        /// </summary>
        public static void WriteAndRelease(FileStream handle, int pid, int port)
        {
            var json = $"{{\"pid\":{pid},\"port\":{port},\"started\":\"{DateTime.UtcNow:O}\"}}";
            var bytes = Encoding.UTF8.GetBytes(json);
            handle.Write(bytes, 0, bytes.Length);
            handle.Flush();
            handle.Dispose();
        }

        /// <summary>
        /// Deletes the lock file for the given port, if it exists.
        /// </summary>
        public static void Remove(int port)
        {
            var path = GetLockFilePath(port);
            try { File.Delete(path); } catch { }
        }
    }
}
