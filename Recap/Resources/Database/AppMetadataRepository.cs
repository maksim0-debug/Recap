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
    }
}
