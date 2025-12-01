using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace Cascode.Workspace;

/// <summary>
/// Writes characterization LUT data to the PDK database.
/// </summary>
public static class CharLutWriter
{
    /// <summary>
    /// Writes a characterization run record and returns the generated run ID.
    /// </summary>
    public static long WriteCharRun(string dbPath, CharRunRecord run)
    {
        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();

        var modelId = GetModelId(db.Connection, tx, run.ModelName);
        if (modelId is null)
            throw new InvalidOperationException($"Model '{run.ModelName}' not found in database.");

        long? deviceId = null;
        if (!string.IsNullOrWhiteSpace(run.DeviceName))
        {
            deviceId = GetDeviceId(db.Connection, tx, run.DeviceName!);
        }

        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO char_runs(model_id, device_id, corner, backend, timestamp, w_m, l_m, nf, vds, vsb, temperature_c, status, job_dir)
            VALUES ($mid, $did, $corner, $backend, $ts, $w, $l, $nf, $vds, $vsb, $temp, $status, $jobdir)
            RETURNING id;";

        AddParam(cmd, "$mid", modelId.Value);
        AddParam(cmd, "$did", (object?)deviceId ?? DBNull.Value);
        AddParam(cmd, "$corner", run.Corner);
        AddParam(cmd, "$backend", run.Backend);
        AddParam(cmd, "$ts", run.Timestamp.ToString("o", CultureInfo.InvariantCulture));
        AddParam(cmd, "$w", run.W_M);
        AddParam(cmd, "$l", run.L_M);
        AddParam(cmd, "$nf", run.Nf);
        AddParam(cmd, "$vds", run.Vds);
        AddParam(cmd, "$vsb", run.Vsb);
        AddParam(cmd, "$temp", run.TemperatureC);
        AddParam(cmd, "$status", run.Status);
        AddParam(cmd, "$jobdir", run.JobDir);

        var runId = (long)cmd.ExecuteScalar()!;
        tx.Commit();
        return runId;
    }

    /// <summary>
    /// Writes LUT data points for a characterization run.
    /// </summary>
    public static void WriteLutPoints(string dbPath, long runId, IReadOnlyList<CharLutPoint> points)
    {
        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();

        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO char_lut_points(run_id, vgs, id_a, gm, gds, gm_over_id, vth, vdsat, ro, gm_ro, ft, cgs, cgd)
            VALUES ($rid, $vgs, $id, $gm, $gds, $gmid, $vth, $vdsat, $ro, $gmro, $ft, $cgs, $cgd);";

        var pRid = AddParam(cmd, "$rid", runId);
        var pVgs = AddParam(cmd, "$vgs", 0.0);
        var pId = AddParamNullable(cmd, "$id");
        var pGm = AddParamNullable(cmd, "$gm");
        var pGds = AddParamNullable(cmd, "$gds");
        var pGmId = AddParamNullable(cmd, "$gmid");
        var pVth = AddParamNullable(cmd, "$vth");
        var pVdsat = AddParamNullable(cmd, "$vdsat");
        var pRo = AddParamNullable(cmd, "$ro");
        var pGmRo = AddParamNullable(cmd, "$gmro");
        var pFt = AddParamNullable(cmd, "$ft");
        var pCgs = AddParamNullable(cmd, "$cgs");
        var pCgd = AddParamNullable(cmd, "$cgd");

        foreach (var pt in points)
        {
            pVgs.Value = pt.Vgs;
            SetNullable(pId, pt.Id);
            SetNullable(pGm, pt.Gm);
            SetNullable(pGds, pt.Gds);
            SetNullable(pGmId, pt.GmOverId);
            SetNullable(pVth, pt.Vth);
            SetNullable(pVdsat, pt.Vdsat);
            SetNullable(pRo, pt.Ro);
            SetNullable(pGmRo, pt.GmRo);
            SetNullable(pFt, pt.Ft);
            SetNullable(pCgs, pt.Cgs);
            SetNullable(pCgd, pt.Cgd);
            cmd.ExecuteNonQuery();
        }

        tx.Commit();
    }

    /// <summary>
    /// Computes and writes summary statistics for a characterization run.
    /// </summary>
    public static void WriteRunSummary(string dbPath, long runId)
    {
        var points = CharLutReader.LoadLutPoints(dbPath, runId);
        if (points.Count == 0) return;

        double? gmIdPeak = null, vgsAtPeak = null, vthExtracted = null, idAtVth = null;
        double? gmRoMax = null, ftMax = null;

        foreach (var pt in points)
        {
            if (pt.GmOverId.HasValue && (!gmIdPeak.HasValue || pt.GmOverId.Value > gmIdPeak.Value))
            {
                gmIdPeak = pt.GmOverId.Value;
                vgsAtPeak = pt.Vgs;
            }
            if (pt.GmRo.HasValue && (!gmRoMax.HasValue || pt.GmRo.Value > gmRoMax.Value))
                gmRoMax = pt.GmRo.Value;
            if (pt.Ft.HasValue && (!ftMax.HasValue || pt.Ft.Value > ftMax.Value))
                ftMax = pt.Ft.Value;
            if (pt.Vth.HasValue && !vthExtracted.HasValue)
            {
                vthExtracted = pt.Vth.Value;
                idAtVth = pt.Id;
            }
        }

        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();

        using var cmd = db.Connection.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO char_run_summary(run_id, gm_id_peak, vgs_at_peak_gm_id, vth_extracted, id_at_vth, gm_ro_max, ft_max, saturation_margin)
            VALUES ($rid, $gmpeak, $vgspeak, $vth, $idvth, $gmromax, $ftmax, $satmarg)
            ON CONFLICT(run_id) DO UPDATE SET
                gm_id_peak=excluded.gm_id_peak,
                vgs_at_peak_gm_id=excluded.vgs_at_peak_gm_id,
                vth_extracted=excluded.vth_extracted,
                id_at_vth=excluded.id_at_vth,
                gm_ro_max=excluded.gm_ro_max,
                ft_max=excluded.ft_max,
                saturation_margin=excluded.saturation_margin;";

        AddParam(cmd, "$rid", runId);
        AddParamNullableValue(cmd, "$gmpeak", gmIdPeak);
        AddParamNullableValue(cmd, "$vgspeak", vgsAtPeak);
        AddParamNullableValue(cmd, "$vth", vthExtracted);
        AddParamNullableValue(cmd, "$idvth", idAtVth);
        AddParamNullableValue(cmd, "$gmromax", gmRoMax);
        AddParamNullableValue(cmd, "$ftmax", ftMax);
        AddParamNullableValue(cmd, "$satmarg", null);

        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Imports characterization data from a job directory containing spec.json and derived.csv.
    /// </summary>
    public static long ImportFromJobDir(string dbPath, string jobDir)
    {
        var specPath = Path.Combine(jobDir, "spec.json");
        var derivedPath = Path.Combine(jobDir, "derived.csv");

        if (!File.Exists(specPath))
            throw new FileNotFoundException("spec.json not found", specPath);
        if (!File.Exists(derivedPath))
            throw new FileNotFoundException("derived.csv not found", derivedPath);

        var specJson = File.ReadAllText(specPath);
        var spec = JsonSerializer.Deserialize<SpecJson>(specJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("Failed to parse spec.json");

        var run = new CharRunRecord
        {
            ModelName = spec.ModelName ?? string.Empty,
            Corner = spec.Corner ?? "tt",
            Backend = string.IsNullOrWhiteSpace(spec.Backend) ? "spectre" : spec.Backend!.ToLowerInvariant(),
            Timestamp = DateTime.UtcNow,
            W_M = spec.W_M,
            L_M = spec.L_M,
            Nf = spec.Nf > 0 ? spec.Nf : 1,
            Vds = spec.VdsFixed,
            Vsb = spec.VsbFixed,
            TemperatureC = spec.TemperatureC > 0 ? spec.TemperatureC : 27.0,
            Status = "complete",
            JobDir = jobDir,
            DeviceName = string.IsNullOrWhiteSpace(spec.DeviceName) ? null : spec.DeviceName
        };

        var runId = WriteCharRun(dbPath, run);
        var points = ParseDerivedCsv(derivedPath);
        WriteLutPoints(dbPath, runId, points);
        WriteRunSummary(dbPath, runId);

        return runId;
    }

    private static IReadOnlyList<CharLutPoint> ParseDerivedCsv(string path)
    {
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return Array.Empty<CharLutPoint>();

        var header = lines[0].Split(',', StringSplitOptions.TrimEntries);
        var colMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < header.Length; i++) colMap[header[i]] = i;

        int Col(params string[] names)
        {
            foreach (var n in names)
                if (colMap.TryGetValue(n, out var idx)) return idx;
            return -1;
        }

        var iVgs = Col("vgs", "vsg");
        var iId = Col("id", "ids", "id_a");
        var iGm = Col("gm");
        var iGds = Col("gds");
        var iGmId = Col("gm_over_id", "gmoverid");
        var iVth = Col("vth");
        var iVdsat = Col("vdsat");
        var iRo = Col("ro");
        var iGmRo = Col("gm_ro", "gmro");
        var iFt = Col("ft");
        var iCgs = Col("cgs");
        var iCgd = Col("cgd");

        if (iVgs < 0) return Array.Empty<CharLutPoint>();

        var points = new List<CharLutPoint>();
        for (var i = 1; i < lines.Length; i++)
        {
            var parts = lines[i].Split(',', StringSplitOptions.None);
            if (parts.Length <= iVgs) continue;

            var pt = new CharLutPoint
            {
                Vgs = ParseDouble(parts, iVgs) ?? 0.0,
                Id = ParseDouble(parts, iId),
                Gm = ParseDouble(parts, iGm),
                Gds = ParseDouble(parts, iGds),
                GmOverId = ParseDouble(parts, iGmId),
                Vth = ParseDouble(parts, iVth),
                Vdsat = ParseDouble(parts, iVdsat),
                Ro = ParseDouble(parts, iRo),
                GmRo = ParseDouble(parts, iGmRo),
                Ft = ParseDouble(parts, iFt),
                Cgs = ParseDouble(parts, iCgs),
                Cgd = ParseDouble(parts, iCgd)
            };
            points.Add(pt);
        }

        return points;
    }

    private static double? ParseDouble(string[] parts, int idx)
    {
        if (idx < 0 || idx >= parts.Length) return null;
        var text = parts[idx].Trim();
        if (string.IsNullOrWhiteSpace(text)) return null;
        if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var val))
            return val;
        return null;
    }

    private static long? GetModelId(SqliteConnection conn, SqliteTransaction tx, string modelName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM models WHERE name=$name";
        AddParam(cmd, "$name", modelName);
        var result = cmd.ExecuteScalar();
        return result is long id ? id : null;
    }

    private static long? GetDeviceId(SqliteConnection conn, SqliteTransaction tx, string deviceName)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "SELECT id FROM devices WHERE canonical_name=$name";
        AddParam(cmd, "$name", deviceName);
        var result = cmd.ExecuteScalar();
        return result is long id ? id : null;
    }

    private static SqliteParameter AddParam(SqliteCommand cmd, string name, object value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value;
        cmd.Parameters.Add(p);
        return p;
    }

    private static SqliteParameter AddParamNullable(SqliteCommand cmd, string name)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = DBNull.Value;
        cmd.Parameters.Add(p);
        return p;
    }

    private static void AddParamNullableValue(SqliteCommand cmd, string name, double? value)
    {
        var p = cmd.CreateParameter();
        p.ParameterName = name;
        p.Value = value.HasValue ? value.Value : DBNull.Value;
        cmd.Parameters.Add(p);
    }

    private static void SetNullable(SqliteParameter p, double? value)
        => p.Value = value.HasValue ? value.Value : DBNull.Value;

    private sealed class SpecJson
    {
        [System.Text.Json.Serialization.JsonPropertyName("model_name")]
        public string? ModelName { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("corner")]
        public string? Corner { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("backend")]
        public string? Backend { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("w_m")]
        public double W_M { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("l_m")]
        public double L_M { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("nf")]
        public int Nf { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("vds_fixed")]
        public double VdsFixed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("vsb_fixed")]
        public double VsbFixed { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("temperature_c")]
        public double TemperatureC { get; set; }
        [System.Text.Json.Serialization.JsonPropertyName("device_name")]
        public string? DeviceName { get; set; }
    }
}
