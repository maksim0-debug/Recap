using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using SQLitePCL;
using Recap.Utilities;
using Recap.Database;

namespace Recap
{
    public class OcrDatabase : IDisposable
    {
        private readonly SqliteDbContext _dbContext;
        private readonly FrameRepositoryDb _frameRepo;
        private readonly SearchRepository _searchRepo;
        private readonly AppMetadataRepository _appRepo;
        private readonly UserDataRepository _userRepo;
        private readonly DatabaseMigrator _migrator;

        public struct NoteItem
        {
            public long Timestamp;
            public string Title;
            public string Description;
        }

        public OcrDatabase(string storagePath)
        {
            Batteries_V2.Init();
            
            _dbContext = new SqliteDbContext(storagePath);
            _migrator = new DatabaseMigrator(_dbContext);
            _frameRepo = new FrameRepositoryDb(_dbContext);
            _searchRepo = new SearchRepository(_dbContext);
            _appRepo = new AppMetadataRepository(_dbContext);
            _userRepo = new UserDataRepository(_dbContext);
            
            _migrator.Initialize();
            _dbContext.InitializeKeepAlive();
        }

        public void Dispose()
        {
            _dbContext.Dispose();
        }

        public void Vacuum() => _dbContext.Vacuum();

        public DataTable ExecuteRawQuery(string sql)
        {
             return _dbContext.ExecuteScalarWithRetry(connection =>
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

        public void AddFrame(long timestamp, string appName) => _frameRepo.AddFrame(timestamp, appName);
        
        public bool IsFrameProcessed(long timestamp) => _frameRepo.IsFrameProcessed(timestamp);
        
        public List<long> GetUnprocessedFrames() => _frameRepo.GetUnprocessedFrames();
        
        public string GetOcrText(long timestamp) => _frameRepo.GetOcrText(timestamp);
        
        public byte[] GetTextData(long timestamp) => _frameRepo.GetTextData(timestamp);
        
        public void DeleteDayMeta(string dayStr) => _frameRepo.DeleteDayMeta(dayStr);
        
        public void MarkAsProcessed(long timestamp, string text, byte[] compressedData, bool isDuplicate) 
            => _searchRepo.MarkAsProcessed(timestamp, text, compressedData, isDuplicate);
        
        public void SetHasCustomIcon(string appName, bool hasCustom) => _appRepo.SetHasCustomIcon(appName, hasCustom);
        
        public bool CheckHasCustomIcon(string appName) => _appRepo.CheckHasCustomIcon(appName);

        public void SaveDailyActivityStats(string dayStr, double totalSeconds) => _frameRepo.SaveDailyActivityStats(dayStr, totalSeconds);
        
        public void ClearAllDailyActivityStats() => _frameRepo.ClearAllDailyActivityStats();
        
        public double? GetDailyActivityStats(string dayStr) => _frameRepo.GetDailyActivityStats(dayStr);
        
        public void ClearDailyActivityStats(string dayStr = null) => _frameRepo.ClearDailyActivityStats(dayStr);
        
        public bool IsDayIndexed(string dayStr) => _frameRepo.IsDayIndexed(dayStr);
        
        public bool HasVideoFrames(string dayStr) => _frameRepo.HasVideoFrames(dayStr);
        
        public void MarkDayIndexed(string dayStr, int frameCount) => _frameRepo.MarkDayIndexed(dayStr, frameCount);
        
        public void BulkInsertFramesMeta(List<(long Timestamp, string DayStr, string AppName, long DataOffset, int DataLength)> frames) 
            => _frameRepo.BulkInsertFramesMeta(frames);
        
        public void RestoreFramesMetaBulk(List<FrameIndex> frames, string dayStr) => _frameRepo.RestoreFramesMetaBulk(frames, dayStr);
        
        public void InsertFrameMeta(long timestamp, string dayStr, string appName, long dataOffset, int dataLength) 
            => _frameRepo.InsertFrameMeta(timestamp, dayStr, appName, dataOffset, dataLength);

        private bool IsAppHidden(string appName, HashSet<string> hiddenApps)
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

        public List<FrameIndex> GetFramesMetaForDay(string dayStr, List<string> appFilters = null) 
        {
            var hiddenApps = _appRepo.GetHiddenApps();
            return _frameRepo.GetFramesMetaForDay(dayStr, hiddenApps, appFilters);
        }

        public List<MiniFrame> GetMiniFramesForDay(string dayStr)
        {
             var hiddenApps = _appRepo.GetHiddenApps();
             var hiddenAppIds = new HashSet<int>();
             if (hiddenApps.Count > 0)
             {
                 var map = _appRepo.GetAppMap();
                 foreach(var kvp in map) 
                 {
                     if (IsAppHidden(kvp.Value, hiddenApps))
                        hiddenAppIds.Add(kvp.Key);
                 }
             }
             return _frameRepo.GetMiniFramesForDay(dayStr, hiddenAppIds);
        }

        public FrameIndex? GetFrameIndex(long timestamp) => _frameRepo.GetFrameIndex(timestamp);
        
        public HashSet<string> GetIndexedDaysSet() => _frameRepo.GetIndexedDaysSet();
        
        public List<string> GetAppsForDay(string dayStr) => _frameRepo.GetAppsForDay(dayStr);
        
        public int GetFrameCountForDay(string dayStr) => _frameRepo.GetFrameCountForDay(dayStr);
        
        public Dictionary<string, int> GetAllDaysWithCounts() => _frameRepo.GetAllDaysWithCounts();

        public Dictionary<int, string> GetAppMap() => _appRepo.GetAppMap();
        
        public void SetAppAlias(string rawName, string alias) => _appRepo.SetAppAlias(rawName, alias);
        
        public void RemoveAppAlias(string rawName) => _appRepo.RemoveAppAlias(rawName);
        
        public Dictionary<string, string> LoadAppAliases() => _appRepo.LoadAppAliases();
        
        public void HideApp(string appName) => _appRepo.HideApp(appName);
        
        public void UnhideApp(string appName) => _appRepo.UnhideApp(appName);
        
        public HashSet<string> GetHiddenApps() => _appRepo.GetHiddenApps();

        public List<(string Term, int Count)> GetSearchSuggestions(string prefix, int limit = 10, long? dateStart = null, long? dateEnd = null) 
            => _searchRepo.GetSearchSuggestions(prefix, limit, dateStart, dateEnd);

        public List<FrameIndex> Search(string searchText, List<string> appNameFilters = null) 
        {
            var hiddenApps = _appRepo.GetHiddenApps();
            return _searchRepo.Search(searchText, hiddenApps, appNameFilters);
        }
        
        public List<FrameIndex> SearchFramesMeta(string searchText)
        {
            var hiddenApps = _appRepo.GetHiddenApps();
            return _searchRepo.SearchFramesMeta(searchText, hiddenApps);
        }
        
        public void RebuildSearchIndex(IProgress<int> progress = null) => _searchRepo.RebuildSearchIndex(progress);

        public bool AddNote(long timestamp, string title, string description) => _userRepo.AddNote(timestamp, title, description);
        
        public List<NoteItem> GetNotesForPeriod(DateTime start, DateTime end)
        {
            var data = _userRepo.GetNotesForPeriod(start, end);
            return data.Select(n => new NoteItem { Timestamp = n.Timestamp, Title = n.Title, Description = n.Description }).ToList();
        }
        
        public List<NoteItem> SearchNotes(string query)
        {
             var data = _userRepo.SearchNotes(query);
            return data.Select(n => new NoteItem { Timestamp = n.Timestamp, Title = n.Title, Description = n.Description }).ToList();
        }
        
        public void DeleteNote(long timestamp) => _userRepo.DeleteNote(timestamp);
    }
}
