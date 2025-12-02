using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Data.Sqlite;

namespace Cascode.Workspace;

/// <summary>
/// Writes Workspace scan results into the PDK SQLite database.
/// Device tables and characterization will be added incrementally.
/// </summary>
public static class PdkDatabaseWriter
{
    public static void Write(string dbPath, WorkspaceScanResult scan, IReadOnlyList<Device>? devices = null, CancellationToken cancellationToken = default)
    {
        if (scan is null) throw new ArgumentNullException(nameof(scan));
        cancellationToken.ThrowIfCancellationRequested();

        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();

        UpsertLibraries(db.Connection, tx, scan.Libraries, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        UpsertModels(db.Connection, tx, scan.Models, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        if (devices is not null)
        {
            UpsertDevices(db.Connection, tx, devices, cancellationToken);
        }

        cancellationToken.ThrowIfCancellationRequested();
        tx.Commit();
    }

    public static void UpsertMatches(string dbPath, IReadOnlyList<DeviceModelMatchRecord> matches, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();
        var deviceId = LoadIdMap(db.Connection, tx, "devices", "canonical_name");
        var modelId = LoadIdMap(db.Connection, tx, "models", "name");

        using var clear = db.Connection.CreateCommand();
        clear.Transaction = tx;
        clear.CommandText = "DELETE FROM device_model_matches";
        clear.ExecuteNonQuery();

        using var insert = db.Connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"
            INSERT INTO device_model_matches(device_id, model_id, quality, rank, notes)
            VALUES ($d, $m, $q, $r, $n)
            ON CONFLICT(device_id, model_id) DO UPDATE SET quality=excluded.quality, rank=excluded.rank, notes=excluded.notes;";
        var pd = insert.CreateParameter(); pd.ParameterName = "$d"; insert.Parameters.Add(pd);
        var pm = insert.CreateParameter(); pm.ParameterName = "$m"; insert.Parameters.Add(pm);
        var pq = insert.CreateParameter(); pq.ParameterName = "$q"; insert.Parameters.Add(pq);
        var pr = insert.CreateParameter(); pr.ParameterName = "$r"; insert.Parameters.Add(pr);
        var pn = insert.CreateParameter(); pn.ParameterName = "$n"; insert.Parameters.Add(pn);

        var count = 0;
        foreach (var rec in matches)
        {
            if (++count % 100 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            if (!deviceId.TryGetValue(rec.DeviceCanonicalName, out var did)) continue;
            if (!modelId.TryGetValue(rec.ModelName, out var mid)) continue;
            pd.Value = did; pm.Value = mid; pq.Value = rec.Quality; pr.Value = rec.Rank; pn.Value = (object?)rec.Notes ?? DBNull.Value;
            insert.ExecuteNonQuery();
        }

        cancellationToken.ThrowIfCancellationRequested();
        tx.Commit();

        // Rebuild summaries after matches change
        RebuildDeviceClassSummary(dbPath, cancellationToken);
    }

    public static void UpsertGeometry(string dbPath, IReadOnlyList<ModelGeometry> geometry)
    {
        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();
        var modelId = LoadIdMap(db.Connection, tx, "models", "name");

        using var insert = db.Connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"
            INSERT INTO model_geometry(model_id, w_min, w_max, l_min, l_max, nf_min, nf_max, area_min, area_max, perim_min, perim_max, w_default, l_default, nf_default, source, notes)
            VALUES ($id, $wmin, $wmax, $lmin, $lmax, $nfmin, $nfmax, $areamin, $areamax, $pmin, $pmax, $wdef, $ldef, $nfdef, $src, $notes)
            ON CONFLICT(model_id) DO UPDATE SET w_min=excluded.w_min, w_max=excluded.w_max, l_min=excluded.l_min, l_max=excluded.l_max,
                nf_min=excluded.nf_min, nf_max=excluded.nf_max, area_min=excluded.area_min, area_max=excluded.area_max, perim_min=excluded.perim_min, perim_max=excluded.perim_max,
                w_default=excluded.w_default, l_default=excluded.l_default, nf_default=excluded.nf_default, source=excluded.source, notes=excluded.notes;";
        var pid = insert.CreateParameter(); pid.ParameterName = "$id"; insert.Parameters.Add(pid);
        var pwmin = insert.CreateParameter(); pwmin.ParameterName = "$wmin"; insert.Parameters.Add(pwmin);
        var pwmax = insert.CreateParameter(); pwmax.ParameterName = "$wmax"; insert.Parameters.Add(pwmax);
        var plmin = insert.CreateParameter(); plmin.ParameterName = "$lmin"; insert.Parameters.Add(plmin);
        var plmax = insert.CreateParameter(); plmax.ParameterName = "$lmax"; insert.Parameters.Add(plmax);
        var pnfmin = insert.CreateParameter(); pnfmin.ParameterName = "$nfmin"; insert.Parameters.Add(pnfmin);
        var pnfmax = insert.CreateParameter(); pnfmax.ParameterName = "$nfmax"; insert.Parameters.Add(pnfmax);
        var pamn = insert.CreateParameter(); pamn.ParameterName = "$areamin"; insert.Parameters.Add(pamn);
        var pamx = insert.CreateParameter(); pamx.ParameterName = "$areamax"; insert.Parameters.Add(pamx);
        var ppmin = insert.CreateParameter(); ppmin.ParameterName = "$pmin"; insert.Parameters.Add(ppmin);
        var ppmax = insert.CreateParameter(); ppmax.ParameterName = "$pmax"; insert.Parameters.Add(ppmax);
        var pwdef = insert.CreateParameter(); pwdef.ParameterName = "$wdef"; insert.Parameters.Add(pwdef);
        var pldef = insert.CreateParameter(); pldef.ParameterName = "$ldef"; insert.Parameters.Add(pldef);
        var pnfdef = insert.CreateParameter(); pnfdef.ParameterName = "$nfdef"; insert.Parameters.Add(pnfdef);
        var psrc = insert.CreateParameter(); psrc.ParameterName = "$src"; insert.Parameters.Add(psrc);
        var pnotes = insert.CreateParameter(); pnotes.ParameterName = "$notes"; insert.Parameters.Add(pnotes);

        foreach (var g in geometry)
        {
            if (!modelId.TryGetValue(g.ModelName, out var mid)) continue;
            pid.Value = mid;
            pwmin.Value = (object?)g.WMin ?? DBNull.Value;
            pwmax.Value = (object?)g.WMax ?? DBNull.Value;
            plmin.Value = (object?)g.LMin ?? DBNull.Value;
            plmax.Value = (object?)g.LMax ?? DBNull.Value;
            pnfmin.Value = (object?)g.NfMin ?? DBNull.Value;
            pnfmax.Value = (object?)g.NfMax ?? DBNull.Value;
            pamn.Value = DBNull.Value;
            pamx.Value = DBNull.Value;
            ppmin.Value = DBNull.Value;
            ppmax.Value = DBNull.Value;
            pwdef.Value = (object?)g.WDefault ?? DBNull.Value;
            pldef.Value = (object?)g.LDefault ?? DBNull.Value;
            pnfdef.Value = (object?)g.NfDefault ?? DBNull.Value;
            psrc.Value = (object?)g.Source ?? DBNull.Value;
            pnotes.Value = (object?)g.Notes ?? DBNull.Value;
            insert.ExecuteNonQuery();
        }

        tx.Commit();
    }

    public static void UpsertDeviceGeometry(string dbPath, IReadOnlyList<Device> devices, IReadOnlyList<DeviceModelMatchRecord> matches, IReadOnlyList<ModelGeometry> modelGeometry, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();

        var deviceId = LoadIdMap(db.Connection, tx, "devices", "canonical_name");
        var geomByModel = modelGeometry.ToDictionary(g => g.ModelName, StringComparer.OrdinalIgnoreCase);
        var bestByDevice = new Dictionary<string, (int Rank, string ModelName)>(StringComparer.OrdinalIgnoreCase);
        foreach (var match in matches.OrderBy(m => m.Rank).ThenBy(m => m.ModelName, StringComparer.OrdinalIgnoreCase))
        {
            if (!bestByDevice.TryGetValue(match.DeviceCanonicalName, out var current) || match.Rank < current.Rank)
            {
                bestByDevice[match.DeviceCanonicalName] = (match.Rank, match.ModelName);
            }
        }

        using var insert = db.Connection.CreateCommand();
        insert.Transaction = tx;
        insert.CommandText = @"
            INSERT INTO device_geometry(device_id, w_min, w_max, l_min, l_max, nf_min, nf_max, w_default, l_default, nf_default, source, notes)
            VALUES ($id, $wmin, $wmax, $lmin, $lmax, $nfmin, $nfmax, $wdef, $ldef, $nfdef, $src, $notes)
            ON CONFLICT(device_id) DO UPDATE SET
                w_min=excluded.w_min,
                w_max=excluded.w_max,
                l_min=excluded.l_min,
                l_max=excluded.l_max,
                nf_min=excluded.nf_min,
                nf_max=excluded.nf_max,
                w_default=excluded.w_default,
                l_default=excluded.l_default,
                nf_default=excluded.nf_default,
                source=excluded.source,
                notes=excluded.notes;";
        var pid = insert.CreateParameter(); pid.ParameterName = "$id"; insert.Parameters.Add(pid);
        var pwmin = insert.CreateParameter(); pwmin.ParameterName = "$wmin"; insert.Parameters.Add(pwmin);
        var pwmax = insert.CreateParameter(); pwmax.ParameterName = "$wmax"; insert.Parameters.Add(pwmax);
        var plmin = insert.CreateParameter(); plmin.ParameterName = "$lmin"; insert.Parameters.Add(plmin);
        var plmax = insert.CreateParameter(); plmax.ParameterName = "$lmax"; insert.Parameters.Add(plmax);
        var pnfmin = insert.CreateParameter(); pnfmin.ParameterName = "$nfmin"; insert.Parameters.Add(pnfmin);
        var pnfmax = insert.CreateParameter(); pnfmax.ParameterName = "$nfmax"; insert.Parameters.Add(pnfmax);
        var pwdef = insert.CreateParameter(); pwdef.ParameterName = "$wdef"; insert.Parameters.Add(pwdef);
        var pldef = insert.CreateParameter(); pldef.ParameterName = "$ldef"; insert.Parameters.Add(pldef);
        var pnfdef = insert.CreateParameter(); pnfdef.ParameterName = "$nfdef"; insert.Parameters.Add(pnfdef);
        var psrc = insert.CreateParameter(); psrc.ParameterName = "$src"; insert.Parameters.Add(psrc);
        var pnotes = insert.CreateParameter(); pnotes.ParameterName = "$notes"; insert.Parameters.Add(pnotes);

        foreach (var device in devices)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!deviceId.TryGetValue(device.CanonicalName, out var did)) continue;
            if (!bestByDevice.TryGetValue(device.CanonicalName, out var best)) continue;
            if (!geomByModel.TryGetValue(best.ModelName, out var geom)) continue;

            pid.Value = did;
            pwmin.Value = (object?)geom.WMin ?? DBNull.Value;
            pwmax.Value = (object?)geom.WMax ?? DBNull.Value;
            plmin.Value = (object?)geom.LMin ?? DBNull.Value;
            plmax.Value = (object?)geom.LMax ?? DBNull.Value;
            pnfmin.Value = (object?)geom.NfMin ?? DBNull.Value;
            pnfmax.Value = (object?)geom.NfMax ?? DBNull.Value;
            pwdef.Value = (object?)geom.WDefault ?? DBNull.Value;
            pldef.Value = (object?)geom.LDefault ?? DBNull.Value;
            pnfdef.Value = (object?)geom.NfDefault ?? DBNull.Value;
            var source = string.IsNullOrWhiteSpace(geom.Source) ? best.ModelName : $"{geom.Source}:{best.ModelName}";
            psrc.Value = source;
            pnotes.Value = (object?)geom.Notes ?? DBNull.Value;
            insert.ExecuteNonQuery();
        }

        cancellationToken.ThrowIfCancellationRequested();
        tx.Commit();
    }

    /// <summary>
    /// Recomputes and persists per-device-class rollup metrics into the database's <c>device_class_summary</c> table.
    /// </summary>
    /// <param name="dbPath">Filesystem path to the SQLite database file to update; existing summary rows are replaced or updated.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    public static void RebuildDeviceClassSummary(string dbPath, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        using var db = PdkDatabase.Open(dbPath);
        using var tx = db.Connection.BeginTransaction();

        // Clear existing
        using (var clear = db.Connection.CreateCommand())
        {
            clear.Transaction = tx;
            clear.CommandText = "DELETE FROM device_class_summary";
            clear.ExecuteNonQuery();
        }

        // Load devices (for count + vt/vdd sets per class)
        var classDeviceCount = new Dictionary<int, int>();
        var classVt = new Dictionary<int, HashSet<string>>();
        var classVdd = new Dictionary<int, HashSet<string>>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "SELECT device_class, vt_tags, vdd_tags FROM devices";
            using var r = cmd.ExecuteReader();
            while (r.Read())
            {
                var cls = r.GetInt32(0);
                classDeviceCount[cls] = classDeviceCount.TryGetValue(cls, out var c) ? c + 1 : 1;
                if (!classVt.TryGetValue(cls, out var vtSet)) { vtSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase); classVt[cls] = vtSet; }
                if (!classVdd.TryGetValue(cls, out var vddSet)) { vddSet = new HashSet<string>(StringComparer.OrdinalIgnoreCase); classVdd[cls] = vddSet; }
                if (!r.IsDBNull(1)) foreach (var tok in r.GetString(1).Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)) vtSet.Add(tok);
                if (!r.IsDBNull(2))
                {
                    var vv = r.GetDouble(2);
                    vddSet.Add(VddFormatting.PrettyFromVolts(vv));
                }
            }
        }

        // Matched and ambiguous counts per class
        var classMatched = new Dictionary<int, int>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                SELECT d.device_class, COUNT(DISTINCT d.id)
                FROM devices d JOIN device_model_matches m ON m.device_id=d.id
                GROUP BY d.device_class";
            using var r = cmd.ExecuteReader();
            while (r.Read()) classMatched[r.GetInt32(0)] = r.GetInt32(1);
        }
        var classAmbiguous = new Dictionary<int, int>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                SELECT d.device_class, COUNT(*)
                FROM (
                  SELECT device_id, COUNT(*) c FROM device_model_matches GROUP BY device_id HAVING c>1
                ) a JOIN devices d ON d.id=a.device_id
                GROUP BY d.device_class";
            using var r = cmd.ExecuteReader();
            while (r.Read()) classAmbiguous[r.GetInt32(0)] = r.GetInt32(1);
        }

        // Best-match model per device → aggregate corners and deck counts by class
        var classCorners = new Dictionary<int, HashSet<string>>();
        var classDecks = new Dictionary<int, int>();
        var classExampleModel = new Dictionary<int, string>();
        using (var cmd = db.Connection.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                WITH best AS (
                  SELECT d.id AS device_id, d.device_class, m.id AS model_id, m.name AS model_name
                  FROM devices d
                  JOIN device_model_matches mm ON mm.device_id=d.id
                  JOIN models m ON m.id=mm.model_id
                  WHERE mm.rank = (
                    SELECT MIN(rank) FROM device_model_matches mm2 WHERE mm2.device_id=d.id
                  )
                )
                SELECT b.device_class, b.model_id, b.model_name FROM best b
            ";
            using var r = cmd.ExecuteReader();
            var modelsByClass = new Dictionary<int, HashSet<long>>();
            var exampleByClassTemp = new Dictionary<int, List<string>>();
            while (r.Read())
            {
                var cls = r.GetInt32(0);
                var mid = r.GetInt64(1);
                var mname = r.GetString(2);
                if (!modelsByClass.TryGetValue(cls, out var set)) { set = new HashSet<long>(); modelsByClass[cls] = set; }
                set.Add(mid);
                if (!exampleByClassTemp.TryGetValue(cls, out var names)) { names = new List<string>(); exampleByClassTemp[cls] = names; }
                names.Add(mname);
            }

            // Corners from model_contexts (table guaranteed to exist in schema)
            using var c2 = db.Connection.CreateCommand();
            c2.Transaction = tx;
            c2.CommandText = @"SELECT mc.model_id, c.name FROM model_contexts mc JOIN corners c ON c.id=mc.corner_id WHERE c.name IS NOT NULL AND c.name<>''";
            using var r2 = c2.ExecuteReader();
            var cornersByModel = new Dictionary<long, HashSet<string>>();
            while (r2.Read())
            {
                var mid = r2.GetInt64(0);
                var corner = r2.IsDBNull(1) ? string.Empty : r2.GetString(1);
                if (string.IsNullOrWhiteSpace(corner)) continue;
                if (!cornersByModel.TryGetValue(mid, out var set)) { set = new HashSet<string>(StringComparer.OrdinalIgnoreCase); cornersByModel[mid] = set; }
                set.Add(corner);
            }
            foreach (var (cls, mids) in modelsByClass)
            {
                var agg = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var mid in mids)
                {
                    if (cornersByModel.TryGetValue(mid, out var set)) foreach (var c in set) agg.Add(c);
                }
                classCorners[cls] = agg;
            }

            // Deck counts
            using (var d2 = db.Connection.CreateCommand())
            {
                d2.Transaction = tx;
                d2.CommandText = "SELECT model_id, COUNT(*) FROM model_decks GROUP BY model_id";
                using var r3 = d2.ExecuteReader();
                var decksByModel = new Dictionary<long, int>();
                while (r3.Read()) decksByModel[r3.GetInt64(0)] = r3.GetInt32(1);
                foreach (var (cls, mids) in modelsByClass)
                {
                    var sum = 0;
                    foreach (var mid in mids) if (decksByModel.TryGetValue(mid, out var c)) sum += c;
                    classDecks[cls] = sum;
                }
            }

            foreach (var (cls, names) in exampleByClassTemp)
            {
                classExampleModel[cls] = names.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).FirstOrDefault() ?? "-";
            }
        }

        // Write rows
        using (var ins = db.Connection.CreateCommand())
        {
            ins.Transaction = tx;
            ins.CommandText = @"
                INSERT INTO device_class_summary(device_class, device_count, matched_count, ambiguous_count, unmatched_count, voltage_domains, thresholds, corners, example_model, decks)
                VALUES ($cls, $dc, $mc, $ac, $uc, $vdd, $vt, $corn, $ex, $decks)
                ON CONFLICT(device_class) DO UPDATE SET
                    device_count=excluded.device_count,
                    matched_count=excluded.matched_count,
                    ambiguous_count=excluded.ambiguous_count,
                    unmatched_count=excluded.unmatched_count,
                    voltage_domains=excluded.voltage_domains,
                    thresholds=excluded.thresholds,
                    corners=excluded.corners,
                    example_model=excluded.example_model,
                    decks=excluded.decks;";
            var pCls = ins.CreateParameter(); pCls.ParameterName = "$cls"; ins.Parameters.Add(pCls);
            var pDc = ins.CreateParameter(); pDc.ParameterName = "$dc"; ins.Parameters.Add(pDc);
            var pMc = ins.CreateParameter(); pMc.ParameterName = "$mc"; ins.Parameters.Add(pMc);
            var pAc = ins.CreateParameter(); pAc.ParameterName = "$ac"; ins.Parameters.Add(pAc);
            var pUc = ins.CreateParameter(); pUc.ParameterName = "$uc"; ins.Parameters.Add(pUc);
            var pVdd = ins.CreateParameter(); pVdd.ParameterName = "$vdd"; ins.Parameters.Add(pVdd);
            var pVt = ins.CreateParameter(); pVt.ParameterName = "$vt"; ins.Parameters.Add(pVt);
            var pCorn = ins.CreateParameter(); pCorn.ParameterName = "$corn"; ins.Parameters.Add(pCorn);
            var pEx = ins.CreateParameter(); pEx.ParameterName = "$ex"; ins.Parameters.Add(pEx);
            var pDecks = ins.CreateParameter(); pDecks.ParameterName = "$decks"; ins.Parameters.Add(pDecks);

            foreach (var (cls, count) in classDeviceCount)
            {
                var matched = classMatched.TryGetValue(cls, out var m) ? m : 0;
                var ambiguous = classAmbiguous.TryGetValue(cls, out var a) ? a : 0;
                var unmatched = Math.Max(0, count - matched);
                string join(HashSet<string>? set, int take = 5) => set is null || set.Count == 0
                    ? string.Empty
                    : string.Join(',', set.OrderBy(s => s, StringComparer.OrdinalIgnoreCase).Take(take));

                pCls.Value = cls;
                pDc.Value = count;
                pMc.Value = matched;
                pAc.Value = ambiguous;
                pUc.Value = unmatched;
                pVdd.Value = join(classVdd.TryGetValue(cls, out var vdd) ? vdd : null);
                pVt.Value = join(classVt.TryGetValue(cls, out var vt) ? vt : null);
                pCorn.Value = join(classCorners.TryGetValue(cls, out var cor) ? cor : null);
                pEx.Value = classExampleModel.TryGetValue(cls, out var ex) ? ex : "-";
                pDecks.Value = classDecks.TryGetValue(cls, out var dk) ? dk : 0;
                ins.ExecuteNonQuery();
            }
        }

        tx.Commit();
    }

    private static Dictionary<string, long> LoadIdMap(SqliteConnection conn, SqliteTransaction tx, string table, string keyColumn)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = $"SELECT id, {keyColumn} FROM {table}";
        var map = new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);
        using var reader = cmd.ExecuteReader();
        while (reader.Read()) map[reader.GetString(1)] = reader.GetInt64(0);
        return map;
    }

    private static void UpsertLibraries(SqliteConnection conn, SqliteTransaction tx, IReadOnlyList<WorkspaceLibrary> libs, CancellationToken cancellationToken)
    {
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = @"
            INSERT INTO libraries(name, path)
            VALUES ($name, $path)
            ON CONFLICT(name, path) DO NOTHING;";
        var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
        var pPath = cmd.CreateParameter(); pPath.ParameterName = "$path"; cmd.Parameters.Add(pPath);

        var count = 0;
        foreach (var lib in libs)
        {
            if (++count % 50 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            pName.Value = lib.Name;
            pPath.Value = Path.GetFullPath(lib.Path);
            cmd.ExecuteNonQuery();
        }
    }

    /// <summary>
    /// Upserts model records and their related metadata (sources, decks, and definition contexts) into the database using the provided transaction.
    /// </summary>
    /// <param name="models">The collection of SpectreModel entries to persist; for each model this writes or updates the core model row, inserts source files and decks, and, when present, creates or looks up corner/detail/section/include tokens and links them via model_contexts.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private static void UpsertModels(SqliteConnection conn, SqliteTransaction tx, IReadOnlyList<SpectreModel> models, CancellationToken cancellationToken)
    {
        using var insertModel = conn.CreateCommand();
        insertModel.Transaction = tx;
        insertModel.CommandText = @"
            INSERT INTO models(name, model_type, device_class, voltage_domain, threshold_flavor)
            VALUES ($name, $type, $class, $vdd, $vt)
            ON CONFLICT(name) DO UPDATE SET
                model_type=excluded.model_type,
                device_class=excluded.device_class,
                voltage_domain=excluded.voltage_domain,
                threshold_flavor=excluded.threshold_flavor;";
        var mName = insertModel.CreateParameter(); mName.ParameterName = "$name"; insertModel.Parameters.Add(mName);
        var mType = insertModel.CreateParameter(); mType.ParameterName = "$type"; insertModel.Parameters.Add(mType);
        var mClass = insertModel.CreateParameter(); mClass.ParameterName = "$class"; insertModel.Parameters.Add(mClass);
        var mVdd = insertModel.CreateParameter(); mVdd.ParameterName = "$vdd"; insertModel.Parameters.Add(mVdd);
        var mVt = insertModel.CreateParameter(); mVt.ParameterName = "$vt"; insertModel.Parameters.Add(mVt);

        using var getId = conn.CreateCommand();
        getId.Transaction = tx;
        getId.CommandText = "SELECT id FROM models WHERE name=$name";
        var gName = getId.CreateParameter(); gName.ParameterName = "$name"; getId.Parameters.Add(gName);

        using var insertSource = conn.CreateCommand();
        insertSource.Transaction = tx;
        insertSource.CommandText = @"
            INSERT INTO model_sources(model_id, path)
            VALUES ($model_id, $path)
            ON CONFLICT(model_id, path) DO NOTHING;";
        var sMid = insertSource.CreateParameter(); sMid.ParameterName = "$model_id"; insertSource.Parameters.Add(sMid);
        var sPath = insertSource.CreateParameter(); sPath.ParameterName = "$path"; insertSource.Parameters.Add(sPath);

        using var insertDeck = conn.CreateCommand();
        insertDeck.Transaction = tx;
        insertDeck.CommandText = @"
            INSERT INTO model_decks(model_id, path)
            VALUES ($model_id, $path)
            ON CONFLICT(model_id, path) DO NOTHING;";
        var dMid = insertDeck.CreateParameter(); dMid.ParameterName = "$model_id"; insertDeck.Parameters.Add(dMid);
        var dPath = insertDeck.CreateParameter(); dPath.ParameterName = "$path"; insertDeck.Parameters.Add(dPath);

        // store volts inline in models table; no auxiliary table

        // Dimension upserts for contexts
        using var insCornerTok = conn.CreateCommand(); insCornerTok.Transaction = tx; insCornerTok.CommandText = "INSERT INTO corners(name) VALUES ($n) ON CONFLICT(name) DO NOTHING;"; var pCornerName = insCornerTok.CreateParameter(); pCornerName.ParameterName = "$n"; insCornerTok.Parameters.Add(pCornerName);
        using var selCornerTok = conn.CreateCommand(); selCornerTok.Transaction = tx; selCornerTok.CommandText = "SELECT id FROM corners WHERE name=$n"; var pSelCorner = selCornerTok.CreateParameter(); pSelCorner.ParameterName = "$n"; selCornerTok.Parameters.Add(pSelCorner);

        using var insDetailTok = conn.CreateCommand(); insDetailTok.Transaction = tx; insDetailTok.CommandText = "INSERT INTO details(name) VALUES ($n) ON CONFLICT(name) DO NOTHING;"; var pDetailName = insDetailTok.CreateParameter(); pDetailName.ParameterName = "$n"; insDetailTok.Parameters.Add(pDetailName);
        using var selDetailTok = conn.CreateCommand(); selDetailTok.Transaction = tx; selDetailTok.CommandText = "SELECT id FROM details WHERE name=$n"; var pSelDetail = selDetailTok.CreateParameter(); pSelDetail.ParameterName = "$n"; selDetailTok.Parameters.Add(pSelDetail);

        using var insSectionTok = conn.CreateCommand(); insSectionTok.Transaction = tx; insSectionTok.CommandText = "INSERT INTO sections(name) VALUES ($n) ON CONFLICT(name) DO NOTHING;"; var pSectionName = insSectionTok.CreateParameter(); pSectionName.ParameterName = "$n"; insSectionTok.Parameters.Add(pSectionName);
        using var selSectionTok = conn.CreateCommand(); selSectionTok.Transaction = tx; selSectionTok.CommandText = "SELECT id FROM sections WHERE name=$n"; var pSelSection = selSectionTok.CreateParameter(); pSelSection.ParameterName = "$n"; selSectionTok.Parameters.Add(pSelSection);

        using var insIncludeTok = conn.CreateCommand(); insIncludeTok.Transaction = tx; insIncludeTok.CommandText = "INSERT INTO includes(path) VALUES ($p) ON CONFLICT(path) DO NOTHING;"; var pIncludePath = insIncludeTok.CreateParameter(); pIncludePath.ParameterName = "$p"; insIncludeTok.Parameters.Add(pIncludePath);
        using var selIncludeTok = conn.CreateCommand(); selIncludeTok.Transaction = tx; selIncludeTok.CommandText = "SELECT id FROM includes WHERE path=$p"; var pSelInclude = selIncludeTok.CreateParameter(); pSelInclude.ParameterName = "$p"; selIncludeTok.Parameters.Add(pSelInclude);

        using var insContext = conn.CreateCommand();
        insContext.Transaction = tx;
        insContext.CommandText = @"
            INSERT INTO model_contexts(model_id, corner_id, detail_id, section_id, include_id)
            VALUES ($mid, $cid, $did, $sid, $iid)
            ON CONFLICT(model_id, corner_id, detail_id, section_id, include_id) DO NOTHING;";
        var pMid = insContext.CreateParameter(); pMid.ParameterName = "$mid"; insContext.Parameters.Add(pMid);
        var pCid = insContext.CreateParameter(); pCid.ParameterName = "$cid"; insContext.Parameters.Add(pCid);
        var pDid = insContext.CreateParameter(); pDid.ParameterName = "$did"; insContext.Parameters.Add(pDid);
        var pSid = insContext.CreateParameter(); pSid.ParameterName = "$sid"; insContext.Parameters.Add(pSid);
        var pIid = insContext.CreateParameter(); pIid.ParameterName = "$iid"; insContext.Parameters.Add(pIid);

        var matchingConfig = PdkMatchingConfigManager.Load();
        var count = 0;
        foreach (var model in models)
        {
            if (++count % 20 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            mName.Value = model.Name;
            mType.Value = model.ModelType ?? string.Empty;
            mClass.Value = (int)model.DeviceClass;
            var tokenForModel = VddFormatting.ExtractTokenFromVoltageDomain(model.VoltageDomain, matchingConfig);
            if (VddFormatting.TryTokenToVolts(tokenForModel, out var modelVolts)) mVdd.Value = modelVolts; else mVdd.Value = DBNull.Value;
            mVt.Value = (object?)model.ThresholdFlavor ?? DBNull.Value;
            insertModel.ExecuteNonQuery();

            gName.Value = model.Name;
            var idObj = getId.ExecuteScalar();
            if (idObj is not long id) continue;
            // numeric volts already stored inline

            sMid.Value = id;
            foreach (var src in model.SourceFiles ?? Array.Empty<string>())
            {
                sPath.Value = Path.GetFullPath(src);
                insertSource.ExecuteNonQuery();
            }

            dMid.Value = id;
            foreach (var deck in model.Decks ?? Array.Empty<string>())
            {
                dPath.Value = Path.GetFullPath(deck);
                insertDeck.ExecuteNonQuery();
            }

            // Persist definition contexts if present; otherwise skip (no fabrication)
            pMid.Value = id;
            var contexts = model.DefinitionContexts;
            if (contexts is not null)
            {
                foreach (var ctx in contexts)
                {
                    long? cornerId = null, detailId = null, sectionId = null, includeId = null;
                    if (!string.IsNullOrWhiteSpace(ctx.Corner)) { pCornerName.Value = ctx.Corner; insCornerTok.ExecuteNonQuery(); pSelCorner.Value = ctx.Corner; cornerId = (long?)selCornerTok.ExecuteScalar(); }
                    if (!string.IsNullOrWhiteSpace(ctx.Detail)) { pDetailName.Value = ctx.Detail; insDetailTok.ExecuteNonQuery(); pSelDetail.Value = ctx.Detail; detailId = (long?)selDetailTok.ExecuteScalar(); }
                    if (!string.IsNullOrWhiteSpace(ctx.Section)) { pSectionName.Value = ctx.Section; insSectionTok.ExecuteNonQuery(); pSelSection.Value = ctx.Section; sectionId = (long?)selSectionTok.ExecuteScalar(); }
                    if (!string.IsNullOrWhiteSpace(ctx.IncludePath)) { pIncludePath.Value = Path.GetFullPath(ctx.IncludePath); insIncludeTok.ExecuteNonQuery(); pSelInclude.Value = Path.GetFullPath(ctx.IncludePath); includeId = (long?)selIncludeTok.ExecuteScalar(); }

                    pCid.Value = (object?)cornerId ?? DBNull.Value;
                    pDid.Value = (object?)detailId ?? DBNull.Value;
                    pSid.Value = (object?)sectionId ?? DBNull.Value;
                    pIid.Value = (object?)includeId ?? DBNull.Value;
                    insContext.ExecuteNonQuery();
                }
            }
        }
    }

    /// <summary>
    /// Upserts device rows into the devices table and ensures each device's view entries exist in <c>device_views</c>.
    /// </summary>
    /// <param name="conn">Open SQLite connection used to persist device metadata.</param>
    /// <param name="tx">Active transaction that scopes the upsert operations.</param>
    /// <param name="devices">Devices to persist; each device's canonical name is used as the key and the device's properties (including vt/vdd/tag values and layout/symbol flags) are written or updated and its view entries are inserted into <c>device_views</c>.</param>
    /// <param name="cancellationToken">Token to cancel the operation.</param>
    private static void UpsertDevices(SqliteConnection conn, SqliteTransaction tx, IReadOnlyList<Device> devices, CancellationToken cancellationToken)
    {
        using var insertDevice = conn.CreateCommand();
        insertDevice.Transaction = tx;
        insertDevice.CommandText = @"
            INSERT INTO devices(canonical_name, display_name, lib_name, lib_path, cell_name, cell_path, device_class, device_subclass, has_layout, has_symbol, vt_tags, vdd_tags, tags)
            VALUES ($key, $display, $lib, $libpath, $cell, $cellpath, $class, $subclass, $layout, $symbol, $vt, $vdd, $tags)
            ON CONFLICT(canonical_name) DO UPDATE SET
                display_name=excluded.display_name,
                lib_name=excluded.lib_name,
                lib_path=excluded.lib_path,
                cell_name=excluded.cell_name,
                cell_path=excluded.cell_path,
                device_class=excluded.device_class,
                device_subclass=excluded.device_subclass,
                has_layout=excluded.has_layout,
                has_symbol=excluded.has_symbol,
                vt_tags=excluded.vt_tags,
                vdd_tags=excluded.vdd_tags,
                tags=excluded.tags;";
        var pKey = insertDevice.CreateParameter(); pKey.ParameterName = "$key"; insertDevice.Parameters.Add(pKey);
        var pDisplay = insertDevice.CreateParameter(); pDisplay.ParameterName = "$display"; insertDevice.Parameters.Add(pDisplay);
        var pLib = insertDevice.CreateParameter(); pLib.ParameterName = "$lib"; insertDevice.Parameters.Add(pLib);
        var pLibPath = insertDevice.CreateParameter(); pLibPath.ParameterName = "$libpath"; insertDevice.Parameters.Add(pLibPath);
        var pCell = insertDevice.CreateParameter(); pCell.ParameterName = "$cell"; insertDevice.Parameters.Add(pCell);
        var pCellPath = insertDevice.CreateParameter(); pCellPath.ParameterName = "$cellpath"; insertDevice.Parameters.Add(pCellPath);
        var pClass = insertDevice.CreateParameter(); pClass.ParameterName = "$class"; insertDevice.Parameters.Add(pClass);
        var pSubclass = insertDevice.CreateParameter(); pSubclass.ParameterName = "$subclass"; insertDevice.Parameters.Add(pSubclass);
        var pLayout = insertDevice.CreateParameter(); pLayout.ParameterName = "$layout"; insertDevice.Parameters.Add(pLayout);
        var pSymbol = insertDevice.CreateParameter(); pSymbol.ParameterName = "$symbol"; insertDevice.Parameters.Add(pSymbol);
        var pVt = insertDevice.CreateParameter(); pVt.ParameterName = "$vt"; insertDevice.Parameters.Add(pVt);
        var pVdd = insertDevice.CreateParameter(); pVdd.ParameterName = "$vdd"; insertDevice.Parameters.Add(pVdd);
        var pTags = insertDevice.CreateParameter(); pTags.ParameterName = "$tags"; insertDevice.Parameters.Add(pTags);

        using var getId = conn.CreateCommand();
        getId.Transaction = tx;
        getId.CommandText = "SELECT id FROM devices WHERE canonical_name=$key";
        var gKey = getId.CreateParameter(); gKey.ParameterName = "$key"; getId.Parameters.Add(gKey);

        using var insertView = conn.CreateCommand();
        insertView.Transaction = tx;
        insertView.CommandText = @"
            INSERT INTO device_views(device_id, view)
            VALUES ($id, $view)
            ON CONFLICT(device_id, view) DO NOTHING;";
        var vId = insertView.CreateParameter(); vId.ParameterName = "$id"; insertView.Parameters.Add(vId);
        var vView = insertView.CreateParameter(); vView.ParameterName = "$view"; insertView.Parameters.Add(vView);

        // (no auxiliary device_vdds table; single REAL value per device)

        var count = 0;
        foreach (var d in devices)
        {
            if (++count % 50 == 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
            }

            pKey.Value = d.CanonicalName;
            pDisplay.Value = d.DisplayName;
            pLib.Value = d.LibraryName;
            pLibPath.Value = d.LibraryPath;
            pCell.Value = d.CellName;
            pCellPath.Value = d.CellPath;
            pClass.Value = (int)d.Class;
            pSubclass.Value = (int)d.Subclass;
            pLayout.Value = d.HasLayout ? 1 : 0;
            pSymbol.Value = d.HasSymbol ? 1 : 0;
            pVt.Value = string.Join(',', d.VtTags ?? Array.Empty<string>());
            // Persist a single REAL (first parsed token) for vdd_tags
            double? voltsValue = null;
            foreach (var t in d.VddTags ?? Array.Empty<string>())
            {
                if (VddFormatting.TryTokenToVolts(t, out var v)) { voltsValue = v; break; }
            }
            pVdd.Value = voltsValue.HasValue ? voltsValue.Value : DBNull.Value;
            pTags.Value = string.Join(',', d.Tags ?? Array.Empty<string>());
            insertDevice.ExecuteNonQuery();

            gKey.Value = d.CanonicalName;
            var idObj = getId.ExecuteScalar();
            if (idObj is not long id) continue;
            vId.Value = id;
            foreach (var view in d.Views ?? Array.Empty<string>())
            {
                vView.Value = view;
                insertView.ExecuteNonQuery();
            }
        }
    }
}
