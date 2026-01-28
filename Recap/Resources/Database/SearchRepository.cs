using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Recap.Utilities;

namespace Recap.Database
{
    public class SearchRepository
    {
        private readonly SqliteDbContext _context;

        public SearchRepository(SqliteDbContext context)
        {
            _context = context;
        }

        public void MarkAsProcessed(long timestamp, string text, byte[] compressedData, bool isDuplicate)
        {
             _context.ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        string sql = "UPDATE Frames SET IsProcessed = 1, TextData = @blob, OcrText = @text WHERE Timestamp = @ts";
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

                            if (!isDuplicate && !string.IsNullOrWhiteSpace(text))
                            {
                                command.Parameters.AddWithValue("@text", text);
                            }
                            else
                            {
                                command.Parameters.AddWithValue("@text", DBNull.Value);
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
                                    ftsCmd.Parameters.AddWithValue("@text", TextCleaner.Normalize(text).ToLowerInvariant());
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
        
        public List<(string Term, int Count)> GetSearchSuggestions(string prefix, int limit = 10, long? dateStart = null, long? dateEnd = null)
        {
             if (!AdvancedSettings.Instance.EnableTextSearch) return new List<(string, int)>();

            if (string.IsNullOrWhiteSpace(prefix) || prefix.Length < 2) 
                return new List<(string, int)>();

            return _context.ExecuteScalarWithRetry(connection =>
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
                            AND f.Timestamp BETWEEN @start AND @end 
                            AND f.AppName NOT IN (SELECT AppName FROM HiddenApps)";

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

        public List<FrameIndex> Search(string searchText, HashSet<string> hiddenApps = null, List<string> appNameFilters = null)
        {
             if (!AdvancedSettings.Instance.EnableTextSearch) return new List<FrameIndex>();

            return _context.ExecuteScalarWithRetry(connection =>
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
                    
                    if (!string.IsNullOrWhiteSpace(ftsQuery) && appNameFilters != null && appNameFilters.Count > 0)
                    {
                        var clauses = new List<string>();
                        foreach(var app in appNameFilters) 
                        {
                             if (app == "All Applications") continue;
                             string safeApp = app.Replace("\"", "\"\"");
                             clauses.Add($"AppName: \"{safeApp}\"");
                        }

                        if (clauses.Count > 0)
                        {
                            ftsQuery += $" AND ({string.Join(" OR ", clauses)})";
                        }
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
                                if (IsAppHidden(app, hiddenApps)) continue; 
                                
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
                        DebugLogger.LogError("SearchRepository.Search", ex);
                    }
                }
                return results;
            });
        }
        
        public List<FrameIndex> SearchFramesMeta(string searchText, HashSet<string> hiddenApps = null)
        {
            var appMap = new Dictionary<long, string>();
            _context.ExecuteWithRetry(connection => 
            {
                using (var cmd = new SqliteCommand("SELECT ID, Name FROM Apps", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string name = reader.GetString(1);
                        bool hidden = false;
                        if (hiddenApps != null)
                        {
                            if (hiddenApps.Contains(name)) hidden = true;
                            else 
                            {
                                foreach(var h in hiddenApps) 
                                    if (name.StartsWith(h, StringComparison.OrdinalIgnoreCase) && (name.Length == h.Length || name[h.Length] == '|')) 
                                    { hidden = true; break; }
                            }
                        }

                        if (!hidden)
                            appMap[reader.GetInt64(0)] = name;
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

            long totalRows = _context.ExecuteScalarWithRetry(connection => 
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

                _context.ExecuteWithRetry(connection => 
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
                            bool hidden = false;

                            if (appId != -1) 
                            {
                                if (!appMap.TryGetValue(appId, out appName))
                                {
                                    hidden = true;
                                }
                            }

                            if (hidden) continue;

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

        public void RebuildSearchIndex(IProgress<int> progress = null)
        {
             _context.ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        using (var clearCmd = new SqliteCommand("INSERT INTO SearchIndexV2(SearchIndexV2) VALUES('delete-all')", connection, transaction))
                        {
                            clearCmd.ExecuteNonQuery();
                        }

                        long lastId = -1;
                        int count = 0;
                        bool hasMore = true;

                        var insertCmd = new SqliteCommand("INSERT INTO SearchIndexV2(rowid, OcrText, AppName, Timestamp) VALUES (@rowid, @text, @app, @ts)", connection, transaction);
                        var pRowId = insertCmd.Parameters.Add("@rowid", SqliteType.Integer);
                        var pText = insertCmd.Parameters.Add("@text", SqliteType.Text);
                        var pApp = insertCmd.Parameters.Add("@app", SqliteType.Text);
                        var pTs = insertCmd.Parameters.Add("@ts", SqliteType.Integer);

                        while (hasMore)
                        {
                            hasMore = false;
                            string sql = @"SELECT ID, Timestamp, AppName, TextData 
                                           FROM Frames 
                                           WHERE ID > @lastId AND IsProcessed = 1 AND TextData IS NOT NULL 
                                           ORDER BY ID ASC LIMIT 100";
                            
                            using (var readCmd = new SqliteCommand(sql, connection, transaction))
                            {
                                readCmd.Parameters.AddWithValue("@lastId", lastId);
                                using (var reader = readCmd.ExecuteReader())
                                {
                                    while (reader.Read())
                                    {
                                        hasMore = true;
                                        long rowid = reader.GetInt64(0);
                                        lastId = rowid;
                                        long ts = reader.GetInt64(1);
                                        string app = reader.IsDBNull(2) ? "" : reader.GetString(2);
                                        
                                        if (!reader.IsDBNull(3))
                                        {
                                            byte[] blob = (byte[])reader.GetValue(3);
                                            try 
                                            {
                                                var words = BinaryCoordinatesPacker.Unpack(blob);
                                                if (words != null && words.Count > 0)
                                                {
                                                    var sb = new StringBuilder();
                                                    foreach (var w in words)
                                                    {
                                                        sb.Append(w.T).Append(" ");
                                                    }
                                                    
                                                    string rawText = sb.ToString();
                                                    string cleanText = TextCleaner.Normalize(rawText);
                                                    
                                                    if (!string.IsNullOrWhiteSpace(cleanText))
                                                    {
                                                        pRowId.Value = rowid;
                                                        pText.Value = cleanText.ToLowerInvariant();
                                                        pApp.Value = app;
                                                        pTs.Value = ts;
                                                        insertCmd.ExecuteNonQuery();
                                                    }
                                                }
                                            }
                                            catch (Exception ex)
                                            {
                                                DebugLogger.LogError($"RebuildIndex error at ID {rowid}", ex);
                                            }
                                        }

                                        count++;
                                        if (count % 100 == 0) progress?.Report(count);
                                    }
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

                try
                {
                    using (var vacuumCmd = new SqliteCommand("VACUUM;", connection))
                    {
                        vacuumCmd.ExecuteNonQuery();
                    }
                    DebugLogger.Log("VACUUM completed after rebuild.");
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("RebuildIndex VACUUM", ex);
                }
            });
        }
    }
}
