using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;
using Recap.Utilities;

namespace Recap.Database
{
    public struct NoteItem
    {
        public long Timestamp;
        public string Title;
        public string Description;
    }

    public class UserDataRepository
    {
        private readonly SqliteDbContext _context;

        public UserDataRepository(SqliteDbContext context)
        {
            _context = context;
        }

        public bool AddNote(long timestamp, string title, string description)
        {
             return _context.ExecuteScalarWithRetry(connection =>
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
             return _context.ExecuteScalarWithRetry(connection =>
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
             return _context.ExecuteScalarWithRetry(connection =>
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
             _context.ExecuteWithRetry(connection =>
            {
                using (var cmd = new SqliteCommand("DELETE FROM Notes WHERE Timestamp = @ts", connection))
                {
                    cmd.Parameters.AddWithValue("@ts", timestamp);
                    cmd.ExecuteNonQuery();
                }
            });
        }
    }
}
