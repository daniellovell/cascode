using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Cascode.Workspace;

namespace Cascode.Workspace.Tests;

public sealed class WorkspaceScannerLibInitTests
{
    [Fact]
    public void ScanRejectsNonSpectreModelFiles()
    {
        using var workspace = TemporaryWorkspace.Create();

        // Root cds.lib includes a PDK cds.lib located deeper in the tree.
        workspace.WriteFile(
            "cds.lib",
            $"INCLUDE ./pdk/cds.lib{Environment.NewLine}");

        // The PDK cds.lib defines a library that uses a libInit.il to register model decks.
        workspace.WriteFile(
            Path.Combine("pdk", "cds.lib"),
            $"DEFINE vendorAnalog ./vendorAnalog{Environment.NewLine}");

        workspace.CreateDirectory(Path.Combine("pdk", "vendorAnalog"));

        var modelsDir = workspace.CreateDirectory(Path.Combine("pdk", "models", "spectre"));

        // Create files with different extensions - only .scs should be accepted
        var validModelPath = Path.Combine(modelsDir, "analog_models_v1.scs");
        var eldoPath = Path.Combine(modelsDir, "toplevel.eldo");
        var hspicePath = Path.Combine(modelsDir, "toplevel.l");
        var addedPath = Path.Combine(modelsDir, "source.added");

        File.WriteAllText(validModelPath, "simulator lang=spectre");
        File.WriteAllText(eldoPath, "eldo model");
        File.WriteAllText(hspicePath, "hspice model");
        File.WriteAllText(addedPath, "added content");

        // The libInit.il references all four files
        var libInitContent = $@";
; setup spectre model files, sections
    if(isContextLoaded(""schView"") then

    foreach( sim list( 'ams 'UltraSim 'spectre)
       envSetVal(""asimenv.startup"" ""simulator"" 'string sprintf(nil ""%s"" sim))
      tools = envGetAvailableTools()
      if(member(sprintf(nil ""%s"" sim) tools) then
       asiSetEnvOptionVal(asiGetTool(sim) ""modelFiles""
         list(
	list(strcat( libPath ""/../models/spectre/analog_models_v1.scs"") ""probe"")
	list(strcat( libPath ""/../models/spectre/toplevel.eldo"") ""probe"")
	list(strcat( libPath ""/../models/spectre/toplevel.l"") ""probe"")
	list(strcat( libPath ""/../models/spectre/source.added"") ""probe"")
          )
        ) ; end asiSetEnvOptionVal
      ) ; end member
    ) ; end foreach
  ) ; end if
";

        workspace.WriteFile(Path.Combine("pdk", "vendorAnalog", "libInit.il"), libInitContent);

        var scanner = new WorkspaceScanner();

        var result = scanner.Scan(workspace.RootPath);

        // Should only find the .scs file
        var normalizedValidPath = Path.GetFullPath(validModelPath);
        Assert.Single(result.ModelDecks);
        Assert.Contains(
            normalizedValidPath,
            result.ModelDecks.Select(deck => deck.DeckPath),
            StringComparer.OrdinalIgnoreCase);

        // Should NOT find the non-Spectre files
        Assert.DoesNotContain(
            Path.GetFullPath(eldoPath),
            result.ModelDecks.Select(deck => deck.DeckPath),
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.GetFullPath(hspicePath),
            result.ModelDecks.Select(deck => deck.DeckPath),
            StringComparer.OrdinalIgnoreCase);
        Assert.DoesNotContain(
            Path.GetFullPath(addedPath),
            result.ModelDecks.Select(deck => deck.DeckPath),
            StringComparer.OrdinalIgnoreCase);
    }

    [Fact]
    public void ScanFindsModelDecksDeclaredInNestedLibInit()
    {
        using var workspace = TemporaryWorkspace.Create();

        // Root cds.lib includes a PDK cds.lib located deeper in the tree.
        workspace.WriteFile(
            "cds.lib",
            $"INCLUDE ./pdk/cds.lib{Environment.NewLine}");

        // The PDK cds.lib defines a library that uses a libInit.il to register model decks.
        workspace.WriteFile(
            Path.Combine("pdk", "cds.lib"),
            $"DEFINE vendorAnalog ./vendorAnalog{Environment.NewLine}");

        workspace.CreateDirectory(Path.Combine("pdk", "vendorAnalog"));

        var modelsDir = workspace.CreateDirectory(Path.Combine("pdk", "models", "spectre"));
        var modelPath = Path.Combine(modelsDir, "analog_models_v1.scs");
        File.WriteAllText(modelPath, "simulator deck");

        // The libInit.il relies on libPath and relative navigation to point to model files.
        var libInitContent = $@";
; setup spectre model files, sections
    if(isContextLoaded(""schView"") then

    foreach( sim list( 'ams 'UltraSim 'spectre)
       envSetVal(""asimenv.startup"" ""simulator"" 'string sprintf(nil ""%s"" sim))
      tools = envGetAvailableTools()
      if(member(sprintf(nil ""%s"" sim) tools) then
       asiSetEnvOptionVal(asiGetTool(sim) ""modelFiles""
         list(
	list(strcat( libPath ""/../models/spectre/analog_models_v1.scs"") ""probe"")
          )
        ) ; end asiSetEnvOptionVal
      ) ; end member
    ) ; end foreach
  ) ; end if
";

        workspace.WriteFile(Path.Combine("pdk", "vendorAnalog", "libInit.il"), libInitContent);

        var scanner = new WorkspaceScanner();

        var result = scanner.Scan(workspace.RootPath);

        Assert.Empty(result.Warnings);

        var normalizedDeckPath = Path.GetFullPath(modelPath);
        Assert.Contains(
            normalizedDeckPath,
            result.ModelDecks.Select(deck => deck.DeckPath),
            StringComparer.OrdinalIgnoreCase);
    }

    private sealed class TemporaryWorkspace : IDisposable
    {
        private TemporaryWorkspace(string rootPath)
        {
            RootPath = rootPath;
        }

        public string RootPath { get; }

        public static TemporaryWorkspace Create()
        {
            var root = Path.Combine(Path.GetTempPath(), $"cascode-workspace-test-{Guid.NewGuid():N}");
            Directory.CreateDirectory(root);
            return new TemporaryWorkspace(root);
        }

        public string CreateDirectory(string relativePath)
        {
            var path = Path.Combine(RootPath, relativePath);
            Directory.CreateDirectory(path);
            return path;
        }

        public void WriteFile(string relativePath, string contents)
        {
            var fullPath = Path.Combine(RootPath, relativePath);
            var directory = Path.GetDirectoryName(fullPath);
            if (!string.IsNullOrEmpty(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.WriteAllText(fullPath, contents);
        }

        public void Dispose()
        {
            try
            {
                if (Directory.Exists(RootPath))
                {
                    Directory.Delete(RootPath, true);
                }
            }
            catch
            {
                // best effort cleanup
            }
        }
    }
}
