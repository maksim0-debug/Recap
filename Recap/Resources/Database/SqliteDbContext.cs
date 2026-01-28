using System;
using System.Data;
using System.IO;
using System.Threading;
using Microsoft.Data.Sqlite;
using Recap.Utilities;

namespace Recap.Database
{
    public class SqliteDbContext : IDisposable
    {
        private readonly string _connectionString;
        private SqliteConnection _keepAliveConnection;
        private readonly object _lock = new object();
        public string DbPath { get; private set; }

        public SqliteDbContext(string storagePath)
        {
            DbPath = Path.Combine(storagePath, "index.db");
            _connectionString = $"Data Source={DbPath};Mode=ReadWrite;Cache=Shared;Pooling=True;";
        }

        public void InitializeKeepAlive()
        {
            lock (_lock)
            {
                if (_keepAliveConnection != null) return;
                
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
                    DebugLogger.LogError("SqliteDbContext.KeepAlive", ex);
                }
            }
        }

        public void ConfigureConnection(SqliteConnection connection)
        {
            var settings = AdvancedSettings.Instance;
            using (var cmd = new SqliteCommand($"PRAGMA mmap_size={settings.DbMmapSize};", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand($"PRAGMA threads = {settings.DbThreads};", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand($"PRAGMA cache_size={settings.DbCacheSize};", connection)) cmd.ExecuteNonQuery();
            using (var cmd = new SqliteCommand($"PRAGMA synchronous={settings.DbSynchronous};", connection)) cmd.ExecuteNonQuery();
        }

        public void ExecuteWithRetry(Action<SqliteConnection> action, int maxRetries = 5)
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

        public T ExecuteScalarWithRetry<T>(Func<SqliteConnection, T> action, int maxRetries = 5)
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

        public SqliteConnection CreateConnection()
        {
            var conn = new SqliteConnection(_connectionString);
            conn.Open();
            ConfigureConnection(conn);
            return conn;
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
                    DebugLogger.LogError("SqliteDbContext.Vacuum", ex);
                }
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
                        if (_keepAliveConnection.State == ConnectionState.Open)
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
    }
}
