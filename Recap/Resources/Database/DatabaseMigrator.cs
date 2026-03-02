using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Microsoft.Data.Sqlite;
using Recap.Utilities;

namespace Recap.Database
{
    public class DatabaseMigrator
    {
        private readonly SqliteDbContext _context;

        public DatabaseMigrator(SqliteDbContext context)
        {
            _context = context;
        }

        public void Initialize()
        {
            bool exists = File.Exists(_context.DbPath);
            
            using (var connection = _context.CreateConnection())
            {
                using (var cmd = new SqliteCommand("PRAGMA journal_mode=WAL;", connection)) cmd.ExecuteScalar();
                using (var cmd = new SqliteCommand("PRAGMA auto_vacuum=FULL;", connection)) cmd.ExecuteNonQuery();

                try
                {
                    if (exists)
                    {
                        long dbSize = new FileInfo(_context.DbPath).Length;
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

                InitializeSchema(connection);
                RunMigrations(connection);
            }
        }

        private void InitializeSchema(SqliteConnection connection)
        {
            string sql = @"
                CREATE TABLE IF NOT EXISTS Frames (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp INTEGER NOT NULL UNIQUE,
                    AppName TEXT,
                    IsProcessed INTEGER DEFAULT 0,
                    TextData BLOB
                );
                
                CREATE INDEX IF NOT EXISTS idx_timestamp ON Frames(Timestamp);
                CREATE INDEX IF NOT EXISTS idx_processed ON Frames(IsProcessed) WHERE IsProcessed = 0;
                CREATE INDEX IF NOT EXISTS idx_app_ts ON Frames(AppName, Timestamp);

                CREATE TABLE IF NOT EXISTS FramesMeta (
                    Timestamp INTEGER PRIMARY KEY,
                    DayStr TEXT NOT NULL,
                    AppName TEXT,
                    DataOffset INTEGER DEFAULT 0,
                    DataLength INTEGER DEFAULT 0
                );

                CREATE INDEX IF NOT EXISTS idx_framesmeta_day ON FramesMeta(DayStr);
                CREATE INDEX IF NOT EXISTS idx_framesmeta_day_app ON FramesMeta(DayStr, AppName);
                
                CREATE INDEX IF NOT EXISTS idx_framesmeta_covering ON FramesMeta(Timestamp, AppName, DataOffset, DataLength);

                CREATE TABLE IF NOT EXISTS IndexedDays (
                    DayStr TEXT PRIMARY KEY,
                    FrameCount INTEGER DEFAULT 0,
                    LastIndexed INTEGER
                );

                CREATE TABLE IF NOT EXISTS Apps (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Name TEXT NOT NULL UNIQUE,
                    HasCustomIcon INTEGER DEFAULT 0,
                    ExecutablePath TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_apps_name ON Apps(Name);

                CREATE TABLE IF NOT EXISTS Notes (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    Timestamp INTEGER NOT NULL UNIQUE,
                    Title TEXT NOT NULL,
                    Description TEXT
                );
                CREATE INDEX IF NOT EXISTS idx_notes_timestamp ON Notes(Timestamp);

                CREATE TABLE IF NOT EXISTS AppAliases (
                    RawName TEXT PRIMARY KEY,
                    Alias TEXT NOT NULL
                );

                CREATE TABLE IF NOT EXISTS HiddenApps (
                    ID INTEGER PRIMARY KEY AUTOINCREMENT,
                    AppName TEXT UNIQUE
                );
            ";
            using (var command = new SqliteCommand(sql, connection))
            {
                command.ExecuteNonQuery();
            }
        }

        private void RunMigrations(SqliteConnection connection)
        {
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
                DebugLogger.LogError("DatabaseMigrator.NotesMigration", ex);
            }

            if (!HasColumn(connection, "Apps", "HasCustomIcon"))
            {
                try { using (var cmd = new SqliteCommand("ALTER TABLE Apps ADD COLUMN HasCustomIcon INTEGER DEFAULT 0;", connection)) cmd.ExecuteNonQuery(); } catch { }
            }

            if (!HasColumn(connection, "Apps", "ExecutablePath"))
            {
                try { using (var cmd = new SqliteCommand("ALTER TABLE Apps ADD COLUMN ExecutablePath TEXT;", connection)) cmd.ExecuteNonQuery(); } catch { }
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
            catch (Exception ex) { DebugLogger.LogError("DatabaseMigrator.AppIdMigration", ex); }

            try { using (var cmd = new SqliteCommand("ALTER TABLE Frames ADD COLUMN OcrText TEXT;", connection)) cmd.ExecuteNonQuery(); } catch { }

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
            catch (Exception ex) { DebugLogger.LogError("DatabaseMigrator.CleanupInvalidIndex", ex); }

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

            RunFtsMigrations(connection);
        }

        private bool HasColumn(SqliteConnection connection, string tableName, string columnName)
        {
            try
            {
                using (var cmd = new SqliteCommand($"PRAGMA table_info({tableName});", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        var name = reader["name"] as string;
                        if (string.Equals(name, columnName, StringComparison.OrdinalIgnoreCase))
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        private void RunFtsMigrations(SqliteConnection connection)
        {
            bool needFtsMigration = false;
            bool isOldFts4 = false;
            
            try
            {
                using (var checkFts = new SqliteCommand("SELECT sql FROM sqlite_master WHERE type='table' AND name='SearchIndexV2'", connection))
                {
                    var sqlDef = checkFts.ExecuteScalar()?.ToString() ?? "";
                    if (string.IsNullOrEmpty(sqlDef))
                    {
                        if (string.IsNullOrEmpty(sqlDef)) needFtsMigration = true;
                        
                    }
                }
            }
            catch { }

            if (needFtsMigration)
            {
                 try
                {
                    var backupData = new List<(long ts, string app, string text)>();
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
                    
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("DatabaseMigrator.FtsMigration", ex);
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
                                        var sb = new StringBuilder();
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
                                    pText.Value = TextCleaner.Normalize(item.text).ToLowerInvariant();
                                    pApp.Value = item.app;
                                    pTs.Value = item.ts;
                                    insertCmd.ExecuteNonQuery();
                                }
                            }
                            transaction.Commit();
                        }
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("DatabaseMigrator.TokenizerMigration", ex);
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
                    DebugLogger.LogError("DatabaseMigrator.InitVocab", ex);
                }
            }
        }
    }
}
