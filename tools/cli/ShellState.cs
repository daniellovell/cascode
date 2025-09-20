using Cascode.Workspace;

namespace Cascode.Cli;

internal sealed class ShellState
{
    private readonly List<string> _messages = new();

    public ShellState(string workspaceRoot)
    {
        WorkspaceRoot = workspaceRoot;
    }

    public string WorkspaceRoot { get; private set; }

    public WorkspaceScanResult? Scan { get; set; }

    public int? SelectedDeckIndex { get; set; }

    public IReadOnlyList<string> Messages => _messages;

    public void SetWorkspace(string root)
    {
        WorkspaceRoot = Path.GetFullPath(root);
        Scan = null;
        SelectedDeckIndex = null;
        _messages.Clear();
    }

    public void AddMessage(string message)
    {
        if (_messages.Count > 200)
        {
            _messages.RemoveAt(0);
        }
        _messages.Add(message);
    }
}
