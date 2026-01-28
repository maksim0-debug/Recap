using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Data.Sqlite;
using Recap.Utilities;

namespace Recap.Database
{
    public class FrameRepositoryDb
    {
        private readonly SqliteDbContext _context;

        public FrameRepositoryDb(SqliteDbContext context)
        {
            _context = context;
        }

        public void AddFrame(long timestamp, string appName)
        {
            _context.ExecuteWithRetry(connection => 
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

        public void DeleteDayMeta(string dayStr)
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (var cmd = new SqliteCommand("DELETE FROM FramesMeta WHERE DayStr = @day", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@day", dayStr);
                            cmd.ExecuteNonQuery();
                        }

                        using (var cmd = new SqliteCommand("DELETE FROM IndexedDays WHERE DayStr = @day", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@day", dayStr);
                            cmd.ExecuteNonQuery();
                        }

                        transaction.Commit();
                    }
                    catch (Exception)
                    {
                        transaction.Rollback();
                        throw;
                    }
                }
            });
        }

        public bool IsFrameProcessed(long timestamp)
        {
            return _context.ExecuteScalarWithRetry(connection =>
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
            return _context.ExecuteScalarWithRetry(connection =>
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
        
        public byte[] GetTextData(long timestamp)
        {
            return _context.ExecuteScalarWithRetry(connection =>
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

        public void SaveDailyActivityStats(string dayStr, double totalSeconds)
        {
            _context.ExecuteWithRetry(connection =>
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

        public void ClearAllDailyActivityStats()
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("DROP TABLE IF EXISTS DailyActivityStats", connection))
                {
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public double? GetDailyActivityStats(string dayStr)
        {
            return _context.ExecuteScalarWithRetry<double?>(connection =>
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
            _context.ExecuteWithRetry(connection =>
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

        public bool IsDayIndexed(string dayStr)
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("SELECT 1 FROM IndexedDays WHERE DayStr = @day", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    return cmd.ExecuteScalar() != null;
                }
            });
        }

        public bool HasVideoFrames(string dayStr)
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("SELECT 1 FROM FramesMeta WHERE DayStr = @day AND DataLength = -1 LIMIT 1", connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    return cmd.ExecuteScalar() != null;
                }
            });
        }

        public void MarkDayIndexed(string dayStr, int frameCount)
        {
            _context.ExecuteWithRetry(connection =>
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

            _context.ExecuteWithRetry(connection =>
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

        public void RestoreFramesMetaBulk(List<FrameIndex> frames, string dayStr)
        {
            if (frames == null || frames.Count == 0) return;

            _context.ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        var cmdGetApp = new SqliteCommand("SELECT ID FROM Apps WHERE Name = @name", connection, transaction);
                        cmdGetApp.Parameters.Add("@name", SqliteType.Text);

                        var cmdInsApp = new SqliteCommand("INSERT INTO Apps (Name) VALUES (@name)", connection, transaction);
                        cmdInsApp.Parameters.Add("@name", SqliteType.Text);

                        var cmdMeta = new SqliteCommand(
                           "INSERT OR IGNORE INTO FramesMeta (Timestamp, DayStr, AppName, AppID, DataOffset, DataLength) VALUES (@ts, @day, @app, @appid, @offset, @len)",
                           connection, transaction);
                        var pH_ts = cmdMeta.Parameters.Add("@ts", SqliteType.Integer);
                        var pH_day = cmdMeta.Parameters.Add("@day", SqliteType.Text);
                        var pH_app = cmdMeta.Parameters.Add("@app", SqliteType.Text);
                        var pH_appid = cmdMeta.Parameters.Add("@appid", SqliteType.Integer);
                        var pH_offset = cmdMeta.Parameters.Add("@offset", SqliteType.Integer);
                        var pH_len = cmdMeta.Parameters.Add("@len", SqliteType.Integer);

                        var cmdFrames = new SqliteCommand(
                           "INSERT OR IGNORE INTO Frames (Timestamp, AppName, IsProcessed, TextData, OcrText) VALUES (@ts, @app, 1, NULL, '')",
                           connection, transaction);
                        var pF_ts = cmdFrames.Parameters.Add("@ts", SqliteType.Integer);
                        var pF_app = cmdFrames.Parameters.Add("@app", SqliteType.Text);

                        var appIdCache = new Dictionary<string, long>();

                        foreach (var fr in frames)
                        {
                            string appName = fr.AppName ?? "Unknown";
                            
                            long appId;
                            if (!appIdCache.TryGetValue(appName, out appId))
                            {
                                cmdGetApp.Parameters["@name"].Value = appName;
                                var res = cmdGetApp.ExecuteScalar();
                                if (res != null)
                                {
                                    appId = Convert.ToInt64(res);
                                }
                                else
                                {
                                    cmdInsApp.Parameters["@name"].Value = appName;
                                    cmdInsApp.ExecuteNonQuery();
                                    
                                    using (var cmdRowId = new SqliteCommand("SELECT last_insert_rowid()", connection, transaction))
                                    {
                                        appId = (long)cmdRowId.ExecuteScalar();
                                    }
                                }
                                appIdCache[appName] = appId;
                            }

                            pH_ts.Value = fr.TimestampTicks;
                            pH_day.Value = dayStr;
                            pH_app.Value = appName;
                            pH_appid.Value = appId;
                            pH_offset.Value = fr.DataOffset;
                            pH_len.Value = fr.DataLength;
                            cmdMeta.ExecuteNonQuery();

                            pF_ts.Value = fr.TimestampTicks;
                            pF_app.Value = appName;
                            cmdFrames.ExecuteNonQuery();
                        }

                        var cmdCount = new SqliteCommand("SELECT COUNT(*) FROM FramesMeta WHERE DayStr = @day", connection, transaction);
                        cmdCount.Parameters.AddWithValue("@day", dayStr);
                        long totalCount = (long)cmdCount.ExecuteScalar();

                        var cmdIndex = new SqliteCommand(
                            "INSERT OR REPLACE INTO IndexedDays (DayStr, FrameCount, LastIndexed) VALUES (@day, @count, @now)",
                            connection, transaction);
                        cmdIndex.Parameters.AddWithValue("@day", dayStr);
                        cmdIndex.Parameters.AddWithValue("@count", totalCount);
                        cmdIndex.Parameters.AddWithValue("@now", DateTime.UtcNow.Ticks);
                        cmdIndex.ExecuteNonQuery();

                        var cmdStats = new SqliteCommand("DELETE FROM DailyActivityStats WHERE DayStr = @day", connection, transaction);
                        cmdStats.Parameters.AddWithValue("@day", dayStr);
                        cmdStats.ExecuteNonQuery();

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        DebugLogger.LogError("RestoreFramesMetaBulk", ex);
                        throw;
                    }
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
            _context.ExecuteWithRetry(connection =>
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

        public List<FrameIndex> GetFramesMetaForDay(string dayStr, HashSet<string> hiddenApps = null, List<string> appFilters = null)
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                var results = new List<FrameIndex>();
                string sql = "SELECT Timestamp, AppName, DataOffset, DataLength FROM FramesMeta WHERE DayStr = @day";
                
                if (appFilters != null && appFilters.Count > 0)
                {
                    var paramNames = new List<string>();
                    for (int i = 0; i < appFilters.Count; i++)
                    {
                        if (appFilters[i] == "All Applications") continue;
                        paramNames.Add($"@p{i}");
                    }
                    if (paramNames.Count > 0)
                    {
                        sql += $" AND AppName IN ({string.Join(",", paramNames)})";
                    }
                }
                
                sql += " ORDER BY Timestamp";

                using (var cmd = new SqliteCommand(sql, connection))
                {
                    cmd.Parameters.AddWithValue("@day", dayStr);
                    if (appFilters != null && appFilters.Count > 0)
                    {
                        int pIdx = 0;
                        for (int i = 0; i < appFilters.Count; i++)
                        {
                            if (appFilters[i] == "All Applications") continue;
                            cmd.Parameters.AddWithValue($"@p{pIdx}", appFilters[i]);
                            pIdx++;
                        }
                    }

                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            string appName = reader.IsDBNull(1) ? "" : reader.GetString(1);
                            if (string.IsNullOrEmpty(appName) || IsAppHidden(appName, hiddenApps)) continue;

                            results.Add(new FrameIndex
                            {
                                TimestampTicks = reader.GetInt64(0),
                                AppName = appName,
                                DataOffset = reader.GetInt64(2),
                                DataLength = reader.GetInt32(3)
                            });
                        }
                    }
                }
                return results;
            });
        }

        public List<MiniFrame> GetMiniFramesForDay(string dayStr, HashSet<int> hiddenAppIds = null)
        {
            return _context.ExecuteScalarWithRetry(connection =>
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
                            int appId = reader.IsDBNull(1) ? -1 : reader.GetInt32(1);
                            if (appId != -1 && hiddenAppIds != null && hiddenAppIds.Contains(appId)) continue;

                            results.Add(new MiniFrame
                            {
                                TimestampTicks = reader.GetInt64(0),
                                AppId = appId,
                                IntervalMs = 0     
                            });
                        }
                    }
                }
                return results;
            });
        }

        public FrameIndex? GetFrameIndex(long timestamp)
        {
            return _context.ExecuteScalarWithRetry<FrameIndex?>(connection =>
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
            return _context.ExecuteScalarWithRetry(connection =>
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

        public List<string> GetAppsForDay(string dayStr)
        {
            return _context.ExecuteScalarWithRetry(connection =>
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
            return _context.ExecuteScalarWithRetry(connection =>
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
            return _context.ExecuteScalarWithRetry(connection =>
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

        private static bool IsAppHidden(string appName, HashSet<string> hiddenApps)
        {
            if (hiddenApps == null || hiddenApps.Count == 0) return false;
            if (hiddenApps.Contains(appName)) return true;
            foreach (var h in hiddenApps)
            {
                if (appName.StartsWith(h, StringComparison.OrdinalIgnoreCase) && 
                   (appName.Length == h.Length || appName[h.Length] == '|'))
                    return true;
            }
            return false;
        }

        public string GetOcrText(long timestamp)
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                try
                {
                    string sql = "SELECT OcrText FROM Frames WHERE Timestamp <= @ts AND OcrText IS NOT NULL ORDER BY Timestamp DESC LIMIT 1";
                    using (var command = new SqliteCommand(sql, connection))
                    {
                        command.Parameters.AddWithValue("@ts", timestamp);
                        var result = command.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                        {
                            return result.ToString();
                        }
                    }
                    
                    string blobSql = "SELECT TextData FROM Frames WHERE Timestamp <= @ts AND TextData IS NOT NULL ORDER BY Timestamp DESC LIMIT 1";
                    using (var cmd = new SqliteCommand(blobSql, connection))
                    {
                        cmd.Parameters.AddWithValue("@ts", timestamp);
                        var blobResult = cmd.ExecuteScalar();
                        if (blobResult != null && blobResult != DBNull.Value)
                        {
                            byte[] blob = (byte[])blobResult;
                            try 
                            {
                                var words = BinaryCoordinatesPacker.Unpack(blob);
                                StringBuilder sb = new StringBuilder();
                                foreach (var w in words) sb.Append(w.T).Append(" ");
                                string recovered = sb.ToString().Trim();
                                
                                if (!string.IsNullOrEmpty(recovered))
                                {
                                    return recovered;
                                }
                            }
                            catch {}
                        }
                    }
                }
                catch { }
                return null;
            });
        }
    }
}
