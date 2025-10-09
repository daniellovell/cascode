using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Cascode.Workspace;

public sealed class PhysicalLibraryScanner
{
    private static readonly string[] LayoutViews = { "layout", "layoutxl", "layoutxl1" };
    private static readonly string[] SymbolViews = { "symbol", "symbolic" };

    public List<Device> Scan(IReadOnlyList<WorkspaceLibrary> libraries, ICollection<string>? warnings = null)
    {
        var devices = new List<Device>();
        foreach (var lib in libraries)
        {
            try
            {
                var libPath = lib.Path;
                if (!Directory.Exists(libPath))
                {
                    warnings?.Add($"Library path not found: {libPath}");
                    continue;
                }

                foreach (var cellDir in Directory.EnumerateDirectories(libPath))
                {
                    var cellName = Path.GetFileName(cellDir) ?? cellDir;
                    var views = SafeListDir(cellDir).Select(Path.GetFileName).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).ToList();
                    var hasLayout = views.Any(v => LayoutViews.Contains(v!, StringComparer.OrdinalIgnoreCase));
                    var hasSymbol = views.Any(v => SymbolViews.Contains(v!, StringComparer.OrdinalIgnoreCase));
                    if (!hasLayout || !hasSymbol) continue;

                    var cls = NameNormalization.ClassifyByName(cellName);
                    var subclass = NameNormalization.ClassifySubclass(cellName);
                    var vt = NameNormalization.ExtractVtTags(cellName);
                    var vdd = NameNormalization.ExtractVddTags(cellName);
                    var tags = new List<string>();
                    if (NameNormalization.LooksInfra(cellName)) tags.Add("infra");

                    devices.Add(new Device
                    {
                        LibraryName = lib.Name,
                        LibraryPath = libPath,
                        CellName = cellName,
                        CellPath = cellDir,
                        Class = cls,
                        Subclass = subclass,
                        HasLayout = hasLayout,
                        HasSymbol = hasSymbol,
                        Views = views,
                        VtTags = vt,
                        VddTags = vdd,
                        Tags = tags
                    });
                }
            }
            catch (Exception ex)
            {
                warnings?.Add($"Failed to scan library '{lib.Name}': {ex.Message}");
            }
        }
        return devices;
    }

    private static IEnumerable<string> SafeListDir(string dir)
    {
        try { return Directory.EnumerateDirectories(dir); }
        catch { return Array.Empty<string>(); }
    }
}

