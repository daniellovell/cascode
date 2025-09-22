using Cascode.Workspace;
using System;
using System.Collections.Generic;
using System.IO;

namespace Cascode.Cli;

internal sealed class ShellState
{
    private const int MaxMessages = 1000;
    private readonly List<string> _messages = new();
    private readonly List<string> _history = new();

    public ShellState(string workspaceRoot)
    {
        WorkspaceRoot = workspaceRoot;
        _historyCursor = 0;
    }

    public string WorkspaceRoot { get; private set; }

    public string? PdkRoot { get; private set; }

    public WorkspaceScanResult? Scan { get; set; }

    public int? SelectedDeckIndex { get; set; }

    public IReadOnlyList<string> Messages => _messages;

    public int LogViewport { get; private set; } = 10;

    public int LogScrollOffset { get; private set; }

    public bool IsLogPinned => LogScrollOffset == 0;

    private int _historyCursor;

    public void SetWorkspace(string root)
    {
        var normalized = Path.GetFullPath(root);
        var changed = !string.Equals(normalized, WorkspaceRoot, StringComparison.OrdinalIgnoreCase);

        WorkspaceRoot = normalized;

        if (!changed)
        {
            return;
        }

        Scan = null;
        SelectedDeckIndex = null;
        _messages.Clear();
        _history.Clear();
        ResetHistoryCursor();
        LogScrollOffset = 0;
    }

    public void UpdatePdkRoot(string? root)
    {
        PdkRoot = root is null ? null : Path.GetFullPath(root);
    }

    public void AddMessage(string message)
    {
        if (_messages.Count >= MaxMessages)
        {
            _messages.RemoveAt(0);
        }

        _messages.Add(message);

        if (!IsLogPinned)
        {
            var maxOffset = Math.Max(0, _messages.Count - LogViewport);
            LogScrollOffset = Math.Min(LogScrollOffset + 1, maxOffset);
        }

        ClampScrollOffset();
    }

    public void RecordCommand(string command)
    {
        var trimmed = command.Trim();
        if (trimmed.Length == 0)
        {
            return;
        }

        AddMessage($"> {trimmed}");
        AddHistory(trimmed);
    }

    public void UpdateLogViewport(int viewport)
    {
        if (viewport <= 0)
        {
            return;
        }

        LogViewport = viewport;
        ClampScrollOffset();
    }

    public void ScrollLogUp(int lines)
    {
        var maxOffset = Math.Max(0, _messages.Count - LogViewport);
        if (maxOffset == 0)
        {
            LogScrollOffset = 0;
            return;
        }

        LogScrollOffset = Math.Clamp(LogScrollOffset + lines, 0, maxOffset);
    }

    public void ScrollLogDown(int lines)
    {
        if (_messages.Count <= LogViewport)
        {
            LogScrollOffset = 0;
            return;
        }

        LogScrollOffset = Math.Clamp(LogScrollOffset - lines, 0, Math.Max(0, _messages.Count - LogViewport));
    }

    public void ScrollLogHome()
    {
        var maxOffset = Math.Max(0, _messages.Count - LogViewport);
        LogScrollOffset = maxOffset;
    }

    public void ScrollLogEnd()
    {
        LogScrollOffset = 0;
    }

    public void PinLog() => LogScrollOffset = 0;

    public void ResetHistoryCursor()
    {
        _historyCursor = _history.Count;
    }

    public bool TryHistoryPrevious(out string command)
    {
        if (_history.Count == 0)
        {
            command = string.Empty;
            return false;
        }

        if (_historyCursor > 0)
        {
            _historyCursor--;
        }

        command = _history[_historyCursor];
        return true;
    }

    public bool TryHistoryNext(out string command)
    {
        if (_history.Count == 0)
        {
            command = string.Empty;
            return false;
        }

        if (_historyCursor < _history.Count - 1)
        {
            _historyCursor++;
            command = _history[_historyCursor];
            return true;
        }

        _historyCursor = _history.Count;
        command = string.Empty;
        return true;
    }

    private void AddHistory(string command)
    {
        if (_history.Count == 0 || !string.Equals(_history[^1], command, StringComparison.Ordinal))
        {
            _history.Add(command);
        }

        ResetHistoryCursor();
    }

    private void ClampScrollOffset()
    {
        var maxOffset = Math.Max(0, _messages.Count - LogViewport);
        LogScrollOffset = Math.Clamp(LogScrollOffset, 0, maxOffset);
    }
}
