using Cascode.Workspace;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.IO;

namespace Cascode.Cli;

internal enum ShellViewMode
{
    Home = 0,
    DeviceSummary,
    CharRead
}

internal sealed class ShellState
{
    private const int MaxMessages = 1000;
    private readonly List<string> _messages = new();
    private readonly object _messagesLock = new();
    private readonly List<string> _history = new();

    public ShellState(string workspaceRoot)
    {
        WorkspaceRoot = workspaceRoot;
        _historyCursor = 0;
    }

    public string WorkspaceRoot { get; private set; }

    public string? PdkRoot { get; private set; }

    public ILoggerFactory? LoggerFactory { get; private set; }

    public void SetLoggerFactory(ILoggerFactory factory)
    {
        LoggerFactory = factory;
    }

    public WorkspaceScanResult? Scan { get; set; }

    public int? SelectedDeckIndex { get; set; }

    public string[] GetMessagesSnapshot()
    {
        lock (_messagesLock)
        {
            return _messages.ToArray();
        }
    }

    public event Action? Changed;
    private void OnChanged()
    {
        // Notify subscribers best-effort. Today we expect at most one subscriber
        // (the interactive renderer). Guard in debug and isolate each handler.
        var handlers = Changed;
        if (handlers is null) return;

        System.Diagnostics.Debug.Assert(
            handlers.GetInvocationList().Length <= 1,
            "ShellState.Changed should have at most one subscriber.");

        foreach (Action h in handlers.GetInvocationList())
        {
            try { h(); }
            catch { /* best-effort: keep notifying others */ }
        }
    }

    public int LogViewport { get; private set; } = 10;

    public int LogScrollOffset { get; private set; }

    public bool IsLogPinned => LogScrollOffset == 0;

    public ShellViewMode ViewMode { get; private set; } = ShellViewMode.Home;

    public DeviceSummaryViewState? DeviceSummary { get; private set; }

    public CharReadViewState? CharRead { get; private set; }

    public int DeviceDetailOffset { get; private set; }

    public int DeviceDetailPageSize { get; private set; }

    // Characterization progress (for PDK batch runs)
    public bool CharJobActive { get; private set; }
    public int CharTotal { get; private set; }
    public int CharGenerated { get; private set; }
    public int CharRan { get; private set; }
    public int CharExported { get; private set; }
    public int CharSkipped { get; private set; }
    public string CharCurrent { get; private set; } = string.Empty;
    public string? CharCorner { get; private set; }
    public string? CharBackend { get; private set; }

    // Busy/prompt spinner state
    public bool IsBusy { get; private set; }
    public string BusyText { get; private set; } = string.Empty;
    private static readonly string[] SpinnerFrames = new[] { "⠋", "⠙", "⠸", "⠴", "⠦", "⠇" };
    private int _spinnerIndex;

    private int _historyCursor;
    private bool _hasStreamedOutput;

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
        lock (_messagesLock)
        {
            _messages.Clear();
        }
        _history.Clear();
        ResetHistoryCursor();
        LogScrollOffset = 0;
        ShowHome();
        _hasStreamedOutput = false;
    }

    public void UpdatePdkRoot(string? root)
    {
        PdkRoot = root is null ? null : Path.GetFullPath(root);
    }

    public void AddMessage(string message)
    {
        lock (_messagesLock)
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
        OnChanged();
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

    public bool HasStreamedOutput => _hasStreamedOutput;

    public void MarkStreamedOutput()
    {
        _hasStreamedOutput = true;
    }

    public void ResetStreamedOutput()
    {
        _hasStreamedOutput = false;
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
        var maxOffset = Math.Max(0, GetMessageCount() - LogViewport);
        if (maxOffset == 0)
        {
            LogScrollOffset = 0;
            return;
        }

        LogScrollOffset = Math.Clamp(LogScrollOffset + lines, 0, maxOffset);
    }

    public void ScrollLogDown(int lines)
    {
        var messageCount = GetMessageCount();
        if (messageCount <= LogViewport)
        {
            LogScrollOffset = 0;
            return;
        }

        LogScrollOffset = Math.Clamp(LogScrollOffset - lines, 0, Math.Max(0, messageCount - LogViewport));
    }

    public void ScrollLogHome()
    {
        var maxOffset = Math.Max(0, GetMessageCount() - LogViewport);
        LogScrollOffset = maxOffset;
    }

    public void ScrollLogEnd()
    {
        LogScrollOffset = 0;
    }

    public void PinLog() => LogScrollOffset = 0;

    public void StartBusy(string text)
    {
        IsBusy = true;
        BusyText = text ?? string.Empty;
        _spinnerIndex = 0;
        OnChanged();
    }

    public void StopBusy()
    {
        IsBusy = false;
        BusyText = string.Empty;
        _spinnerIndex = 0;
        OnChanged();
    }

    public string GetSpinnerFrame()
    {
        return SpinnerFrames[_spinnerIndex % SpinnerFrames.Length];
    }

    // Advance the spinner independently of log writes while busy.
    public void TickSpinner()
    {
        if (!IsBusy) return;
        _spinnerIndex = (_spinnerIndex + 1) % SpinnerFrames.Length;
        // No OnChanged() here: the interactive loop refreshes on a timer.
    }

    public void ShowHome()
    {
        ViewMode = ShellViewMode.Home;
        DeviceSummary = null;
        CharRead = null;
        DeviceDetailOffset = 0;
        DeviceDetailPageSize = 0;
    }

    public void StartCharJob(int total, string backend, string? corner)
    {
        CharJobActive = true;
        CharTotal = Math.Max(0, total);
        CharGenerated = 0;
        CharRan = 0;
        CharExported = 0;
        CharSkipped = 0;
        CharCurrent = string.Empty;
        CharCorner = corner;
        CharBackend = backend;
    }

    public void UpdateCharProgress(string current, int? generatedDelta = null, int? ranDelta = null, int? exportedDelta = null, int? skippedDelta = null)
    {
        if (!CharJobActive) return;
        CharCurrent = current ?? string.Empty;
        if (generatedDelta.HasValue) CharGenerated += Math.Max(0, generatedDelta.Value);
        if (ranDelta.HasValue) CharRan += Math.Max(0, ranDelta.Value);
        if (exportedDelta.HasValue) CharExported += Math.Max(0, exportedDelta.Value);
        if (skippedDelta.HasValue) CharSkipped += Math.Max(0, skippedDelta.Value);
    }

    public void CompleteCharJob()
    {
        CharJobActive = false;
    }

    public bool TrySetDeviceDetailOffset(int offset)
    {
        if (DeviceSummary is null || !DeviceSummary.HasDetailRows)
        {
            return false;
        }

        var pageSize = DeviceDetailPageSize > 0 ? DeviceDetailPageSize : DeviceSummary.DetailRows.Count;
        var maxOffset = Math.Max(0, DeviceSummary.DetailRows.Count - pageSize);
        offset = Math.Clamp(offset, 0, maxOffset);
        if (offset == DeviceDetailOffset)
        {
            return false;
        }

        DeviceDetailOffset = offset;
        return true;
    }

    public void ShowDeviceSummary(DeviceSummaryViewState summary)
    {
        ReplaceDeviceSummary(summary ?? throw new ArgumentNullException(nameof(summary)));
        ViewMode = ShellViewMode.DeviceSummary;
    }

    public void ShowCharRead(CharReadViewState view)
    {
        CharRead = view ?? throw new ArgumentNullException(nameof(view));
        ViewMode = ShellViewMode.CharRead;
    }

    public void ReplaceDeviceSummary(DeviceSummaryViewState summary)
    {
        DeviceSummary = summary;
        if (summary.HasDetailRows)
        {
            DeviceDetailPageSize = summary.DetailPageSize > 0 ? summary.DetailPageSize : summary.DetailRows.Count;
            DeviceDetailOffset = Math.Clamp(summary.DetailOffset, 0, Math.Max(0, summary.DetailRows.Count - DeviceDetailPageSize));
        }
        else
        {
            DeviceDetailPageSize = 0;
            DeviceDetailOffset = 0;
        }
    }

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
        var count = GetMessageCount();
        var maxOffset = Math.Max(0, count - LogViewport);
        LogScrollOffset = Math.Clamp(LogScrollOffset, 0, maxOffset);
    }

    public void RequestRender() => OnChanged();

    private int GetMessageCount()
    {
        lock (_messagesLock)
        {
            return _messages.Count;
        }
    }
}
