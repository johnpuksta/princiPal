using System;
using System.Collections.Generic;
using System.Diagnostics;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio.Shell;
using PrinciPal.Common.Errors.Extension;
using PrinciPal.Common.Results;
using PrinciPal.Domain.ValueObjects;
using PrinciPal.VsExtension.Abstractions;

namespace PrinciPal.VsExtension.Adapters
{
    /// <summary>
    /// Reads debugger state from the DTE2 COM automation model.
    /// All methods must be called on the UI thread.
    /// </summary>
    public sealed class VsDebuggerAdapter : IDebuggerReader
    {
        private readonly DTE2 _dte;

        public VsDebuggerAdapter(DTE2 dte)
        {
            _dte = dte;
        }

        public bool IsInBreakMode
        {
            get
            {
                ThreadHelper.ThrowIfNotOnUIThread();
                return _dte.Debugger.CurrentMode == dbgDebugMode.dbgBreakMode;
            }
        }

        public bool IsDebuggingVisualStudio()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var processes = _dte.Debugger.DebuggedProcesses;
                if (processes == null) return false;

                for (int i = 1; i <= processes.Count; i++)
                {
                    var name = processes.Item(i).Name;
                    if (name.EndsWith("devenv.exe", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"PrinciPal: Error checking debugged processes: {ex.Message}");
            }
            return false;
        }

        public Result<SourceLocation> ReadCurrentLocation()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var currentFrame = _dte.Debugger.CurrentStackFrame;
                if (currentFrame == null)
                    return Result.Failure<SourceLocation>(new ComReadError("current stack frame"));

                var location = new SourceLocation
                {
                    FunctionName = currentFrame.FunctionName
                };

                var doc = _dte.ActiveDocument;
                if (doc != null)
                {
                    location.FilePath = doc.FullName;
                    location.ProjectName = doc.ProjectItem?.ContainingProject?.Name;

                    if (doc.Selection is TextSelection selection)
                    {
                        location.Line = selection.CurrentLine;
                        location.Column = selection.CurrentColumn;
                    }
                }

                return location;
            }
            catch (Exception ex)
            {
                return Result.Failure<SourceLocation>(new ComReadError("location", ex.Message));
            }
        }

        public Result<List<LocalVariable>> ReadLocals(int maxDepth = 2)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var currentFrame = _dte.Debugger.CurrentStackFrame;
                if (currentFrame == null)
                    return new List<LocalVariable>();

                return ReadExpressions(currentFrame.Locals, maxDepth);
            }
            catch (Exception ex)
            {
                return Result.Failure<List<LocalVariable>>(new ComReadError("locals", ex.Message));
            }
        }

        public Result<List<StackFrameInfo>> ReadCallStack(int maxFrames = 20)
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var frames = new List<StackFrameInfo>();
                var thread = _dte.Debugger.CurrentThread;
                if (thread == null)
                    return frames;

                var stackFrames = thread.StackFrames;
                for (int i = 1; i <= Math.Min(stackFrames.Count, maxFrames); i++)
                {
                    var frame = stackFrames.Item(i);
                    frames.Add(new StackFrameInfo
                    {
                        Index = i,
                        FunctionName = frame.FunctionName,
                        Language = frame.Language,
                        Module = frame.Module
                    });
                }

                return frames;
            }
            catch (Exception ex)
            {
                return Result.Failure<List<StackFrameInfo>>(new ComReadError("call stack", ex.Message));
            }
        }

        public Result<List<BreakpointInfo>> ReadBreakpoints()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            try
            {
                var breakpoints = new List<BreakpointInfo>();
                var bps = _dte.Debugger.Breakpoints;
                for (int i = 1; i <= bps.Count; i++)
                {
                    var bp = bps.Item(i);
                    breakpoints.Add(new BreakpointInfo
                    {
                        FilePath = bp.File,
                        Line = bp.FileLine,
                        Column = bp.FileColumn,
                        FunctionName = bp.FunctionName,
                        Enabled = bp.Enabled,
                        Condition = bp.Condition
                    });
                }

                return breakpoints;
            }
            catch (Exception ex)
            {
                return Result.Failure<List<BreakpointInfo>>(new ComReadError("breakpoints", ex.Message));
            }
        }

        private List<LocalVariable> ReadExpressions(Expressions expressions, int maxDepth, int currentDepth = 0)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var variables = new List<LocalVariable>();
            if (expressions == null || currentDepth > maxDepth) return variables;

            for (int i = 1; i <= expressions.Count; i++)
            {
                try
                {
                    var expr = expressions.Item(i);
                    var variable = new LocalVariable
                    {
                        Name = expr.Name,
                        Value = expr.Value,
                        Type = expr.Type,
                        IsValidValue = expr.IsValidValue
                    };

                    if (expr.DataMembers != null && expr.DataMembers.Count > 0 && currentDepth < maxDepth)
                    {
                        variable.Members = ReadExpressions(expr.DataMembers, maxDepth, currentDepth + 1);
                    }

                    variables.Add(variable);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"PrinciPal: Error reading expression at index {i}: {ex.Message}");
                }
            }

            return variables;
        }
    }
}
