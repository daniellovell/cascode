using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Data.Sqlite;

namespace Cascode.Workspace;

/// <summary>
/// Reads characterization LUT data from the PDK database.
/// </summary>
public static class CharLutReader
{
    /// <summary>
    /// Loads a characterization run record by ID.
    /// </summary>
    public static CharRunRecord? LoadCharRun(string dbPath, long runId)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT r.id, m.name, r.corner, r.backend, r.timestamp, r.w_m, r.l_m, r.nf,
                   r.vds, r.vsb, r.temperature_c, r.status, r.job_dir, d.canonical_name
            FROM char_runs r
            JOIN models m ON m.id = r.model_id
            LEFT JOIN devices d ON d.id = r.device_id
            WHERE r.id = $id";
        AddParam(cmd, "$id", runId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new CharRunRecord
        {
            Id = reader.GetInt64(0),
            ModelName = reader.GetString(1),
            Corner = reader.GetString(2),
            Backend = reader.GetString(3),
            Timestamp = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            W_M = reader.GetDouble(5),
            L_M = reader.GetDouble(6),
            Nf = reader.GetInt32(7),
            Vds = reader.GetDouble(8),
            Vsb = reader.GetDouble(9),
            TemperatureC = reader.GetDouble(10),
            Status = reader.GetString(11),
            JobDir = reader.GetString(12),
            DeviceName = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }

    /// <summary>
    /// Loads LUT data points for a characterization run.
    /// </summary>
    public static IReadOnlyList<CharLutPoint> LoadLutPoints(string dbPath, long runId)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT vgs, id_a, gm, gds, gm_over_id, vth, vdsat, ro, gm_ro, ft, cgs, cgd
            FROM char_lut_points
            WHERE run_id = $rid
            ORDER BY vgs";
        AddParam(cmd, "$rid", runId);

        var points = new List<CharLutPoint>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            points.Add(new CharLutPoint
            {
                Vgs = reader.GetDouble(0),
                Id = GetNullableDouble(reader, 1),
                Gm = GetNullableDouble(reader, 2),
                Gds = GetNullableDouble(reader, 3),
                GmOverId = GetNullableDouble(reader, 4),
                Vth = GetNullableDouble(reader, 5),
                Vdsat = GetNullableDouble(reader, 6),
                Ro = GetNullableDouble(reader, 7),
                GmRo = GetNullableDouble(reader, 8),
                Ft = GetNullableDouble(reader, 9),
                Cgs = GetNullableDouble(reader, 10),
                Cgd = GetNullableDouble(reader, 11)
            });
        }

        return points;
    }

    /// <summary>
    /// Loads summary statistics for a characterization run.
    /// </summary>
    public static CharRunSummary? LoadRunSummary(string dbPath, long runId)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT run_id, gm_id_peak, vgs_at_peak_gm_id, vth_extracted, id_at_vth, gm_ro_max, ft_max, saturation_margin
            FROM char_run_summary
            WHERE run_id = $rid";
        AddParam(cmd, "$rid", runId);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new CharRunSummary
        {
            RunId = reader.GetInt64(0),
            GmIdPeak = GetNullableDouble(reader, 1),
            VgsAtPeakGmId = GetNullableDouble(reader, 2),
            VthExtracted = GetNullableDouble(reader, 3),
            IdAtVth = GetNullableDouble(reader, 4),
            GmRoMax = GetNullableDouble(reader, 5),
            FtMax = GetNullableDouble(reader, 6),
            SaturationMargin = GetNullableDouble(reader, 7)
        };
    }

    /// <summary>
    /// Gets characterization coverage report showing which models/corners have been characterized.
    /// </summary>
    public static CharacterizationCoverage GetCharacterizationCoverage(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);

        var models = new List<string>();
        var corners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var runSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalRuns = 0;

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT DISTINCT m.name
                FROM char_runs r
                JOIN models m ON m.id = r.model_id
                ORDER BY m.name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) models.Add(reader.GetString(0));
        }

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT corner FROM char_runs ORDER BY corner";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) corners.Add(reader.GetString(0));
        }

        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT m.name, r.corner
                FROM char_runs r
                JOIN models m ON m.id = r.model_id";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var modelName = reader.GetString(0);
                var corner = reader.GetString(1);
                runSet.Add($"{modelName}|{corner}");
                totalRuns++;
            }
        }

        return new CharacterizationCoverage(models, corners.OrderBy(c => c).ToList(), totalRuns, runSet);
    }

    /// <summary>
    /// Gets the most recent characterization run for a model at a given corner.
    /// </summary>
    public static CharRunRecord? GetLatestRunForModel(string dbPath, string modelName, string corner)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();
        cmd.CommandText = @"
            SELECT r.id, m.name, r.corner, r.backend, r.timestamp, r.w_m, r.l_m, r.nf,
                   r.vds, r.vsb, r.temperature_c, r.status, r.job_dir, d.canonical_name
            FROM char_runs r
            JOIN models m ON m.id = r.model_id
            LEFT JOIN devices d ON d.id = r.device_id
            WHERE m.name = $name AND r.corner = $corner
            ORDER BY r.timestamp DESC
            LIMIT 1";
        AddParam(cmd, "$name", modelName);
        AddParam(cmd, "$corner", corner);

        using var reader = cmd.ExecuteReader();
        if (!reader.Read()) return null;

        return new CharRunRecord
        {
            Id = reader.GetInt64(0),
            ModelName = reader.GetString(1),
            Corner = reader.GetString(2),
            Backend = reader.GetString(3),
            Timestamp = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            W_M = reader.GetDouble(5),
            L_M = reader.GetDouble(6),
            Nf = reader.GetInt32(7),
            Vds = reader.GetDouble(8),
            Vsb = reader.GetDouble(9),
            TemperatureC = reader.GetDouble(10),
            Status = reader.GetString(11),
            JobDir = reader.GetString(12),
            DeviceName = reader.IsDBNull(13) ? null : reader.GetString(13)
        };
    }

    /// <summary>
    /// Gets all characterization runs for a model, optionally filtered by corner.
    /// </summary>
    public static IReadOnlyList<CharRunRecord> GetRunsForModel(string dbPath, string modelName, string? corner = null)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();

        var whereClause = corner is null
            ? "WHERE m.name = $name"
            : "WHERE m.name = $name AND r.corner = $corner";

        cmd.CommandText = $@"
            SELECT r.id, m.name, r.corner, r.backend, r.timestamp, r.w_m, r.l_m, r.nf,
                   r.vds, r.vsb, r.temperature_c, r.status, r.job_dir, d.canonical_name
            FROM char_runs r
            JOIN models m ON m.id = r.model_id
            LEFT JOIN devices d ON d.id = r.device_id
            {whereClause}
            ORDER BY r.timestamp DESC";

        AddParam(cmd, "$name", modelName);
        if (corner is not null) AddParam(cmd, "$corner", corner);

        var runs = new List<CharRunRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            runs.Add(new CharRunRecord
            {
                Id = reader.GetInt64(0),
                ModelName = reader.GetString(1),
                Corner = reader.GetString(2),
                Backend = reader.GetString(3),
                Timestamp = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                W_M = reader.GetDouble(5),
                L_M = reader.GetDouble(6),
                Nf = reader.GetInt32(7),
                Vds = reader.GetDouble(8),
                Vsb = reader.GetDouble(9),
                TemperatureC = reader.GetDouble(10),
                Status = reader.GetString(11),
                JobDir = reader.GetString(12),
                DeviceName = reader.IsDBNull(13) ? null : reader.GetString(13)
            });
        }

        return runs;
    }

    /// <summary>
    /// Gets all characterization runs for a device, optionally filtered by corner.
    /// </summary>
    public static IReadOnlyList<CharRunRecord> GetRunsForDevice(string dbPath, string deviceName, string? corner = null)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);
        using var cmd = db.Connection.CreateCommand();

        var whereClause = corner is null
            ? "WHERE d.canonical_name = $name"
            : "WHERE d.canonical_name = $name AND r.corner = $corner";

        cmd.CommandText = $@"
            SELECT r.id, m.name, r.corner, r.backend, r.timestamp, r.w_m, r.l_m, r.nf,
                   r.vds, r.vsb, r.temperature_c, r.status, r.job_dir, d.canonical_name
            FROM char_runs r
            JOIN models m ON m.id = r.model_id
            JOIN devices d ON d.id = r.device_id
            {whereClause}
            ORDER BY r.timestamp DESC";

        AddParam(cmd, "$name", deviceName);
        if (corner is not null) AddParam(cmd, "$corner", corner);

        var runs = new List<CharRunRecord>();
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
        {
            runs.Add(new CharRunRecord
            {
                Id = reader.GetInt64(0),
                ModelName = reader.GetString(1),
                Corner = reader.GetString(2),
                Backend = reader.GetString(3),
                Timestamp = DateTime.Parse(reader.GetString(4), CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
                W_M = reader.GetDouble(5),
                L_M = reader.GetDouble(6),
                Nf = reader.GetInt32(7),
                Vds = reader.GetDouble(8),
                Vsb = reader.GetDouble(9),
                TemperatureC = reader.GetDouble(10),
                Status = reader.GetString(11),
                JobDir = reader.GetString(12),
                DeviceName = reader.IsDBNull(13) ? null : reader.GetString(13)
            });
        }

        return runs;
    }

    /// <summary>
    /// Gets per-device characterization coverage using completed runs.
    /// </summary>
    public static DeviceCharacterizationCoverage GetDeviceCoverage(string dbPath)
    {
        using var db = PdkDatabase.OpenReadOnly(dbPath);

        var devices = new List<string>();
        var deviceClasses = new Dictionary<string, DeviceClass>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT canonical_name, device_class FROM devices ORDER BY canonical_name";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var name = reader.GetString(0);
                devices.Add(name);
                deviceClasses[name] = (DeviceClass)reader.GetInt32(1);
            }
        }

        var corners = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = "SELECT DISTINCT corner FROM char_runs ORDER BY corner";
            using var reader = cmd.ExecuteReader();
            while (reader.Read()) corners.Add(reader.GetString(0));
        }

        var runSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var totalRuns = 0;
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.CommandText = @"
                SELECT d.canonical_name, r.corner
                FROM char_runs r
                JOIN devices d ON d.id = r.device_id
                WHERE r.status = 'complete'";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var device = reader.GetString(0);
                var cornerVal = reader.GetString(1);
                runSet.Add($"{device}|{cornerVal}");
            }
        }

        totalRuns = runSet.Count;

        return new DeviceCharacterizationCoverage(
            devices,
            corners.OrderBy(c => c, StringComparer.OrdinalIgnoreCase).ToList(),
            totalRuns,
            runSet,
            deviceClasses);
    }

    private static void AddParam(SqliteCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
    }

    private static double? GetNullableDouble(SqliteDataReader reader, int ordinal)
        => reader.IsDBNull(ordinal) ? null : reader.GetDouble(ordinal);
}
