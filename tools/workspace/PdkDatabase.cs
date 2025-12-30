using System;
using System.IO;
using Microsoft.Data.Sqlite;

namespace Cascode.Workspace;

/// <summary>
/// Thin wrapper for opening and migrating the PDK database (SQLite).
/// </summary>
public sealed class PdkDatabase : IDisposable
{
    private readonly SqliteConnection _conn;

    private PdkDatabase(SqliteConnection conn)
    {
        _conn = conn;
    }

    public static PdkDatabase Open(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("dbPath is required", nameof(dbPath));
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(dbPath))!);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = false,
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        var db = new PdkDatabase(conn);
        db.EnsureSchema();
        return db;
    }

    // Open the database for read-only operations without running schema migrations.
    public static PdkDatabase OpenReadOnly(string dbPath)
    {
        if (string.IsNullOrWhiteSpace(dbPath))
            throw new ArgumentException("dbPath is required", nameof(dbPath));
        if (!File.Exists(dbPath))
            throw new FileNotFoundException("PDK database not found", dbPath);
        var builder = new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadOnly,
            Pooling = false,
        };
        var conn = new SqliteConnection(builder.ToString());
        conn.Open();
        // Do NOT call EnsureSchema() for read-only access.
        return new PdkDatabase(conn);
    }

    public SqliteConnection Connection => _conn;

    private void EnsureSchema()
    {
        // Idempotent: always run full schema creation with IF NOT EXISTS.
        CreateSchema();
    }

    /// <summary>
    /// Ensures the database schema exists and is migrated to the expected layout for the PDK.
    /// </summary>
    /// <remarks>
    /// Executes a series of idempotent DDL statements inside a single transaction to create tables,
    /// keys, unique constraints and foreign-key relationships required by the application.
    /// Calling this method multiple times is safe; it uses IF NOT EXISTS to avoid redeclaring objects
    /// and commits atomically only after all statements succeed, preserving atomic schema updates.
    /// </remarks>
    private void CreateSchema()
    {
        using var tx = _conn.BeginTransaction();
        using var cmd = _conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText =
            @"
            CREATE TABLE IF NOT EXISTS libraries (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL,
              path TEXT NOT NULL,
              UNIQUE(name, path)
            );

            CREATE TABLE IF NOT EXISTS models (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL UNIQUE,
              model_type TEXT NOT NULL,
              device_class INTEGER NOT NULL,
              voltage_domain TEXT NULL,
              threshold_flavor TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS model_sources (
              model_id INTEGER NOT NULL,
              path TEXT NOT NULL,
              PRIMARY KEY(model_id, path),
              FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS model_decks (
              model_id INTEGER NOT NULL,
              path TEXT NOT NULL,
              PRIMARY KEY(model_id, path),
              FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS corners (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS details (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS sections (
              id INTEGER PRIMARY KEY,
              name TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS includes (
              id INTEGER PRIMARY KEY,
              path TEXT NOT NULL UNIQUE
            );

            CREATE TABLE IF NOT EXISTS model_contexts (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              model_id INTEGER NOT NULL,
              corner_id INTEGER NULL,
              detail_id INTEGER NULL,
              section_id INTEGER NULL,
              include_id INTEGER NULL,
              UNIQUE(model_id, corner_id, detail_id, section_id, include_id),
              FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE,
              FOREIGN KEY(corner_id) REFERENCES corners(id) ON DELETE SET NULL,
              FOREIGN KEY(detail_id) REFERENCES details(id) ON DELETE SET NULL,
              FOREIGN KEY(section_id) REFERENCES sections(id) ON DELETE SET NULL,
              FOREIGN KEY(include_id) REFERENCES includes(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS devices (
              id INTEGER PRIMARY KEY,
              canonical_name TEXT NOT NULL UNIQUE,
              display_name TEXT NOT NULL,
              lib_name TEXT NOT NULL,
              lib_path TEXT NOT NULL,
              cell_name TEXT NOT NULL,
              cell_path TEXT NOT NULL,
              device_class INTEGER NOT NULL,
              device_subclass INTEGER NOT NULL DEFAULT 0,
              has_layout INTEGER NOT NULL,
              has_symbol INTEGER NOT NULL,
              vt_tags TEXT NULL,
              vdd_tags REAL NULL,
              tags TEXT NULL
            );

            CREATE TABLE IF NOT EXISTS device_views (
              device_id INTEGER NOT NULL,
              view TEXT NOT NULL,
              PRIMARY KEY(device_id, view),
              FOREIGN KEY(device_id) REFERENCES devices(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS provenance (
              key TEXT PRIMARY KEY,
              value TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS device_model_matches (
              device_id INTEGER NOT NULL,
              model_id INTEGER NOT NULL,
              quality TEXT NOT NULL,
              rank INTEGER NOT NULL,
              notes TEXT NULL,
              PRIMARY KEY(device_id, model_id),
              FOREIGN KEY(device_id) REFERENCES devices(id) ON DELETE CASCADE,
              FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS model_geometry (
              model_id INTEGER PRIMARY KEY,
              w_min REAL NULL,
              w_max REAL NULL,
              l_min REAL NULL,
              l_max REAL NULL,
              nf_min INTEGER NULL,
              nf_max INTEGER NULL,
              area_min REAL NULL,
              area_max REAL NULL,
              perim_min REAL NULL,
              perim_max REAL NULL,
              w_default REAL NULL,
              l_default REAL NULL,
              nf_default INTEGER NULL,
              source TEXT NULL,
              notes TEXT NULL,
              FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS device_geometry (
              device_id INTEGER PRIMARY KEY,
              w_min REAL NULL,
              w_max REAL NULL,
              l_min REAL NULL,
              l_max REAL NULL,
              nf_min INTEGER NULL,
              nf_max INTEGER NULL,
              w_default REAL NULL,
              l_default REAL NULL,
              nf_default INTEGER NULL,
              source TEXT NULL,
              notes TEXT NULL,
              FOREIGN KEY(device_id) REFERENCES devices(id) ON DELETE CASCADE
            );

            -- Precomputed rollups to make 'pdk devices' instantaneous
            CREATE TABLE IF NOT EXISTS device_class_summary (
              device_class INTEGER PRIMARY KEY,
              device_count INTEGER NOT NULL,
              matched_count INTEGER NOT NULL,
              ambiguous_count INTEGER NOT NULL,
              unmatched_count INTEGER NOT NULL,
              voltage_domains TEXT NULL,
              thresholds TEXT NULL,
              corners TEXT NULL,
              example_model TEXT NULL,
              decks INTEGER NOT NULL
            );

            -- Characterization LUT tables
            CREATE TABLE IF NOT EXISTS char_runs (
              id INTEGER PRIMARY KEY,
              model_id INTEGER NOT NULL,
              device_id INTEGER NULL,
              corner TEXT NOT NULL,
              backend TEXT NOT NULL,
              timestamp TEXT NOT NULL,
              w_m REAL NOT NULL,
              l_m REAL NOT NULL,
              nf INTEGER NOT NULL,
              vds REAL NOT NULL,
              vsb REAL NOT NULL,
              temperature_c REAL NOT NULL,
              status TEXT NOT NULL,
              job_dir TEXT NOT NULL,
              FOREIGN KEY(model_id) REFERENCES models(id) ON DELETE CASCADE,
              FOREIGN KEY(device_id) REFERENCES devices(id) ON DELETE SET NULL
            );

            CREATE TABLE IF NOT EXISTS char_lut_points (
              id INTEGER PRIMARY KEY,
              run_id INTEGER NOT NULL,
              vgs REAL NOT NULL,
              id_a REAL,
              gm REAL,
              gds REAL,
              gm_over_id REAL,
              vth REAL,
              vdsat REAL,
              ro REAL,
              gm_ro REAL,
              ft REAL,
              cgs REAL,
              cgd REAL,
              FOREIGN KEY(run_id) REFERENCES char_runs(id) ON DELETE CASCADE
            );

            CREATE TABLE IF NOT EXISTS char_run_summary (
              run_id INTEGER PRIMARY KEY,
              gm_id_peak REAL,
              vgs_at_peak_gm_id REAL,
              vth_extracted REAL,
              id_at_vth REAL,
              gm_ro_max REAL,
              ft_max REAL,
              saturation_margin REAL,
              FOREIGN KEY(run_id) REFERENCES char_runs(id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS idx_char_runs_model ON char_runs(model_id, corner);
            CREATE INDEX IF NOT EXISTS idx_char_lut_points_run ON char_lut_points(run_id);
            CREATE INDEX IF NOT EXISTS idx_char_runs_device ON char_runs(device_id, corner);
        ";
        cmd.ExecuteNonQuery();
        tx.Commit();
    }

    /// <summary>
    /// Releases resources used by the PdkDatabase and closes the underlying database connection.
    /// </summary>
    public void Dispose()
    {
        _conn.Dispose();
    }
}
