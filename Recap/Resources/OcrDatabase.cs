using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLitePCL;

namespace Recap
{
    public class OcrDatabase : IDisposable
    {
        private readonly string _dbPath;
        private readonly string _connectionString;
        private SqliteConnection _keepAliveConnection;
        private readonly object _lock = new object();

        public OcrDatabase(string storagePath)
        {
            _dbPath = Path.Combine(storagePath, "index.db");
            _connectionString = $"Data Source={_dbPath};Mode=ReadWrite;Cache=Shared;Pooling=True;";

            Batteries_V2.Init();
            
            Initialize();

            lock (_lock)
            {
                try
                {
                    _keepAliveConnection = new SqliteConnection(_connectionString);
                    _keepAliveConnection.Open();
                    
                    using (var cmd = new SqliteCommand("PRAGMA journal_mode", _keepAliveConnection))
                    {
                        cmd.ExecuteScalar();    
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("OcrDatabase.KeepAlive", ex);
                }
            }
        }

        public DataTable ExecuteRawQuery(string sql)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var dt = new DataTable();
                using (var cmd = new SqliteCommand(sql, connection))
                using (var reader = cmd.ExecuteReader())
                {
                    for (int i = 0; i < reader.FieldCount; i++)
                    {
                        dt.Columns.Add(reader.GetName(i), typeof(object));
                    }

                    while (reader.Read())
                    {
                        var row = dt.NewRow();
                        for (int i = 0; i < reader.FieldCount; i++)
                        {
                            row[i] = reader.GetValue(i);
                        }
                        dt.Rows.Add(row);
                    }
                }
                return dt;
            });
        }

        public void Dispose()
        {
            lock (_lock)
            {
                try
                {
                    if (_keepAliveConnection != null)
                    {
                        if (_keepAliveConnection.State == System.Data.ConnectionState.Open)
                        {
                            try
                            {
                                using (var cmd = new SqliteCommand("PRAGMA wal_checkpoint(TRUNCATE);", _keepAliveConnection))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            catch { }

                            _keepAliveConnection.Close();
                        }
                        _keepAliveConnection.Dispose();
                        _keepAliveConnection = null;
                    }
                    
                    SqliteConnection.ClearAllPools();
                }
                catch { }
            }
        }

        public void Vacuum()
        {
            ExecuteWithRetry(connection =>
            {
                try
                {
                    using (var cmd = new SqliteCommand("VACUUM;", connection))
                    {
                        cmd.ExecuteNonQuery();
                    }
                    DebugLogger.Log("Manual VACUUM completed.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("OcrDatabase.Vacuum", ex);
                }
            });
        }

        private void ConfigureConnection(SqliteConnection connection)
        {
            var settings = AdvancedSettings.Instance;
            using (var cmd = new SqliteCommand($"PRAGMA mmap_size={settings.DbMmapSize};", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand($"PRAGMA threads = {settings.DbThreads};", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand($"PRAGMA cache_size={settings.DbCacheSize};", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand($"PRAGMA synchronous={settings.DbSynchronous};", connection)) cmd.ExecuteNonQuery();
        }

        private void ExecuteWithRetry(Action<SqliteConnection> action, int maxRetries = 5)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                SqliteConnection connection = null;
                try
                {
                    connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    ConfigureConnection(connection);
                    action(connection);
                    return;  
                }
                catch (SqliteException ex)
                {
                    if ((ex.ErrorCode == 5 || ex.ErrorCode == 6) && i < maxRetries - 1)
                    {
                        Thread.Sleep(50 * (i + 1));       
                        continue;
                    }
                    throw;            
                }
                finally
                {
                    connection?.Close();
                    connection?.Dispose();
                }
            }
        }

        private T ExecuteScalarWithRetry<T>(Func<SqliteConnection, T> action, int maxRetries = 5)
        {
            for (int i = 0; i < maxRetries; i++)
            {
                SqliteConnection connection = null;
                try
                {
                    connection = new SqliteConnection(_connectionString);
                    connection.Open();
                    ConfigureConnection(connection);
                    return action(connection);
                }
                catch (SqliteException ex)
                {
                    if ((ex.ErrorCode == 5 || ex.ErrorCode == 6) && i < maxRetries - 1)
                    {
                        Thread.Sleep(50 * (i + 1));
                        continue;
                    }
                    throw;
                }
                finally
                {
                    connection?.Close();
                    connection?.Dispose();
                }
            }
            return default(T);
        }

        private void Initialize()
        {
            bool exists = File.Exists(_dbPath);
            using (var connection = new SqliteConnection($"Data Source={_dbPath};Mode=ReadWriteCreate;Cache=Shared;"))
            {
                connection.Open();
                
                using (var cmd = new SqliteCommand("PRAGMA journal_mode=WAL;", connection)) cmd.ExecuteScalar();

                using (var cmd = new SqliteCommand("PRAGMA mmap_size=2147483648;", connection)) cmd.ExecuteNonQuery();

                using (var cmd = new SqliteCommand("PRAGMA threads = 4;", connection)) cmd.ExecuteNonQuery();

                using (var cmd = new SqliteCommand("PRAGMA auto_vacuum=FULL;", connection)) cmd.ExecuteNonQuery();
                using (var cmd = new SqliteCommand("PRAGMA cache_size=10000;", connection)) cmd.ExecuteNonQuery();   
                using (var cmd = new SqliteCommand("PRAGMA synchronous=NORMAL;", connection)) cmd.ExecuteNonQuery();   

                try
                {
                    if (exists)
                    {
                        long dbSize = new FileInfo(_dbPath).Length;
                        if (dbSize > 50 * 1024 * 1024)
                        {
                            long freePages = 0;
                            long pageSize = 4096;  
                            
                            using (var cmd = new SqliteCommand("PRAGMA freelist_count;", connection))
                                freePages = (long)cmd.ExecuteScalar();
                                
                            using (var cmd = new SqliteCommand("PRAGMA page_size;", connection))
                                pageSize = (long)cmd.ExecuteScalar();

                            long freeSpace = freePages * pageSize;
                            
                            if (freeSpace > dbSize * 0.2)
                            {
                                DebugLogger.Log($"Database fragmented ({freeSpace / 1024 / 1024} MB free). Running VACUUM...");
                                using (var cmd = new SqliteCommand("VACUUM;", connection))
                                {
                                    cmd.ExecuteNonQuery();
                                }
                                DebugLogger.Log("VACUUM completed.");
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("AutoVacuum", ex);
                }

                string sql = @"
                    CREATE TABLE IF NOT EXISTS Frames (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp INTEGER NOT NULL UNIQUE,
                        AppName TEXT,
                        IsProcessed INTEGER DEFAULT 0,
                        TextData BLOB
                    );
                    
                    -- Optimized indexes for fast queries
                    CREATE INDEX IF NOT EXISTS idx_timestamp ON Frames(Timestamp);
                    CREATE INDEX IF NOT EXISTS idx_processed ON Frames(IsProcessed) WHERE IsProcessed = 0;
                    CREATE INDEX IF NOT EXISTS idx_app_ts ON Frames(AppName, Timestamp);

                    -- Fast Frame Metadata table (for instant navigation)
                    CREATE TABLE IF NOT EXISTS FramesMeta (
                        Timestamp INTEGER PRIMARY KEY,
                        DayStr TEXT NOT NULL,
                        AppName TEXT,
                        DataOffset INTEGER DEFAULT 0,
                        DataLength INTEGER DEFAULT 0
                    );
                    CREATE INDEX IF NOT EXISTS idx_framesmeta_day ON FramesMeta(DayStr);
                    CREATE INDEX IF NOT EXISTS idx_framesmeta_day_app ON FramesMeta(DayStr, AppName);
                    
                    -- Covering Index for Global Search (Timestamp, AppName, DataOffset, DataLength)
                    -- This allows SQLite to satisfy the query purely from the index B-Tree
                    CREATE INDEX IF NOT EXISTS idx_framesmeta_covering ON FramesMeta(Timestamp, AppName, DataOffset, DataLength);

                    -- Track which days have been fully indexed from disk
                    CREATE TABLE IF NOT EXISTS IndexedDays (
                        DayStr TEXT PRIMARY KEY,
                        FrameCount INTEGER DEFAULT 0,
                        LastIndexed INTEGER
                    );

                    -- Normalization table for Apps (Dictionary Encoding)
                    CREATE TABLE IF NOT EXISTS Apps (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        Name TEXT NOT NULL UNIQUE
                    );
                    CREATE INDEX IF NOT EXISTS idx_apps_name ON Apps(Name);

                    -- Notes table
                    CREATE TABLE IF NOT EXISTS Notes (
                        ID INTEGER PRIMARY KEY AUTOINCREMENT,
                        Timestamp INTEGER NOT NULL UNIQUE,
                        Title TEXT NOT NULL,
                        Description TEXT
                    );
                    CREATE INDEX IF NOT EXISTS idx_notes_timestamp ON Notes(Timestamp);
                ";
                using (var command = new SqliteCommand(sql, connection))
                {
                    command.ExecuteNonQuery();
                }

                try
                {
                    bool migrationNeeded = false;
                    using (var cmd = new SqliteCommand("SELECT sql FROM sqlite_master WHERE type='table' AND name='Notes'", connection))
                    {
                        var tableSql = cmd.ExecuteScalar() as string;
                        if (tableSql != null && tableSql.Contains("Title TEXT NOT NULL UNIQUE"))
                        {
                            migrationNeeded = true;
                        }
                    }

                    if (migrationNeeded)
                    {
                        DebugLogger.Log("Migrating Notes table to remove UNIQUE constraint on Title...");
                        using (var transaction = connection.BeginTransaction())
                        {
                            using (var cmd = new SqliteCommand("CREATE TABLE Notes_new (ID INTEGER PRIMARY KEY AUTOINCREMENT, Timestamp INTEGER NOT NULL UNIQUE, Title TEXT NOT NULL, Description TEXT)", connection, transaction))
                                cmd.ExecuteNonQuery();
                            
                            using (var cmd = new SqliteCommand("INSERT INTO Notes_new (ID, Timestamp, Title, Description) SELECT ID, Timestamp, Title, Description FROM Notes", connection, transaction))
                                cmd.ExecuteNonQuery();

                            using (var cmd = new SqliteCommand("DROP TABLE Notes", connection, transaction))
                                cmd.ExecuteNonQuery();

                            using (var cmd = new SqliteCommand("ALTER TABLE Notes_new RENAME TO Notes", connection, transaction))
                                cmd.ExecuteNonQuery();

                            using (var cmd = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_notes_timestamp ON Notes(Timestamp)", connection, transaction))
                                cmd.ExecuteNonQuery();

                            transaction.Commit();
                        }
                        DebugLogger.Log("Notes table migration completed.");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("OcrDatabase.NotesMigration", ex);
                }

                try { using (var cmd = new SqliteCommand("ALTER TABLE FramesMeta ADD COLUMN DataOffset INTEGER DEFAULT 0;", connection)) cmd.ExecuteNonQuery(); } catch { }
                try { using (var cmd = new SqliteCommand("ALTER TABLE FramesMeta ADD COLUMN DataLength INTEGER DEFAULT 0;", connection)) cmd.ExecuteNonQuery(); } catch { }
                try { using (var cmd = new SqliteCommand("ALTER TABLE FramesMeta ADD COLUMN AppID INTEGER;", connection)) cmd.ExecuteNonQuery(); } catch { }

                try 
                {
                    bool migrationNeeded = false;
                    using (var cmd = new SqliteCommand("SELECT 1 FROM FramesMeta WHERE AppID IS NULL AND AppName IS NOT NULL LIMIT 1", connection))
                        if (cmd.ExecuteScalar() != null) migrationNeeded = true;

                    if (migrationNeeded)
                    {
                        DebugLogger.Log("Migrating FramesMeta to use AppID (Normalization)...");
                        using (var transaction = connection.BeginTransaction())
                        {
                            using (var cmd = new SqliteCommand("INSERT OR IGNORE INTO Apps (Name) SELECT DISTINCT AppName FROM FramesMeta WHERE AppName IS NOT NULL", connection, transaction))
                                cmd.ExecuteNonQuery();
                            
                            using (var cmd = new SqliteCommand("UPDATE FramesMeta SET AppID = (SELECT ID FROM Apps WHERE Apps.Name = FramesMeta.AppName) WHERE AppID IS NULL", connection, transaction))
                                cmd.ExecuteNonQuery();
                                
                            transaction.Commit();
                        }
                        DebugLogger.Log("AppID Migration completed.");
                    }
                }
                catch (Exception ex) { DebugLogger.LogError("OcrDatabase.AppIdMigration", ex); }

                try { using (var cmd = new SqliteCommand("CREATE INDEX IF NOT EXISTS idx_framesmeta_covering_v2 ON FramesMeta(Timestamp, AppID, DataOffset, DataLength)", connection)) cmd.ExecuteNonQuery(); } catch { }

                try
                {
                    using (var cmd = new SqliteCommand("DELETE FROM IndexedDays WHERE DayStr IN (SELECT DISTINCT DayStr FROM FramesMeta WHERE DataLength = 0)", connection))
                    {
                        int deletedDays = cmd.ExecuteNonQuery();
                        if (deletedDays > 0) DebugLogger.Log($"Removed {deletedDays} days with invalid index data.");
                    }
                    
                    using (var cmd = new SqliteCommand("DELETE FROM FramesMeta WHERE DataLength = 0", connection))
                    {
                        int deletedFrames = cmd.ExecuteNonQuery();
                        if (deletedFrames > 0) DebugLogger.Log($"Removed {deletedFrames} invalid frames from index.");
                    }
                }
                catch (Exception ex) { DebugLogger.LogError("OcrDatabase.CleanupInvalidIndex", ex); }

                try
                {
                    using (var testFts5 = new SqliteCommand("CREATE VIRTUAL TABLE IF NOT EXISTS _fts5_test USING fts5(test_col); DROP TABLE IF EXISTS _fts5_test;", connection))
                    {
                        testFts5.ExecuteNonQuery();
                        DebugLogger.Log("✓ FTS5 module is available and working");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("✗ FTS5 NOT AVAILABLE - Database optimization will be limited!", ex);
                }

                bool needFtsMigration = false;
                bool isOldFts4 = false;
                
                try
                {
                    using (var checkFts = new SqliteCommand("SELECT sql FROM sqlite_master WHERE type='table' AND name='SearchIndexV2'", connection))
                    {
                        var sqlDef = checkFts.ExecuteScalar()?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(sqlDef))
                        {
                            needFtsMigration = false;
                        }
                    }
                }
                catch { }

                if (needFtsMigration)
                {
                    try
                    {
                        var backupData = new List<(long ts, string app, string text)>();
                        
                        if (isOldFts4)
                        {
                            try
                            {
                                using (var readCmd = new SqliteCommand("SELECT Timestamp, AppName, OcrText FROM Frames WHERE OcrText IS NOT NULL AND OcrText != ''", connection))
                                using (var reader = readCmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        backupData.Add((
                                            reader.GetInt64(0),
                                            reader.IsDBNull(1) ? "" : reader.GetString(1),
                                            reader.IsDBNull(2) ? "" : reader.GetString(2)
                                        ));
                                    }
                                }
                            }
                            catch
                            {
                                var rowids = new List<long>();
                                using (var rowidCmd = new SqliteCommand("SELECT rowid FROM SearchIndexV2", connection))
                                using (var rowidReader = rowidCmd.ExecuteReader())
                                {
                                    while (rowidReader.Read())
                                    {
                                        rowids.Add(rowidReader.GetInt64(0));
                                    }
                                }
                                
                                foreach (var rowid in rowids)
                                {
                                    try
                                    {
                                        using (var dataCmd = new SqliteCommand("SELECT OcrText, AppName, Timestamp FROM SearchIndexV2 WHERE rowid = @id", connection))
                                        {
                                            dataCmd.Parameters.AddWithValue("@id", rowid);
                                            using (var dataReader = dataCmd.ExecuteReader())
                                            {
                                                if (dataReader.Read())
                                                {
                                                    backupData.Add((
                                                        dataReader.IsDBNull(2) ? 0 : dataReader.GetInt64(2),
                                                        dataReader.IsDBNull(1) ? "" : dataReader.GetString(1),
                                                        dataReader.IsDBNull(0) ? "" : dataReader.GetString(0)
                                                    ));
                                                }
                                            }
                                        }
                                    }
                                    catch { }
                                }
                            }
                        }
                        else
                        {
                            using (var readCmd = new SqliteCommand("SELECT Timestamp, AppName, OcrText FROM SearchIndexV2", connection))
                            using (var reader = readCmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    backupData.Add((
                                        reader.GetInt64(0),
                                        reader.IsDBNull(1) ? "" : reader.GetString(1),
                                        reader.IsDBNull(2) ? "" : reader.GetString(2)
                                    ));
                                }
                            }
                        }

                        using (var dropCmd = new SqliteCommand("DROP TABLE IF EXISTS SearchIndexV2", connection))
                        {
                            dropCmd.ExecuteNonQuery();
                        }

                        string fts5Sql = @"
                            CREATE VIRTUAL TABLE SearchIndexV2 USING fts5(
                                OcrText, 
                                AppName, 
                                Timestamp,
                                content='',
                                tokenize='porter unicode61'
                            );
                        ";
                        using (var createCmd = new SqliteCommand(fts5Sql, connection))
                        {
                            createCmd.ExecuteNonQuery();
                        }

                        if (backupData.Count > 0)
                        {
                            using (var transaction = connection.BeginTransaction())
                            {
                                var timestampToId = new Dictionary<long, long>();
                                using (var mapCmd = new SqliteCommand("SELECT ID, Timestamp FROM Frames", connection, transaction))
                                using (var mapReader = mapCmd.ExecuteReader())
                                {
                                    while (mapReader.Read())
                                    {
                                        timestampToId[mapReader.GetInt64(1)] = mapReader.GetInt64(0);
                                    }
                                }

                                string insertSql = "INSERT INTO SearchIndexV2(rowid, OcrText, AppName, Timestamp) VALUES (@rowid, @text, @app, @ts)";
                                using (var insertCmd = new SqliteCommand(insertSql, connection, transaction))
                                {
                                    var pRowId = insertCmd.Parameters.Add("@rowid", SqliteType.Integer);
                                    var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
                                    var pApp = insertCmd.Parameters.Add("@app", SqliteType.Text);
                                    var pTs = insertCmd.Parameters.Add("@ts", SqliteType.Integer);

                                    int successCount = 0;
                                    foreach (var item in backupData)
                                    {
                                        if (timestampToId.TryGetValue(item.ts, out long frameId))
                                        {
                                            pRowId.Value = frameId;
                                            pText.Value = item.text.ToLowerInvariant();
                                            pApp.Value = item.app;
                                            pTs.Value = item.ts;
                                            
                                            try
                                            {
                                                insertCmd.ExecuteNonQuery();
                                                successCount++;
                                            }
                                            catch { }   
                                        }
                                    }
                                    
                                    DebugLogger.Log($"FTS Migration: Migrated {successCount}/{backupData.Count} records to FTS5");
                                }
                                transaction.Commit();
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogError("OcrDatabase.FtsMigration", ex);
                    }
                }
                bool needsTokenizerMigration = false;
                try
                {
                    using (var checkFts = new SqliteCommand("SELECT sql FROM sqlite_master WHERE type='table' AND name='SearchIndexV2'", connection))
                    {
                        var sqlDef = checkFts.ExecuteScalar()?.ToString() ?? "";
                        if (!string.IsNullOrEmpty(sqlDef) && (sqlDef.IndexOf("porter", StringComparison.OrdinalIgnoreCase) >= 0 || !sqlDef.Contains("unicode61")))
                        {
                            needsTokenizerMigration = true;
                            DebugLogger.Log($"FTS Migration needed. Current definition: {sqlDef}");
                        }
                    }
                }
                catch { }

                if (needsTokenizerMigration)
                {
                    DebugLogger.Log("Migrating FTS5 to unicode61 tokenizer...");
                    try
                    {
                        var framesToReindex = new List<(long rowid, long ts, string app, string text)>();
                        
                        using (var readCmd = new SqliteCommand("SELECT ID, Timestamp, AppName, TextData FROM Frames WHERE IsProcessed = 1 AND TextData IS NOT NULL", connection))
                        using (var reader = readCmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                try
                                {
                                    long id = reader.GetInt64(0);
                                    long ts = reader.GetInt64(1);
                                    string app = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                    byte[] blob = (byte[])reader["TextData"];
                                    
                                    if (blob != null && blob.Length > 0)
                                    {
                                        var words = BinaryCoordinatesPacker.Unpack(blob);
                                        if (words != null && words.Count > 0)
                                        {
                                            var sb = new System.Text.StringBuilder();
                                            foreach (var w in words) sb.Append(w.T).Append(" ");
                                            framesToReindex.Add((id, ts, app, sb.ToString()));
                                        }
                                    }
                                }
                                catch { }
                            }
                        }

                        using (var dropCmd = new SqliteCommand("DROP TABLE IF EXISTS SearchIndexV2", connection))
                        {
                            dropCmd.ExecuteNonQuery();
                        }

                        string fts5Sql = @"
                            CREATE VIRTUAL TABLE SearchIndexV2 USING fts5(
                                OcrText, 
                                AppName, 
                                Timestamp,
                                content='',
                                tokenize='unicode61 remove_diacritics 1'
                            );
                        ";
                        using (var createCmd = new SqliteCommand(fts5Sql, connection))
                        {
                            createCmd.ExecuteNonQuery();
                        }

                        if (framesToReindex.Count > 0)
                        {
                            using (var transaction = connection.BeginTransaction())
                            {
                                string insertSql = "INSERT INTO SearchIndexV2(rowid, OcrText, AppName, Timestamp) VALUES (@rowid, @text, @app, @ts)";
                                using (var insertCmd = new SqliteCommand(insertSql, connection, transaction))
                                {
                                    var pRowId = insertCmd.Parameters.Add("@rowid", SqliteType.Integer);
                                    var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
                                    var pApp = insertCmd.Parameters.Add("@app", SqliteType.Text);
                                    var pTs = insertCmd.Parameters.Add("@ts", SqliteType.Integer);

                                    foreach (var item in framesToReindex)
                                    {
                                        pRowId.Value = item.rowid;
                                        pText.Value = item.text.ToLowerInvariant();
                                        pApp.Value = item.app;
                                        pTs.Value = item.ts;
                                        insertCmd.ExecuteNonQuery();
                                    }
                                }
                                transaction.Commit();
                            }
                            DebugLogger.Log($"Re-indexed {framesToReindex.Count} frames with unicode61");
                        }
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogError("OcrDatabase.TokenizerMigration", ex);
                    }
                }
                else if (AdvancedSettings.Instance.EnableTextSearch)
                {
                    string fts5Sql = @"
                        CREATE VIRTUAL TABLE IF NOT EXISTS SearchIndexV2 USING fts5(
                            OcrText, 
                            AppName, 
                            Timestamp,
                            content='',
                            tokenize='unicode61 remove_diacritics 1'
                        );
                    ";
                    using (var createCmd = new SqliteCommand(fts5Sql, connection))
                    {
                        createCmd.ExecuteNonQuery();
                    }
                }

                if (AdvancedSettings.Instance.EnableTextSearch)
                {
                    try 
                    {
                        string vocabSql = "CREATE VIRTUAL TABLE IF NOT EXISTS SearchTerms USING fts5vocab(SearchIndexV2, row);";
                        using (var cmd = new SqliteCommand(vocabSql, connection))
                        {
                            cmd.ExecuteNonQuery();
                        }
                    }
                    catch (Exception ex) 
                    {
                        DebugLogger.LogError("OcrDatabase.InitVocab", ex);
                    }
                }

                try
                {
                    using (var checkCol = new SqliteCommand("SELECT OcrText FROM Frames LIMIT 1", connection))
                    {
                        checkCol.ExecuteNonQuery();
                        
                        using (var countCmd = new SqliteCommand("SELECT COUNT(*) FROM Frames WHERE OcrText IS NOT NULL AND OcrText != ''", connection))
                        {
                            long count = (long)countCmd.ExecuteScalar();
                            if (count > 0)
                            {
                                using (var ftsCountCmd = new SqliteCommand("SELECT COUNT(*) FROM SearchIndexV2", connection))
                                {
                                    long ftsCount = (long)ftsCountCmd.ExecuteScalar();
                                    if (ftsCount == 0)
                                    {
                                        var framesToMigrate = new List<(long rowid, long ts, string app, string text)>();
                                            using (var readCmd = new SqliteCommand("SELECT ID, Timestamp, AppName, OcrText FROM Frames WHERE IsProcessed = 1 AND OcrText IS NOT NULL", connection))
                                        using (var reader = readCmd.ExecuteReader())
                                        {
                                            while (reader.Read())
                                            {
                                                framesToMigrate.Add((
                                                    reader.GetInt64(0),
                                                    reader.GetInt64(1),
                                                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                                                    reader.IsDBNull(3) ? "" : reader.GetString(3)
                                                ));
                                            }
                                        }

                                        if (framesToMigrate.Count > 0)
                                        {
                                            using (var transaction = connection.BeginTransaction())
                                            {
                                                string insertSql = "INSERT INTO SearchIndexV2(rowid, OcrText, AppName, Timestamp) VALUES (@rowid, @text, @app, @ts)";
                                                using (var insertCmd = new SqliteCommand(insertSql, connection, transaction))
                                                {
                                                    var pRowId = insertCmd.Parameters.Add("@rowid", SqliteType.Integer);
                                                    var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
                                                    var pApp = insertCmd.Parameters.Add("@app", SqliteType.Text);
                                                    var pTs = insertCmd.Parameters.Add("@ts", SqliteType.Integer);

                                                    foreach (var item in framesToMigrate)
                                                    {
                                                        pRowId.Value = item.rowid;
                                                        pText.Value = item.text.ToLowerInvariant();
                                                        pApp.Value = item.app;
                                                        pTs.Value = item.ts;
                                                        insertCmd.ExecuteNonQuery();
                                                    }
                                                }
                                                transaction.Commit();
                                            }
                                        }
                                    }
                                }

                                using (var clearCmd = new SqliteCommand("UPDATE Frames SET OcrText = NULL", connection))
                                {
                                    clearCmd.ExecuteNonQuery();
                                }
                            }
                        }
                    }
                }
                catch
                {
                }
            }
        }

        public void AddFrame(long timestamp, string appName)
        {
            ExecuteWithRetry(connection => 
            {
                string sql = "INSERT OR IGNORE INTO Frames (Timestamp, AppName, IsProcessed) VALUES (@ts, @app, 0)";
                using (var command = new SqliteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ts", timestamp);
                    command.Parameters.AddWithValue("@app", appName ?? "");
                    command.ExecuteNonQuery();
                }
            });
        }

        public bool IsFrameProcessed(long timestamp)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                string sql = "SELECT IsProcessed FROM Frames WHERE Timestamp = @ts";
                using (var command = new SqliteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ts", timestamp);
                    var result = command.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result) == 1;
                    }
                }
                return false;        
            });
        }

        public List<long> GetUnprocessedFrames()
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var list = new List<long>();
                string sql = "SELECT Timestamp FROM Frames WHERE IsProcessed = 0 ORDER BY Timestamp DESC LIMIT 50";
                using (var command = new SqliteCommand(sql, connection))
                {
                    using (var reader = command.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            list.Add(reader.GetInt64(0));
                        }
                    }
                }
                return list;
            });
        }

        public void MarkAsProcessed(long timestamp, string text, byte[] compressedData, bool isDuplicate)
        {
            ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string sql = "UPDATE Frames SET IsProcessed = 1, TextData = @blob WHERE Timestamp = @ts";
                        using (var command = new SqliteCommand(sql, connection, transaction))
                        {
                            command.Parameters.AddWithValue("@ts", timestamp);
                            if (!isDuplicate && compressedData != null && compressedData.Length > 0)
                            {
                                command.Parameters.Add("@blob", SqliteType.Blob).Value = compressedData;
                            }
                            else
                            {
                                command.Parameters.Add("@blob", SqliteType.Blob).Value = DBNull.Value;
                            }
                            command.ExecuteNonQuery();
                        }

                        if (AdvancedSettings.Instance.EnableTextSearch && !isDuplicate && !string.IsNullOrWhiteSpace(text))
                        {
                            string getAppSql = "SELECT AppName FROM Frames WHERE Timestamp = @ts";
                            string appName = "";
                            using (var getCmd = new SqliteCommand(getAppSql, connection, transaction))
                            {
                                getCmd.Parameters.AddWithValue("@ts", timestamp);
                                var result = getCmd.ExecuteScalar();
                                if (result != null && result != DBNull.Value) appName = result.ToString();
                            }

                            string getIdSql = "SELECT ID FROM Frames WHERE Timestamp = @ts";
                            long frameId = 0;
                            using (var getIdCmd = new SqliteCommand(getIdSql, connection, transaction))
                            {
                                getIdCmd.Parameters.AddWithValue("@ts", timestamp);
                                var idResult = getIdCmd.ExecuteScalar();
                                if (idResult != null && idResult != DBNull.Value)
                                    frameId = Convert.ToInt64(idResult);
                            }

                            if (frameId > 0)
                            {
                                string ftsInsertSql = "INSERT INTO SearchIndexV2(rowid, OcrText, AppName, Timestamp) VALUES (@rowid, @text, @app, @ts)";
                                using (var ftsCmd = new SqliteCommand(ftsInsertSql, connection, transaction))
                                {
                                    ftsCmd.Parameters.AddWithValue("@rowid", frameId);
                                    ftsCmd.Parameters.AddWithValue("@text", text.ToLowerInvariant());
                                    ftsCmd.Parameters.AddWithValue("@app", appName);
                                    ftsCmd.Parameters.AddWithValue("@ts", timestamp);
                                    ftsCmd.ExecuteNonQuery();
                                }
                            }
                        }
                        
                        transaction.Commit();
                    }
                    catch
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            });
        }

        public byte[] GetTextData(long timestamp)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                string sql = @"
                    SELECT TextData 
                    FROM Frames 
                    WHERE Timestamp <= @ts AND TextData IS NOT NULL 
                    ORDER BY Timestamp DESC 
                    LIMIT 1";

                using (var command = new SqliteCommand(sql, connection))
                {
                    command.Parameters.AddWithValue("@ts", timestamp);
                    var result = command.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        return (byte[])result;
                    }
                }
                return null;
            });
        }

        public List<(string Term, int Count)> GetSearchSuggestions(string prefix, int limit = 10, long? dateStart = null, long? dateEnd = null)
        {
            if (!AdvancedSettings.Instance.EnableTextSearch) return new List<(string, int)>();

            if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2) 
                return new List<(string, int)>();

            return ExecuteScalarWithRetry(connection =>
            {
                var results = new List<(string, int)>();
                
                try 
                {
                    using (var check = new SqliteCommand("SELECT 1 FROM sqlite_master WHERE name='SearchTerms'", connection))
                        if (check.ExecuteScalar() == null) return results;
                }
                catch { return results; }

                string prefixLower = prefix.ToLowerInvariant();

                if (dateStart == null || dateEnd == null)
                {
                    string sql = @"
                        SELECT term, doc 
                        FROM SearchTerms 
                        WHERE term LIKE @p 
                        ORDER BY doc DESC 
                        LIMIT @limit";

                    using (var cmd = new SqliteCommand(sql, connection))
                    {
                        cmd.Parameters.AddWithValue("@p", prefixLower + "%");
                        cmd.Parameters.AddWithValue("@limit", limit);

                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                results.Add((reader.GetString(0), reader.GetInt32(1)));
                            }
                        }
                    }
                }
                else
                {
                    var candidates = new List<string>();
                    string candSql = @"
                        SELECT term FROM SearchTerms 
                        WHERE term LIKE @p 
                        ORDER BY doc DESC 
                        LIMIT 50";    

                    using (var cmd = new SqliteCommand(candSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@p", prefixLower + "%");
                        using (var reader = cmd.ExecuteReader())
                        {
                            while (reader.Read()) candidates.Add(reader.GetString(0));
                        }
                    }

                    foreach (var term in candidates)
                    {
                        string countSql = @"
                            SELECT COUNT(*) 
                            FROM SearchIndexV2 s
                            JOIN Frames f ON f.ID = s.rowid
                            WHERE SearchIndexV2 MATCH @term 
                            AND f.Timestamp BETWEEN @start AND @end";

                        using (var cmd = new SqliteCommand(countSql, connection))
                        {
                            string safeTerm = "\"" + term.Replace("\"", "\"\"") + "\"";
                            cmd.Parameters.AddWithValue("@term", safeTerm);
                            cmd.Parameters.AddWithValue("@start", dateStart.Value);
                            cmd.Parameters.AddWithValue("@end", dateEnd.Value);

                            long count = (long)cmd.ExecuteScalar();
                            if (count > 0)
                            {
                                results.Add((term, (int)count));
                            }
                        }
                    }

                    results = results.OrderByDescending(x => x.Item2).Take(limit).ToList();
                }

                return results;
            });
        }

        public List<FrameIndex> Search(string searchText, string appNameFilter = null)
        {
            if (!AdvancedSettings.Instance.EnableTextSearch) return new List<FrameIndex>();

            return ExecuteScalarWithRetry(connection =>
            {
                var results = new List<FrameIndex>();
                
                string checkTableSql = "SELECT name FROM sqlite_master WHERE type='table' AND name='SearchIndexV2'";
                using (var checkCmd = new SqliteCommand(checkTableSql, connection))
                {
                    var tableExists = checkCmd.ExecuteScalar();
                    if (tableExists == null)
                    {
                        return results;
                    }
                }
                
                string sql = @"
                    SELECT f.Timestamp, f.AppName 
                    FROM SearchIndexV2 s
                    JOIN Frames f ON f.ID = s.rowid
                    WHERE SearchIndexV2 MATCH @match
                ";
                
                using (var command = new SqliteCommand(sql, connection))
                {
                    string ftsQuery = "";
                    if (!string.IsNullOrEmpty(searchText))
                    {
                        string lowerSearch = searchText.ToLowerInvariant();
                        var words = lowerSearch.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
                        
                        for (int i = 0; i < words.Length; i++)
                        {
                            string term = words[i].Replace("\"", "");
                            
                            if (string.IsNullOrWhiteSpace(term)) continue;

                            ftsQuery += $"{term}* ";
                        }
                    }
                    
                    if (!string.IsNullOrWhiteSpace(ftsQuery) && !string.IsNullOrEmpty(appNameFilter) && appNameFilter != "All Applications")
                    {
                        string safeApp = appNameFilter.Replace("\"", "\"\"");
                        ftsQuery += $" AND AppName: \"{safeApp}\"";
                    }

                    if (string.IsNullOrWhiteSpace(ftsQuery)) return results;

                    command.Parameters.AddWithValue("@match", ftsQuery.Trim());

                    try
                    {
                        using (var reader = command.ExecuteReader())
                        {
                            while (reader.Read())
                            {
                                long ts = reader.GetInt64(0);
                                string app = reader.IsDBNull(1) ? "" : reader.GetString(1);
                                
                                results.Add(new FrameIndex
                                {
                                    TimestampTicks = ts,
                                    AppName = app,
                                    DataLength = 0,
                                    DataOffset = 0
                                });
                            }
                        }
                    }
                    catch (SqliteException ex)
                    {
                        DebugLogger.LogError("OcrDatabase.Search", ex);
                    }
                }
                return results;
            });
        }

        public void SaveDailyActivityStats(string dayStr, double totalSeconds)
        {
            ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand(
                    "CREATE TABLE IF NOT EXISTS DailyActivityStats (DayStr TEXT PRIMARY KEY, TotalSeconds REAL)", connection))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqliteCommand(
                    "INSERT OR REPLACE INTO DailyActivityStats (DayStr, TotalSeconds) VALUES (@day, @sec)", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    cmd.Parameters.AddWithValue("@sec", totalSeconds);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public double? GetDailyActivityStats(string dayStr)
        {
            return ExecuteScalarWithRetry<double?>(connection =>
            {
                try
                {
                    using (var cmd = new SqliteCommand("SELECT TotalSeconds FROM DailyActivityStats WHERE DayStr = @day", connection))
                    {
                        cmd.Parameters.AddWithValue("@day", dayStr);
                        var res = cmd.ExecuteScalar();
                        if (res != null && res != DBNull.Value)
                        {
                            return Convert.ToDouble(res);
                        }
                    }
                }
                catch 
                {
                }
                return null;
            });
        }

        public void ClearDailyActivityStats(string dayStr = null)
        {
            ExecuteWithRetry(connection =>
            {
                try
                {
                    string sql = "DELETE FROM DailyActivityStats";
                    if (dayStr != null) sql += " WHERE DayStr = @day";
                    
                    using (var cmd = new SqliteCommand(sql, connection))
                    {
                        if (dayStr != null) cmd.Parameters.AddWithValue("@day", dayStr);
                        cmd.ExecuteNonQuery();
                    }
                }
                catch { }
            });
        }

        #region Fast Frame Metadata (for instant navigation)

        public bool IsDayIndexed(string dayStr)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("SELECT 1 FROM IndexedDays WHERE DayStr = @day", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    return cmd.ExecuteScalar() != null;
                }
            });
        }

        public void MarkDayIndexed(string dayStr, int frameCount)
        {
            ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand(
                    "INSERT OR REPLACE INTO IndexedDays (DayStr, FrameCount, LastIndexed) VALUES (@day, @count, @ts)", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    cmd.Parameters.AddWithValue("@count", frameCount);
                    cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.Ticks);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public void BulkInsertFramesMeta(List<(long Timestamp, string DayStr, string AppName, long DataOffset, int DataLength)> frames)
        {
            if (frames == null || frames.Count == 0) return;

            ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    using (var cmd = new SqliteCommand(
                        "INSERT OR IGNORE INTO FramesMeta (Timestamp, DayStr, AppName, DataOffset, DataLength) VALUES (@ts, @day, @app, @offset, @len)", connection, transaction))
                    {
                        var pTs = cmd.Parameters.Add("@ts", SqliteType.Integer);
                        var pDay = cmd.Parameters.Add("@day", SqliteType.Text);
                        var pApp = cmd.Parameters.Add("@app", SqliteType.Text);
                        var pOffset = cmd.Parameters.Add("@offset", SqliteType.Integer);
                        var pLen = cmd.Parameters.Add("@len", SqliteType.Integer);

                        foreach (var frame in frames)
                        {
                            pTs.Value = frame.Timestamp;
                            pDay.Value = frame.DayStr;
                            pApp.Value = frame.AppName ?? "";
                            pOffset.Value = frame.DataOffset;
                            pLen.Value = frame.DataLength;
                            cmd.ExecuteNonQuery();
                        }
                    }
                    transaction.Commit();
                }
            });
        }

        private long GetOrCreateAppId(SqliteConnection connection, string appName, SqliteTransaction transaction = null)
        {
            if (string.IsNullOrEmpty(appName)) return -1;
            
            using (var cmd = new SqliteCommand("SELECT ID FROM Apps WHERE Name = @name", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@name", appName);
                var res = cmd.ExecuteScalar();
                if (res != null) return Convert.ToInt64(res);
            }
            
            using (var cmd = new SqliteCommand("INSERT INTO Apps (Name) VALUES (@name)", connection, transaction))
            {
                cmd.Parameters.AddWithValue("@name", appName);
                cmd.ExecuteNonQuery();
                return (long)new SqliteCommand("SELECT last_insert_rowid()", connection, transaction).ExecuteScalar();
            }
        }

        public void InsertFrameMeta(long timestamp, string dayStr, string appName, long dataOffset, int dataLength)
        {
            ExecuteWithRetry(connection =>
            {
                long appId = GetOrCreateAppId(connection, appName);

                using (var cmd = new SqliteCommand(
                    "INSERT OR IGNORE INTO FramesMeta (Timestamp, DayStr, AppName, AppID, DataOffset, DataLength) VALUES (@ts, @day, @app, @appid, @offset, @len)", connection))
                {
                    cmd.Parameters.AddWithValue("@ts", timestamp);
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    cmd.Parameters.AddWithValue("@app", appName ?? "");
                    cmd.Parameters.AddWithValue("@appid", appId == -1 ? DBNull.Value : (object)appId);
                    cmd.Parameters.AddWithValue("@offset", dataOffset);
                    cmd.Parameters.AddWithValue("@len", dataLength);
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new SqliteCommand(
                    "INSERT INTO IndexedDays (DayStr, FrameCount, LastIndexed) VALUES (@day, 1, @ts) ON CONFLICT(DayStr) DO UPDATE SET FrameCount = FrameCount + 1, LastIndexed = @ts", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    cmd.Parameters.AddWithValue("@ts", DateTime.UtcNow.Ticks);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public List<FrameIndex> GetFramesMetaForDay(string dayStr, string appFilter = null)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var results = new List<FrameIndex>();
                string sql = "SELECT Timestamp, AppName, DataOffset, DataLength FROM FramesMeta WHERE DayStr = @day";
                
                if (!string.IsNullOrEmpty(appFilter) && appFilter != "All Applications")
                {
                    sql += " AND AppName = @app";
                }
                sql += " ORDER BY Timestamp";

                using (var cmd = new SqliteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    if (!string.IsNullOrEmpty(appFilter) && appFilter != "All Applications")
                    {
                        cmd.Parameters.AddWithValue("@app", appFilter);
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new FrameIndex
                            {
                                TimestampTicks = reader.GetInt64(0),
                                AppName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                DataOffset = reader.GetInt64(2),
                                DataLength = reader.GetInt32(3)
                            });
                        }
                    }
                }
                return results;
            });
        }

        public List<MiniFrame> GetMiniFramesForDay(string dayStr)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var results = new List<MiniFrame>();
                string sql = "SELECT Timestamp, AppID, 0 FROM FramesMeta WHERE DayStr = @day ORDER BY Timestamp";
                using (var cmd = new SqliteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new MiniFrame
                            {
                                TimestampTicks = reader.GetInt64(0),
                                AppId = reader.IsDBNull(1) ? -1 : reader.GetInt32(1),
                                IntervalMs = 0     
                            });
                        }
                    }
                }
                return results;
            });
        }

        public Dictionary<int, string> GetAppMap()
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var map = new Dictionary<int, string>();
                using (var cmd = new SqliteCommand("SELECT ID, Name FROM Apps", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        map[reader.GetInt32(0)] = reader.GetString(1);
                    }
                }
                return map;
            });
        }

        public FrameIndex? GetFrameIndex(long timestamp)
        {
            return ExecuteScalarWithRetry<FrameIndex?>(connection =>
            {
                string sql = "SELECT Timestamp, AppName, DataOffset, DataLength FROM FramesMeta WHERE Timestamp = @ts";
                using (var cmd = new SqliteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@ts", timestamp);
                    using (var reader = cmd.ExecuteReader())
                    {
                        if (reader.Read())
                        {
                            return new FrameIndex
                            {
                                TimestampTicks = reader.GetInt64(0),
                                AppName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                                DataOffset = reader.GetInt64(2),
                                DataLength = reader.GetInt32(3),
                                IntervalMs = 0      
                            };
                        }
                    }
                }
                return null;
            });
        }

        public HashSet<string> GetIndexedDaysSet()
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var results = new HashSet<string>();
                using (var cmd = new SqliteCommand("SELECT DayStr FROM IndexedDays", connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(reader.GetString(0));
                        }
                    }
                }
                return results;
            });
        }

        public List<FrameIndex> SearchFramesMeta(string searchText)
        {
            var appMap = new Dictionary<long, string>();
            ExecuteWithRetry(connection => 
            {
                using (var cmd = new SqliteCommand("SELECT ID, Name FROM Apps", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        appMap[reader.GetInt64(0)] = reader.GetString(1);
                    }
                }
            });

            List<long> matchingIds = null;
            if (!string.IsNullOrEmpty(searchText))
            {
                matchingIds = new List<long>();
                foreach(var kvp in appMap)
                {
                    if (kvp.Value.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0)
                        matchingIds.Add(kvp.Key);
                }
                
                if (matchingIds.Count == 0) return new List<FrameIndex>();
            }

            long totalRows = ExecuteScalarWithRetry(connection => 
            {
                string countSql = "SELECT COUNT(*) FROM FramesMeta";
                if (matchingIds != null)
                {
                    countSql += $" WHERE AppID IN ({string.Join(",", matchingIds)})";
                }
                using (var cmd = new SqliteCommand(countSql, connection))
                {
                    var res = cmd.ExecuteScalar();
                    return res != null ? Convert.ToInt64(res) : 0;
                }
            });

            if (totalRows == 0) return new List<FrameIndex>();

            int dop = Math.Max(6, Environment.ProcessorCount);
            long chunkSize = (long)Math.Ceiling((double)totalRows / dop);
            
            var partitions = new List<FrameIndex>[dop];

            Parallel.For(0, dop, i => 
            {
                long offset = i * chunkSize;
                if (offset >= totalRows) 
                {
                    partitions[i] = new List<FrameIndex>();
                    return;
                }
                
                long limit = chunkSize;

                ExecuteWithRetry(connection => 
                {
                    var localList = new List<FrameIndex>((int)limit);
                    
                    string sql = "SELECT Timestamp, AppID, DataOffset, DataLength FROM FramesMeta";
                    
                    if (matchingIds != null)
                    {
                        sql += $" WHERE AppID IN ({string.Join(",", matchingIds)})";
                    }
                    
                    sql += $" ORDER BY Timestamp LIMIT {limit} OFFSET {offset}";

                    using (var cmd = new SqliteCommand(sql, connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            long appId = reader.IsDBNull(1) ? -1 : reader.GetInt64(1);
                            string appName = "";
                            if (appId != -1) 
                            {
                                appMap.TryGetValue(appId, out appName);
                            }

                            localList.Add(new FrameIndex
                            {
                                TimestampTicks = reader.GetInt64(0),
                                AppName = appName ?? "",
                                DataOffset = reader.GetInt64(2),
                                DataLength = reader.GetInt32(3)
                            });
                        }
                    }
                    partitions[i] = localList;
                });
            });

            var finalResults = new List<FrameIndex>((int)totalRows);
            for (int i = 0; i < dop; i++)
            {
                if (partitions[i] != null)
                {
                    finalResults.AddRange(partitions[i]);
                }
            }
            
            return finalResults;
        }

        public List<string> GetAppsForDay(string dayStr)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var results = new List<string>();
                using (var cmd = new SqliteCommand(
                    "SELECT DISTINCT AppName FROM FramesMeta WHERE DayStr = @day AND AppName IS NOT NULL AND AppName != '' ORDER BY AppName", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(reader.GetString(0));
                        }
                    }
                }
                return results;
            });
        }

        public int GetFrameCountForDay(string dayStr)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("SELECT COUNT(*) FROM FramesMeta WHERE DayStr = @day", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            });
        }

        public Dictionary<string, int> GetAllDaysWithCounts()
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var results = new Dictionary<string, int>();
                using (var cmd = new SqliteCommand("SELECT DayStr, FrameCount FROM IndexedDays", connection))
                {
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string day = reader.GetString(0);
                            int count = reader.GetInt32(1);
                            results[day] = count;
                        }
                    }
                }
                return results;
            });
        }

        #endregion

        #region Notes

        public struct NoteItem
        {
            public long Timestamp;
            public string Title;
            public string Description;
        }

        public bool AddNote(long timestamp, string title, string description)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                try
                {
                    using (var cmd = new SqliteCommand("INSERT INTO Notes (Timestamp, Title, Description) VALUES (@ts, @title, @desc)", connection))
                    {
                        cmd.Parameters.AddWithValue("@ts", timestamp);
                        cmd.Parameters.AddWithValue("@title", title);
                        cmd.Parameters.AddWithValue("@desc", description ?? "");
                        cmd.ExecuteNonQuery();
                        return true;
                    }
                }
                catch (SqliteException ex)
                {
                    if (ex.SqliteErrorCode == 19)    
                    {
                        return false;
                    }
                    throw;
                }
            });
        }

        public List<NoteItem> GetNotesForPeriod(DateTime start, DateTime end)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var results = new List<NoteItem>();
                long startTicks = start.Ticks;
                long endTicks = end.Ticks;

                using (var cmd = new SqliteCommand("SELECT Timestamp, Title, Description FROM Notes WHERE Timestamp BETWEEN @start AND @end ORDER BY Timestamp", connection))
                {
                    cmd.Parameters.AddWithValue("@start", startTicks);
                    cmd.Parameters.AddWithValue("@end", endTicks);
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new NoteItem
                            {
                                Timestamp = reader.GetInt64(0),
                                Title = reader.GetString(1),
                                Description = reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }
                }
                return results;
            });
        }

        public List<NoteItem> SearchNotes(string query)
        {
            return ExecuteScalarWithRetry(connection =>
            {
                var results = new List<NoteItem>();
                string sql = "SELECT Timestamp, Title, Description FROM Notes WHERE Title LIKE @q OR Description LIKE @q ORDER BY Timestamp DESC";
                using (var cmd = new SqliteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@q", $"%{query}%");
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            results.Add(new NoteItem
                            {
                                Timestamp = reader.GetInt64(0),
                                Title = reader.GetString(1),
                                Description = reader.IsDBNull(2) ? "" : reader.GetString(2)
                            });
                        }
                    }
                }
                return results;
            });
        }

        public void DeleteNote(long timestamp)
        {
            ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("DELETE FROM Notes WHERE Timestamp = @ts", connection))
                {
                    cmd.Parameters.AddWithValue("@ts", timestamp);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        #endregion
    }
}
