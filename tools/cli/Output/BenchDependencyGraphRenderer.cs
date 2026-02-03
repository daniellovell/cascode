using System;
using System.Collections.Generic;
using System.Linq;
using Cascode.Language.BenchRuntime;
using Spectre.Console;

namespace Cascode.Cli.Output;

internal static class BenchDependencyGraphRenderer
{
    public static void Render(BenchDependencyGraph graph, ICliOutput output)
    {
        if (graph.InvocationsById.Count == 0)
        {
            return;
        }

        if (output.Mode == CliOutputMode.Spectre && output.Err is not null)
        {
            RenderSpectre(graph, output.Err);
            return;
        }

        RenderPlain(graph, output.WriteErrorLine);
    }

    private static void RenderSpectre(BenchDependencyGraph graph, IAnsiConsole console)
    {
        var dependents = BuildDependentsMap(graph);
        var roots = GetRoots(graph);

        var tree = new Tree(
            $"[grey]Dependency Graph ({graph.InvocationsById.Count} measurements)[/]"
        ).Guide(TreeGuide.Line);

        foreach (var rootId in roots.OrderBy(r => r, StringComparer.Ordinal))
        {
            var rootNode = tree.AddNode(FormatInvocation(rootId));
            AddDependents(rootNode, rootId, dependents, graph.DependenciesById);
        }

        console.Write(tree);
        console.WriteLine();
    }

    private static void AddDependents(
        TreeNode parent,
        string nodeId,
        IReadOnlyDictionary<string, HashSet<string>> dependents,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> dependenciesById
    )
    {
        if (!dependents.TryGetValue(nodeId, out var children) || children.Count == 0)
        {
            return;
        }

        foreach (var childId in children.OrderBy(c => c, StringComparer.Ordinal))
        {
            var label = FormatInvocationWithOtherDeps(childId, nodeId, dependenciesById);
            var childNode = parent.AddNode(label);
            AddDependents(childNode, childId, dependents, dependenciesById);
        }
    }

    private static string FormatInvocation(string invocationId)
    {
        return Markup.Escape(invocationId);
    }

    private static string FormatInvocationWithOtherDeps(
        string invocationId,
        string primaryDep,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> dependenciesById
    )
    {
        if (!dependenciesById.TryGetValue(invocationId, out var deps) || deps.Count <= 1)
        {
            return Markup.Escape(invocationId);
        }

        var otherDeps = deps.Where(d => !d.Equals(primaryDep, StringComparison.Ordinal))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        if (otherDeps.Count == 0)
        {
            return Markup.Escape(invocationId);
        }

        return $"{Markup.Escape(invocationId)} [grey](also: {Markup.Escape(string.Join(", ", otherDeps))})[/]";
    }

    private static void RenderPlain(BenchDependencyGraph graph, Action<string> writeLine)
    {
        var dependents = BuildDependentsMap(graph);
        var roots = GetRoots(graph);

        writeLine($"Dependency Graph ({graph.InvocationsById.Count} measurements)");

        foreach (var rootId in roots.OrderBy(r => r, StringComparer.Ordinal))
        {
            writeLine(rootId);
            RenderPlainChildren(writeLine, rootId, dependents, graph.DependenciesById, "  ");
        }

        writeLine("");
    }

    private static void RenderPlainChildren(
        Action<string> writeLine,
        string nodeId,
        IReadOnlyDictionary<string, HashSet<string>> dependents,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> dependenciesById,
        string indent
    )
    {
        if (!dependents.TryGetValue(nodeId, out var children) || children.Count == 0)
        {
            return;
        }

        foreach (var childId in children.OrderBy(c => c, StringComparer.Ordinal))
        {
            var label = FormatPlainWithOtherDeps(childId, nodeId, dependenciesById);
            writeLine($"{indent}{label}");
            RenderPlainChildren(writeLine, childId, dependents, dependenciesById, indent + "  ");
        }
    }

    private static string FormatPlainWithOtherDeps(
        string invocationId,
        string primaryDep,
        IReadOnlyDictionary<string, IReadOnlyCollection<string>> dependenciesById
    )
    {
        if (!dependenciesById.TryGetValue(invocationId, out var deps) || deps.Count <= 1)
        {
            return invocationId;
        }

        var otherDeps = deps.Where(d => !d.Equals(primaryDep, StringComparison.Ordinal))
            .OrderBy(d => d, StringComparer.Ordinal)
            .ToList();

        if (otherDeps.Count == 0)
        {
            return invocationId;
        }

        return $"{invocationId} (also: {string.Join(", ", otherDeps)})";
    }

    private static IReadOnlyList<string> GetRoots(BenchDependencyGraph graph)
    {
        return graph
            .DependenciesById.Where(kvp => kvp.Value.Count == 0)
            .Select(kvp => kvp.Key)
            .ToList();
    }

    private static Dictionary<string, HashSet<string>> BuildDependentsMap(
        BenchDependencyGraph graph
    )
    {
        var dependents = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var id in graph.InvocationsById.Keys)
        {
            dependents.TryAdd(id, new HashSet<string>(StringComparer.Ordinal));
        }

        foreach (var (nodeId, deps) in graph.DependenciesById)
        {
            foreach (var dep in deps)
            {
                if (!dependents.TryGetValue(dep, out var list))
                {
                    list = new HashSet<string>(StringComparer.Ordinal);
                    dependents[dep] = list;
                }
                list.Add(nodeId);
            }
        }

        return dependents;
    }
}
