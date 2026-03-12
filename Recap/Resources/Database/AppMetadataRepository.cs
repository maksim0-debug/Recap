using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Recap.Utilities;

namespace Recap.Database
{
    public class AppMetadataRepository
    {
        private readonly SqliteDbContext _context;

        public AppMetadataRepository(SqliteDbContext context)
        {
            _context = context;
        }

        public Dictionary<int, string> GetAppMap()
        {
            return _context.ExecuteScalarWithRetry(connection =>
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

        public void SetAppAlias(string rawName, string alias)
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("INSERT OR REPLACE INTO AppAliases (RawName, Alias) VALUES (@raw, @alias)", connection))
                {
                    cmd.Parameters.AddWithValue("@raw", rawName);
                    cmd.Parameters.AddWithValue("@alias", alias);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public void RemoveAppAlias(string rawName)
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("DELETE FROM AppAliases WHERE RawName = @raw", connection))
                {
                    cmd.Parameters.AddWithValue("@raw", rawName);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public Dictionary<string, string> LoadAppAliases()
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                var dict = new Dictionary<string, string>();
                using (var cmd = new SqliteCommand("SELECT RawName, Alias FROM AppAliases", connection))
                using (var reader = cmd.ExecuteReader())
                {
                    while (reader.Read())
                    {
                        string r = reader.GetString(0);
                        string a = reader.GetString(1);
                        if (!dict.ContainsKey(r)) dict[r] = a;
                    }
                }
                return dict;
            });
        }

        public void HideApp(string appName)
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("INSERT OR IGNORE INTO HiddenApps (AppName) VALUES (@app)", connection))
                {
                    cmd.Parameters.AddWithValue("@app", appName);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public void UnhideApp(string appName)
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("DELETE FROM HiddenApps WHERE AppName = @app", connection))
                {
                    cmd.Parameters.AddWithValue("@app", appName);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public string GetExecutablePath(string appName)
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("SELECT ExecutablePath FROM Apps WHERE Name = @name", connection))
                {
                    cmd.Parameters.AddWithValue("@name", appName);
                    var result = cmd.ExecuteScalar();
                    return result != DBNull.Value ? result as string : null;
                }
            });
        }

        public void UpdateExecutablePath(string appName, string path)
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("UPDATE Apps SET ExecutablePath = @path WHERE Name = @name", connection))
                {
                    cmd.Parameters.AddWithValue("@path", path);
                    cmd.Parameters.AddWithValue("@name", appName);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public HashSet<string> GetHiddenApps()
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                var result = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try 
                {
                    using (var cmd = new SqliteCommand("SELECT AppName FROM HiddenApps", connection))
                    using (var reader = cmd.ExecuteReader())
                    {
                        while (reader.Read())
                        {
                            result.Add(reader.GetString(0));
                        }
                    }
                }
                catch (SqliteException ex)
                {
                    DebugLogger.LogError("GetHiddenApps", ex);
                }
                return result;
            });
        }

        public void SetHasCustomIcon(string appName, bool hasCustom)
        {
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("UPDATE Apps SET HasCustomIcon = @val WHERE Name = @name", connection))
                {
                    cmd.Parameters.AddWithValue("@val", hasCustom ? 1 : 0);
                    cmd.Parameters.AddWithValue("@name", appName);
                    cmd.ExecuteNonQuery();
                }
            });
        }

        public bool CheckHasCustomIcon(string appName)
        {
            return _context.ExecuteScalarWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("SELECT HasCustomIcon FROM Apps WHERE Name = @name", connection))
                {
                    cmd.Parameters.AddWithValue("@name", appName);
                    object result = cmd.ExecuteScalar();
                    if (result != null && result != DBNull.Value)
                    {
                        return Convert.ToInt32(result) == 1;
                    }
                    return false;
                }
            });
        }

        public List<string> GetLegacyAppNames()
        {
            var list = new HashSet<string>();
            _context.ExecuteWithRetry(connection =>
            {
                string[] queries = new[] {
                    "SELECT DISTINCT Name FROM Apps WHERE Name LIKE '%|%'",
                    "SELECT DISTINCT AppName FROM FramesMeta WHERE AppName LIKE '%|%'"
                };

                foreach (var query in queries)
                {
                    try
                    {
                        using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand(query, connection))
                        {
                            using (var reader = cmd.ExecuteReader())
                            {
                                while (reader.Read())
                                {
                                    if (!reader.IsDBNull(0))
                                    {
                                        string name = reader.GetString(0);
                                        int pipeCount = name.Length - name.Replace("|", "").Length;
                                        if (pipeCount == 1 || pipeCount == 2)
                                        {
                                            list.Add(name);
                                        }
                                    }
                                }
                            }
                        }
                    }
                    catch (Exception) { }
                }
            });
            return new List<string>(list);
        }

        public void RenameApp(string oldName, string newName)
        {
            if (string.IsNullOrWhiteSpace(oldName) || string.IsNullOrWhiteSpace(newName)) return;
            if (oldName.Equals(newName, StringComparison.OrdinalIgnoreCase)) return;

            _context.ExecuteWithRetry(connection =>
            {
                using (var transaction = connection.BeginTransaction())
                {
                    try
                    {
                        long oldId = -1;
                        using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT ID FROM Apps WHERE Name = @n", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@n", oldName);
                            var res = cmd.ExecuteScalar();
                            if (res != null) oldId = Convert.ToInt64(res);
                        }

                        long existingNewId = -1;
                        using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT ID FROM Apps WHERE Name = @n", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@n", newName);
                            var res = cmd.ExecuteScalar();
                            if (res != null) existingNewId = Convert.ToInt64(res);
                        }

                        long finalNewId;

                        if (existingNewId != -1)
                        {
                            finalNewId = existingNewId;

                            if (oldId != -1 && oldId != existingNewId)
                            {
                                using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("UPDATE FramesMeta SET AppID = @nId, AppName = @nName WHERE AppID = @oId", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@nId", finalNewId);
                                    cmd.Parameters.AddWithValue("@nName", newName);
                                    cmd.Parameters.AddWithValue("@oId", oldId);
                                    cmd.ExecuteNonQuery();
                                }
                                using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("DELETE FROM Apps WHERE ID = @oId", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@oId", oldId);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                        }
                        else if (oldId != -1)
                        {
                            using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("UPDATE Apps SET Name = @nName WHERE ID = @oId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@nName", newName);
                                cmd.Parameters.AddWithValue("@oId", oldId);
                                cmd.ExecuteNonQuery();
                            }
                            finalNewId = oldId;

                            using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("UPDATE FramesMeta SET AppName = @nName WHERE AppID = @oId", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@nName", newName);
                                cmd.Parameters.AddWithValue("@oId", oldId);
                                cmd.ExecuteNonQuery();
                            }
                        }
                        else
                        {
                            using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("INSERT INTO Apps (Name) VALUES (@n)", connection, transaction))
                            {
                                cmd.Parameters.AddWithValue("@n", newName);
                                cmd.ExecuteNonQuery();
                            }
                            using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("SELECT last_insert_rowid()", connection, transaction))
                            {
                                finalNewId = Convert.ToInt64(cmd.ExecuteScalar());
                            }
                        }

                        using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand("UPDATE FramesMeta SET AppID = @nId, AppName = @nName WHERE AppName = @oName", connection, transaction))
                        {
                            cmd.Parameters.AddWithValue("@nId", finalNewId);
                            cmd.Parameters.AddWithValue("@nName", newName);
                            cmd.Parameters.AddWithValue("@oName", oldName);
                            cmd.ExecuteNonQuery();
                        }

                        string[] otherTables = new[] { "Frames", "HiddenApps", "SearchIndexV2" };
                        foreach (var table in otherTables)
                        {
                            try
                            {
                                using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand($"UPDATE {table} SET AppName = @nName WHERE AppName = @oName", connection, transaction))
                                {
                                    cmd.Parameters.AddWithValue("@oName", oldName);
                                    cmd.Parameters.AddWithValue("@nName", newName);
                                    cmd.ExecuteNonQuery();
                                }
                            }
                            catch { }         
                        }

                        transaction.Commit();
                    }
                    catch (Exception ex)
                    {
                        transaction.Rollback();
                        DebugLogger.LogError("RenameApp", ex);
                        throw;
                    }
                }
            });
        }

        public int RepairNullAppIds()
        {
            int updated = 0;
            _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand(
                    "INSERT OR IGNORE INTO Apps (Name) " +
                    "SELECT DISTINCT AppName FROM FramesMeta " +
                    "WHERE AppName != '' AND AppName IS NOT NULL AND AppID IS NULL",
                    connection))
                {
                    cmd.ExecuteNonQuery();
                }

                using (var cmd = new Microsoft.Data.Sqlite.SqliteCommand(
                    "UPDATE FramesMeta " +
                    "SET AppID = (SELECT ID FROM Apps WHERE Apps.Name = FramesMeta.AppName) " +
                    "WHERE AppID IS NULL",
                    connection))
                {
                    updated = cmd.ExecuteNonQuery();
                }
            });
            return updated;
        }
    }
}
