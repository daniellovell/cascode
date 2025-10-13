using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Microsoft.Data.Sqlite;

namespace Cascode.Workspace;

public static class PdkDatabaseReader
{
    private static IReadOnlyList<string> SplitCsv(string? csv)
        => string.IsNullOrWhiteSpace(csv) ? Array.Empty<string>() : csv.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
    /// <summary>
    /// Load device metadata from the specified PDK database.
    /// </summary>
    /// <param name="dbPath">Filesystem path to the read-only PDK SQLite database file.</param>
    /// <returns>An IReadOnlyList of Device objects representing devices found in the database (empty if none).</returns>
    public static IReadOnlyList<Device> LoadDevices(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        var devices = new List<Device>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT d.canonical_name, d.display_name, d.lib_name, d.lib_path, d.cell_name, d.cell_path,
                       d.device_class, d.device_subclass, d.has_layout, d.has_symbol, d.vt_tags, d.vdd_tags, d.tags,
                       GROUP_CONCAT(v.view)
                FROM devices d LEFT JOIN device_views v ON d.id=v.device_id
                GROUP BY d.id
                ORDER BY d.lib_name, d.cell_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var viewsCsv = reader.IsDBNull(13) ? string.Empty : reader.GetString(13);
                devices.Add(new Device
                {
                    LibraryName = reader.GetString(2),
                    LibraryPath = reader.GetString(3),
                    CellName = reader.GetString(4),
                    CellPath = reader.GetString(5),
                    Class = (DeviceClass)reader.GetInt32(6),
                    Subclass = (DeviceSubclass)reader.GetInt32(7),
                    HasLayout = reader.GetInt32(8) != 0,
                    HasSymbol = reader.GetInt32(9) != 0,
                    VtTags = SplitCsv(reader.IsDBNull(10) ? null : reader.GetString(10)),
                    VddTags = SplitCsv(reader.IsDBNull(11) ? null : reader.GetString(11)),
                    Tags = SplitCsv(reader.IsDBNull(12) ? null : reader.GetString(12)),
                    Views = SplitCsv(viewsCsv)
                });
            }
        }
        return devices;
    }
    /// <summary>
    /// Loads Spectre models and their associated metadata from the specified PDK database.
    /// </summary>
    /// <param name="dbPath">File path to the PDK SQLite database.</param>
    /// <returns>An array of SpectreModel instances populated with name, type, device class, voltage domain, threshold flavor and associated source files, decks, corners, corner details, and sections. Models are returned ordered by name.</returns>
    public static IReadOnlyList<SpectreModel> LoadModels(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);

        var models = new List<(long Id, SpectreModel Model)>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT id, name, model_type, device_class, voltage_domain, threshold_flavor FROM models ORDER BY name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var name = reader.GetString(1);
                var type = reader.GetString(2);
                var cls = (DeviceClass)reader.GetInt32(3);
                var vdd = reader.IsDBNull(4) ? null : reader.GetString(4);
                var vt = reader.IsDBNull(5) ? null : reader.GetString(5);
                var model = new SpectreModel(name, type, cls, vdd, vt, SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, SpectreModel.EmptyStringList, SpectreModel.EmptyStringList);
                models.Add((id, model));
            }
        }

        if (models.Count == 0) return Array.Empty<SpectreModel>();

        // Load sources
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT model_id, path FROM model_sources";
            using var reader = cmd.ExecuteReader();
            var map = models.ToDictionary(m => m.Id, m => new List<string>());
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var path = reader.GetString(1);
                if (map.TryGetValue(id, out var list)) list.Add(path);
            }
            foreach (var (id, model) in models)
            {
                if (map.TryGetValue(id, out var list)) model.SourceFiles = list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        // Load decks
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT model_id, path FROM model_decks";
            using var reader = cmd.ExecuteReader();
            var map = models.ToDictionary(m => m.Id, m => new List<string>());
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                var path = reader.GetString(1);
                if (map.TryGetValue(id, out var list)) list.Add(path);
            }
            foreach (var (id, model) in models)
            {
                if (map.TryGetValue(id, out var list)) model.Decks = list.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            }
        }

        // Load corners/details/sections from model_contexts (table guaranteed to exist in schema)
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT mc.model_id, c.name AS corner, d.name AS detail, s.name AS section
                                 FROM model_contexts mc
                                 LEFT JOIN corners c ON c.id=mc.corner_id
                                 LEFT JOIN details d ON d.id=mc.detail_id
                                 LEFT JOIN sections s ON s.id=mc.section_id";
            using var reader = cmd.ExecuteReader();
            var cornersMap = models.ToDictionary(m => m.Id, m => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var detailsMap = models.ToDictionary(m => m.Id, m => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            var sectionsMap = models.ToDictionary(m => m.Id, m => new HashSet<string>(StringComparer.OrdinalIgnoreCase));
            while (reader.Read())
            {
                var id = reader.GetInt64(0);
                if (!reader.IsDBNull(1)) cornersMap[id].Add(reader.GetString(1));
                if (!reader.IsDBNull(2)) detailsMap[id].Add(reader.GetString(2));
                if (!reader.IsDBNull(3)) sectionsMap[id].Add(reader.GetString(3));
            }
            foreach (var (id, model) in models)
            {
                model.Corners = cornersMap[id].ToArray();
                model.CornerDetails = detailsMap[id].ToArray();
                model.Sections = sectionsMap[id].ToArray();
            }
        }

        return models.Select(m => m.Model).ToArray();
    }

    // Efficient, server-side filtered device listing for TUI screens.
    // Returns devices with aggregated views; supports optional class filter and paging.
    public static IReadOnlyList<Device> LoadDevicesFiltered(string dbPath, DeviceClass? classFilter, int limit, int offset)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        var where = classFilter.HasValue ? "WHERE d.device_class=$cls" : string.Empty;
        cmd.CommandText = $@"
            SELECT d.id, d.lib_name, d.lib_path, d.cell_name, d.cell_path,
                   d.device_class, d.device_subclass, d.has_layout, d.has_symbol,
                   d.vt_tags, d.vdd_tags, d.tags,
                   GROUP_CONCAT(v.view)
            FROM devices d LEFT JOIN device_views v ON v.device_id=d.id
            {where}
            GROUP BY d.id
            ORDER BY d.lib_name, d.cell_name
            LIMIT $limit OFFSET $offset";
        if (classFilter.HasValue)
        {
            var pCls = cmd.CreateParameter(); pCls.ParameterName = "$cls"; pCls.Value = (int)classFilter.Value; cmd.Parameters.Add(pCls);
        }
        var pLim = cmd.CreateParameter(); pLim.ParameterName = "$limit"; pLim.Value = Math.Max(0, limit); cmd.Parameters.Add(pLim);
        var pOff = cmd.CreateParameter(); pOff.ParameterName = "$offset"; pOff.Value = Math.Max(0, offset); cmd.Parameters.Add(pOff);

        var list = new List<Device>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var viewsCsv = r.IsDBNull(12) ? string.Empty : r.GetString(12);
            list.Add(new Device
            {
                LibraryName = r.GetString(1),
                LibraryPath = r.GetString(2),
                CellName = r.GetString(3),
                CellPath = r.GetString(4),
                Class = (DeviceClass)r.GetInt32(5),
                Subclass = (DeviceSubclass)r.GetInt32(6),
                HasLayout = r.GetInt32(7) != 0,
                HasSymbol = r.GetInt32(8) != 0,
                VtTags = SplitCsv(r.IsDBNull(9) ? null : r.GetString(9)),
                VddTags = SplitCsv(r.IsDBNull(10) ? null : r.GetString(10)),
                Tags = SplitCsv(r.IsDBNull(11) ? null : r.GetString(11)),
                Views = SplitCsv(viewsCsv)
            });
        }
        return list;
    }

    // Return include candidates (deck paths) for a model, ordered by preference.
    // Preference: paths containing "/spectre/" or ending with ".scs" first, then others lexicographically.
    public static IReadOnlyList<string> GetPreferredIncludesForModel(string dbPath, string modelName)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"SELECT md.path
                             FROM models m JOIN model_decks md ON md.model_id=m.id
                             WHERE m.name=$name";
        var p = cmd.CreateParameter(); p.ParameterName = "$name"; p.Value = modelName; cmd.Parameters.Add(p);
        var paths = new List<string>();
        using (var r = cmd.ExecuteReader())
        {
            while (r.Read()) paths.Add(r.GetString(0));
        }
        static int Score(string p)
        {
            var lower = p.ToLowerInvariant();
            var score = 0;
            if (lower.Contains("/spectre/") || lower.EndsWith(".scs", StringComparison.Ordinal)) score += 2;
            if (lower.Contains("/models/")) score += 1;
            return -score; // sort ascending by negative score then by path
        }
        return paths
            .OrderBy(Score)
            .ThenBy(p => p, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    // Returns include+section candidates for a model+corner from model_contexts (table guaranteed to exist in schema).
    public static IReadOnlyList<(string IncludePath, string? Section)> GetContextsForModelAndCorner(string dbPath, string modelName, string? corner)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"SELECT i.path, s.name
                             FROM model_contexts mc
                             JOIN models m ON m.id=mc.model_id
                             LEFT JOIN corners c ON c.id=mc.corner_id
                             LEFT JOIN sections s ON s.id=mc.section_id
                             LEFT JOIN includes i ON i.id=mc.include_id
                             WHERE m.name=$name AND ($corner IS NULL AND c.id IS NULL OR c.name=$corner)
                             GROUP BY i.path, s.name
                             ORDER BY s.name";
        var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; pName.Value = modelName; cmd.Parameters.Add(pName);
        var pCorner = cmd.CreateParameter(); pCorner.ParameterName = "$corner"; pCorner.Value = (object?)corner ?? DBNull.Value; cmd.Parameters.Add(pCorner);
        var list = new List<(string, string?)>();
        using var r = cmd.ExecuteReader();
        while (r.Read()) list.Add((r.IsDBNull(0) ? string.Empty : r.GetString(0), r.IsDBNull(1) ? null : r.GetString(1)));
        return list;
    }

    public static int CountMatchedDevices(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = "SELECT COUNT(DISTINCT device_id) FROM device_model_matches";
        var obj = cmd.ExecuteScalar();
        return obj is long l ? (int)l : 0;
    }

    public static HashSet<string> LoadMatchedDeviceKeys(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"SELECT DISTINCT d.canonical_name
                             FROM device_model_matches m
                             JOIN devices d ON d.id = m.device_id";
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) set.Add(reader.GetString(0));
        return set;
    }

    public sealed record MatchCoverage(int Total, int Matched, int Ambiguous, int Unmatched,
        IReadOnlyList<string> SampleAmbiguous, IReadOnlyList<string> SampleUnmatched);

    public static MatchCoverage GetMatchCoverage(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        // Totals
        int total, matched, ambiguous, unmatched;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT COUNT(*) FROM devices";
            total = Convert.ToInt32((long)(cmd.ExecuteScalar() ?? 0));
        }
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(DISTINCT device_id) FROM device_model_matches";
            matched = Convert.ToInt32((long)(cmd.ExecuteScalar() ?? 0));
        }
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT COUNT(*) FROM (
                SELECT device_id, COUNT(*) c FROM device_model_matches GROUP BY device_id HAVING c > 1
            )";
            ambiguous = Convert.ToInt32((long)(cmd.ExecuteScalar() ?? 0));
        }
        unmatched = Math.Max(0, total - matched);

        // Samples
        var sampleAmb = new List<string>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT d.canonical_name
                                 FROM devices d
                                 JOIN (
                                   SELECT device_id, COUNT(*) c FROM device_model_matches GROUP BY device_id HAVING c > 1
                                 ) a ON a.device_id = d.id
                                 ORDER BY d.canonical_name
                                 LIMIT 8";
            using var r = cmd.ExecuteReader();
            while (r.Read()) sampleAmb.Add(r.GetString(0));
        }

        var sampleUn = new List<string>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"SELECT d.canonical_name
                                 FROM devices d
                                 LEFT JOIN device_model_matches m ON m.device_id = d.id
                                 WHERE m.device_id IS NULL
                                 ORDER BY d.canonical_name
                                 LIMIT 8";
            using var r = cmd.ExecuteReader();
            while (r.Read()) sampleUn.Add(r.GetString(0));
        }

        return new MatchCoverage(total, matched, ambiguous, unmatched, sampleAmb, sampleUn);
    }

    public sealed record MatchCoverageByClass(string Class, int Total, int Matched, int Ambiguous, int Unmatched);

    /// <summary>
    /// Retrieves per-device-class match coverage summaries from the PDK database.
    /// </summary>
    /// <param name="dbPath">Filesystem path to the read-only PDK SQLite database.</param>
    /// <returns>An ordered list of match coverage summaries, one entry per device class, containing total devices, matched count, ambiguous count, and unmatched count.</returns>
    public static IReadOnlyList<MatchCoverageByClass> GetMatchCoverageByClass(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        var list = new List<MatchCoverageByClass>();
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            WITH m AS (
              SELECT device_id, COUNT(*) AS c FROM device_model_matches GROUP BY device_id
            )
            SELECT d.device_class, COUNT(*) AS total,
                   SUM(CASE WHEN m.c IS NULL THEN 0 ELSE 1 END) AS matched,
                   SUM(CASE WHEN m.c > 1 THEN 1 ELSE 0 END)     AS ambiguous,
                   SUM(CASE WHEN m.c IS NULL THEN 1 ELSE 0 END)  AS unmatched
            FROM devices d
            LEFT JOIN m ON m.device_id = d.id
            GROUP BY d.device_class
            ORDER BY total DESC";
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            var cls = (DeviceClass)r.GetInt32(0);
            list.Add(new MatchCoverageByClass(cls.ToString(), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4)));
        }
        return list;
    }

    public sealed record DeviceMatchRow(string ModelName, string Quality, int Rank, string? Notes);

    public static IReadOnlyList<DeviceMatchRow> LoadMatchesForDevice(string dbPath, string deviceCanonicalName)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT m.name, dmm.quality, dmm.rank, dmm.notes
            FROM device_model_matches dmm
            JOIN devices d ON d.id = dmm.device_id
            JOIN models m ON m.id = dmm.model_id
            WHERE d.canonical_name=$key
            ORDER BY dmm.rank, m.name";
        var p = cmd.CreateParameter(); p.ParameterName = "$key"; p.Value = deviceCanonicalName; cmd.Parameters.Add(p);
        var list = new List<DeviceMatchRow>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            list.Add(new DeviceMatchRow(reader.GetString(0), reader.GetString(1), reader.GetInt32(2), reader.IsDBNull(3) ? null : reader.GetString(3)));
        }
        return list;
    }

    public sealed record GeometryRow(double? WMin, double? WMax, double? LMin, double? LMax, int? NfMin, int? NfMax, double? WDefault, double? LDefault, int? NfDefault, string? Source, string? Notes);

    public static GeometryRow? LoadGeometryForModel(string dbPath, string modelName)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT g.w_min, g.w_max, g.l_min, g.l_max, g.nf_min, g.nf_max, g.w_default, g.l_default, g.nf_default, g.source, g.notes
            FROM model_geometry g JOIN models m ON m.id=g.model_id WHERE m.name=$name";
        var p = cmd.CreateParameter(); p.ParameterName = "$name"; p.Value = modelName; cmd.Parameters.Add(p);
        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;
        return new GeometryRow(
            reader.IsDBNull(0) ? null : reader.GetDouble(0),
            reader.IsDBNull(1) ? null : reader.GetDouble(1),
            reader.IsDBNull(2) ? null : reader.GetDouble(2),
            reader.IsDBNull(3) ? null : reader.GetDouble(3),
            reader.IsDBNull(4) ? null : reader.GetInt32(4),
            reader.IsDBNull(5) ? null : reader.GetInt32(5),
            reader.IsDBNull(6) ? null : reader.GetDouble(6),
            reader.IsDBNull(7) ? null : reader.GetDouble(7),
            reader.IsDBNull(8) ? null : reader.GetInt32(8),
            reader.IsDBNull(9) ? null : reader.GetString(9),
            reader.IsDBNull(10) ? null : reader.GetString(10)
        );
    }

    public sealed record DeviceClassSummaryRow(int DeviceClass, int DeviceCount, int Matched, int Ambiguous, int Unmatched, string VoltageDomainsCsv, string ThresholdsCsv, string CornersCsv, string ExampleModel, int Decks);

    public static IReadOnlyList<DeviceClassSummaryRow> LoadDeviceClassSummary(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"SELECT device_class, device_count, matched_count, ambiguous_count, unmatched_count, voltage_domains, thresholds, corners, example_model, decks
                            FROM device_class_summary ORDER BY device_count DESC";
        var list = new List<DeviceClassSummaryRow>();
        using var r = cmd.ExecuteReader();
        while (r.Read())
        {
            list.Add(new DeviceClassSummaryRow(
                r.GetInt32(0), r.GetInt32(1), r.GetInt32(2), r.GetInt32(3), r.GetInt32(4),
                r.IsDBNull(5) ? string.Empty : r.GetString(5),
                r.IsDBNull(6) ? string.Empty : r.GetString(6),
                r.IsDBNull(7) ? string.Empty : r.GetString(7),
                r.IsDBNull(8) ? string.Empty : r.GetString(8),
                r.GetInt32(9)));
        }
        return list;
    }

    // Load the best-ranked model per device in a single query (reduces N+1 lookups)
    public static IReadOnlyDictionary<string, string> LoadBestMatchByDevice(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT d.canonical_name, m.name
            FROM device_model_matches dmm
            JOIN devices d ON d.id = dmm.device_id
            JOIN models m ON m.id = dmm.model_id
            WHERE dmm.rank = (
                SELECT MIN(rank) FROM device_model_matches dmm2 WHERE dmm2.device_id = dmm.device_id
            )
            ORDER BY d.canonical_name";
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            var key = reader.GetString(0);
            var model = reader.GetString(1);
            // If ties exist, keep the first occurrence
            if (!map.ContainsKey(key)) map[key] = model;
        }
        return map;
    }
}
