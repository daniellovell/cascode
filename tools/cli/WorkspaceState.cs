using System;
using System.IO;

namespace Cascode.Cli;

/// <summary>
/// CLI-specific workspace state utilities.
/// Delegates to Cascode.Workspace.WorkspacePaths for core path computation.
/// </summary>
internal static class WorkspaceState
{
    /// <summary>
    /// Determine the filesystem root directory used to store Cascode state.
    /// </summary>
    public static string GetRoot() => Cascode.Workspace.WorkspacePaths.GetCascodeHome();

    /// <summary>
    /// Compute the per-workspace directory path used to store workspace-specific Cascode state.
    /// </summary>
    public static string GetWorkspaceFolder(string workspaceRoot) =>
        Cascode.Workspace.WorkspacePaths.GetWorkspaceFolder(workspaceRoot);

    public static string GetCharacterizationFolder(string workspaceRoot) =>
        Cascode.Workspace.WorkspacePaths.GetCharacterizationFolder(workspaceRoot);

    public static string GetConfigPath() => Path.Combine(GetRoot(), "config.json");

    public static string GetCharConfigPath(string workspaceRoot) =>
        Path.Combine(GetWorkspaceFolder(workspaceRoot), "pdk-char-config.json");
}
