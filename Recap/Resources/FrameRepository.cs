using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Recap
{
    public class FrameRepository
    {
        private readonly string _storagePath;
        private OcrDatabase _ocrDb;

        private const int IoBufferSize = 65536;

        private static readonly long MinValidTicks = new DateTime(2020, 1, 1).Ticks;
        private static readonly long MaxValidTicks = new DateTime(2030, 1, 1).Ticks;

        private const int DefaultLegacyInterval = 3000;

        public FrameRepository(string storagePath, OcrDatabase ocrDb = null)
        {
            _storagePath = storagePath;
            _ocrDb = ocrDb;

            if (!string.IsNullOrEmpty(_storagePath)) Directory.CreateDirectory(_storagePath);
        }

        public void SetOcrDatabase(OcrDatabase ocrDb)
        {
            _ocrDb = ocrDb;
        }

        private string GetDataPath(DateTime date) => Path.Combine(_storagePath, date.ToString("yyyy-MM-dd") + ".sch");
        private string GetCsvPath(DateTime date) => Path.Combine(_storagePath, date.ToString("yyyy-MM-dd") + ".csv");
        private string GetMkvPath(DateTime date) => Path.Combine(_storagePath, date.ToString("yyyy-MM-dd") + ".mkv");

        public List<FrameIndex> GlobalSearch(string searchText)
        {
            DebugLogger.Log($"[GlobalSearch] Requested. SearchText: '{searchText}'. OCR DB Available: {_ocrDb != null}");
            if (_ocrDb != null)
            {
                return GlobalSearchFast(searchText);
            }

            DebugLogger.Log($"[GlobalSearch] Fallback to file search. ProcessorCount: {Environment.ProcessorCount}");
            var results = new System.Collections.Concurrent.ConcurrentBag<FrameIndex>();

            if (string.IsNullOrEmpty(_storagePath)) return results.ToList();

            searchText = searchText?.ToLower() ?? "";

            if (!string.IsNullOrEmpty(_storagePath))
            {
                try
                {
                    var csvFiles = Directory.GetFiles(_storagePath, "*.csv");
                    Parallel.ForEach(csvFiles, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, csvFile =>
                    {
                        try
                        {
                            var stringCache = new Dictionary<string, string>();
                            var framesInFile = LoadFromCsv(csvFile, stringCache);
                            if (framesInFile != null)
                            {
                                foreach (var frame in framesInFile)
                                {
                                    if (string.IsNullOrEmpty(searchText) || frame.AppName.ToLower().Contains(searchText))
                                    {
                                        results.Add(frame);
                                    }
                                }
                            }
                        }
                        catch { }
                    });
                }
                catch (Exception ex) { DebugLogger.LogError("FrameRepository.GlobalSearch.Csv", ex); }
            }

            var list = results.ToList();
            list.Sort((a, b) => a.TimestampTicks.CompareTo(b.TimestampTicks));

            if (list.Count > 1)
            {
                var uniqueList = new List<FrameIndex>(list.Count);
                uniqueList.Add(list[0]);
                for (int i = 1; i < list.Count; i++)
                {
                    if (list[i].TimestampTicks != list[i - 1].TimestampTicks)
                    {
                        uniqueList.Add(list[i]);
                    }
                }
                return uniqueList;
            }

            return list;
        }

        public Dictionary<DateTime, TimeSpan> GetActivityDurations(DateTime startDate, DateTime endDate)
        {
            var activityData = new Dictionary<DateTime, TimeSpan>();
            var maxInterval = TimeSpan.FromSeconds(90);

            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                if (date < DateTime.Today && _ocrDb != null)
                {
                    var cachedSeconds = _ocrDb.GetDailyActivityStats(date.ToString("yyyy-MM-dd"));
                    if (cachedSeconds.HasValue)
                    {
                        activityData[date] = TimeSpan.FromSeconds(cachedSeconds.Value);
                        continue;
                    }
                }

                var frames = LoadFramesForDate(date);
                if (frames == null || frames.Count < 2)
                {
                    activityData[date] = TimeSpan.Zero;
                    if (date < DateTime.Today && _ocrDb != null)
                    {
                        _ocrDb.SaveDailyActivityStats(date.ToString("yyyy-MM-dd"), 0);
                    }
                    continue;
                }

                TimeSpan totalActiveTime = TimeSpan.Zero;
                for (int i = 0; i < frames.Count - 1; i++)
                {
                    var time1 = frames[i].GetTime();
                    var time2 = frames[i + 1].GetTime();
                    var interval = time2 - time1;

                    if (interval < maxInterval && interval > TimeSpan.Zero)
                    {
                        totalActiveTime += interval;
                    }
                }
                activityData[date] = totalActiveTime;

                if (date < DateTime.Today && _ocrDb != null)
                {
                    _ocrDb.SaveDailyActivityStats(date.ToString("yyyy-MM-dd"), totalActiveTime.TotalSeconds);
                }
            }
            return activityData;
        }

        public Dictionary<int, TimeSpan> GetHourlyActivity(DateTime startDate, DateTime endDate)
        {
            var hourlyData = new Dictionary<int, TimeSpan>();
            for (int i = 0; i < 24; i++) hourlyData[i] = TimeSpan.Zero;

            if (startDate == DateTime.MinValue)
            {
                var frames = GlobalSearch("");
                if (frames != null && frames.Count > 0)
                {
                    long[] durationsAllTime = FrameHelper.CalculateFrameDurations(frames);
                    for (int i = 0; i < frames.Count; i++)
                    {
                        long durationMs = durationsAllTime[i];
                        if (durationMs > 0)
                        {
                            var time = frames[i].GetTime();
                            int hour = time.Hour;
                            if (hour >= 0 && hour < 24)
                            {
                                hourlyData[hour] += TimeSpan.FromMilliseconds(durationMs);
                            }
                        }
                    }
                }
                return hourlyData;
            }

            if (startDate < new DateTime(2020, 1, 1)) startDate = new DateTime(2020, 1, 1);
            if (endDate > DateTime.Today) endDate = DateTime.Today;

            var allFramesBag = new System.Collections.Concurrent.ConcurrentBag<MiniFrame>();
            var dates = new List<DateTime>();
            for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
            {
                dates.Add(date);
            }

            Parallel.ForEach(dates, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, date =>
            {
                var frames = LoadMiniFramesForDateFast(date);
                if (frames != null)
                {
                    foreach (var f in frames) allFramesBag.Add(f);
                }
            });

            var allFrames = allFramesBag.ToList();

            if (allFrames.Count == 0) return hourlyData;

            allFrames.Sort((a, b) => a.TimestampTicks.CompareTo(b.TimestampTicks));

            if (allFrames.Count > 1)
            {
                var uniqueList = new List<MiniFrame>(allFrames.Count);
                uniqueList.Add(allFrames[0]);
                for (int i = 1; i < allFrames.Count; i++)
                {
                    if (allFrames[i].TimestampTicks != allFrames[i - 1].TimestampTicks)
                    {
                        uniqueList.Add(allFrames[i]);
                    }
                }
                allFrames = uniqueList;
            }

            for (int i = 0; i < allFrames.Count; i++)
            {
                long durationMs = allFrames[i].IntervalMs;
                if (durationMs <= 0) durationMs = 3000;

                if (durationMs > 0)
                {
                    var time = allFrames[i].GetTime();
                    int hour = time.Hour;
                    if (hour >= 0 && hour < 24)
                    {
                        hourlyData[hour] += TimeSpan.FromMilliseconds(durationMs);
                    }
                }
            }

            return hourlyData;
        }

        public void InvalidateActivityCache(DateTime date)
        {
            if (_ocrDb != null)
            {
                _ocrDb.ClearDailyActivityStats(date.ToString("yyyy-MM-dd"));
            }
        }

        public List<FrameIndex> LoadFramesForDate(DateTime date)
        {
            if (string.IsNullOrEmpty(_storagePath)) return new List<FrameIndex>();

            string csvPath = GetCsvPath(date);
            if (File.Exists(csvPath)) return LoadFromCsv(csvPath, null);

            if (_ocrDb != null)
            {
                var dbFrames = _ocrDb.GetFramesMetaForDay(date.ToString("yyyy-MM-dd"));
                if (dbFrames != null && dbFrames.Count > 0) return dbFrames;
            }

            string schPath = GetDataPath(date);
            if (File.Exists(schPath))
            {
                 return LoadFromSch(schPath);
            }

            return new List<FrameIndex>();
        }

        private List<FrameIndex> LoadFromSch(string schPath)
        {
            var frames = new List<FrameIndex>();
            try
            {
                using (var stream = new FileStream(schPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, IoBufferSize))
                using (var reader = new BinaryReader(stream))
                {
                    while (stream.Position < stream.Length)
                    {
                        try 
                        {
                            long ticks = reader.ReadInt64();
                            int nameLen = reader.ReadInt32();
                            if (nameLen < 0 || nameLen > 10000) break; 

                            byte[] nameBytes = reader.ReadBytes(nameLen);
                            string appName = Encoding.UTF8.GetString(nameBytes);
                            
                            int dataLen = reader.ReadInt32();
                            if (dataLen < 0 || dataLen > 100_000_000) break; 

                            long dataOffset = stream.Position;
                            stream.Seek(dataLen, SeekOrigin.Current);

                            var frame = new FrameIndex
                            {
                                TimestampTicks = ticks,
                                AppName = appName,
                                DataOffset = dataOffset,
                                DataLength = dataLen,
                                IntervalMs = 0 
                            };
                            frames.Add(frame);
                        }
                        catch (EndOfStreamException) { break; }
                    }
                }
                EstimateIntervalsForOldFrames(frames);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("FrameRepository.LoadFromSch", ex);
            }
            return frames;
        }

        private List<FrameIndex> LoadFromCsv(string csvPath, Dictionary<string, string> stringCache)
        {
            var frames = new List<FrameIndex>();
            try
            {
                using (var reader = new StreamReader(csvPath, Encoding.UTF8))
                {
                    string header = reader.ReadLine();   
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        var parts = line.Split(';');
                        if (parts.Length < 3) continue;

                        if (long.TryParse(parts[0], out long frameNumber) &&
                            long.TryParse(parts[1], out long ticks))
                        {
                            string appName = parts[2];
                            if (parts.Length >= 5 && !string.IsNullOrEmpty(parts[3]))
                            {
                                appName = $"{appName}|{parts[3]}";
                            }

                            if (stringCache != null)
                            {
                                if (!stringCache.TryGetValue(appName, out var cached))
                                {
                                    stringCache[appName] = appName;
                                    cached = appName;
                                }
                                appName = cached;
                            }

                            var frame = new FrameIndex
                            {
                                TimestampTicks = ticks,
                                AppName = appName,
                                DataOffset = frameNumber,
                                DataLength = -1,
                                IntervalMs = 0
                            };
                            frames.Add(frame);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("FrameRepository.LoadFromCsv", ex);
                return new List<FrameIndex>();
            }
            return frames;
        }

        private void EstimateIntervalsForOldFrames(List<FrameIndex> frames)
        {
            if (frames == null || frames.Count == 0) return;

            if (frames.Count < 2)
            {
                var f = frames[0];
                f.IntervalMs = DefaultLegacyInterval;
                frames[0] = f;
                return;
            }

            const int MaxValidIntervalMs = 30000;

            for (int i = 0; i < frames.Count - 1; i++)
            {
                var f = frames[i];
                var nextF = frames[i + 1];

                long diffMs = (nextF.TimestampTicks - f.TimestampTicks) / 10000;

                if (diffMs > 0 && diffMs <= MaxValidIntervalMs)
                {
                    f.IntervalMs = (int)diffMs;
                }
                else
                {
                    f.IntervalMs = DefaultLegacyInterval;
                }
                frames[i] = f;
            }

            var lastF = frames[frames.Count - 1];
            lastF.IntervalMs = DefaultLegacyInterval;
            frames[frames.Count - 1] = lastF;
        }

        public async Task<FrameIndex?> SaveFrame(byte[] jpegBytes, string appName, int intervalMs)
        {
            if (string.IsNullOrEmpty(_storagePath)) return null;
            DateTime now = DateTime.Now; 
            string schPath = GetDataPath(now); 
            
            try
            {
                long dataOffset;
                using (var stream = new FileStream(schPath, FileMode.Append, FileAccess.Write, FileShare.Read, IoBufferSize))
                using (var writer = new BinaryWriter(stream))
                {
                    byte[] appNameBytes = Encoding.UTF8.GetBytes(appName);
                    writer.Write(now.Ticks);
                    writer.Write(appNameBytes.Length);
                    writer.Write(appNameBytes);
                    writer.Write(jpegBytes.Length);
                    dataOffset = stream.Position;
                    writer.Write(jpegBytes);
                }

                var frame = new FrameIndex { TimestampTicks = now.Ticks, DataLength = jpegBytes.Length, DataOffset = dataOffset, AppName = appName, IntervalMs = intervalMs };
                
                if (_ocrDb != null)
                {
                    _ocrDb.InsertFrameMeta(now.Ticks, now.ToString("yyyy-MM-dd"), appName, dataOffset, jpegBytes.Length);
                }
                
                return frame;
            }
            catch (Exception ex) { DebugLogger.LogError("FrameRepository.SaveFrame", ex); return null; }
        }

        public byte[] GetFrameData(FrameIndex frame)
        {
            if (frame.DataLength == -1) return null;   
            if (string.IsNullOrEmpty(_storagePath)) return null;

            string containerPath = GetDataPath(frame.GetTime());

            if (!File.Exists(containerPath)) return null;
            try
            {
                using (var stream = new FileStream(containerPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite, IoBufferSize, FileOptions.RandomAccess))
                {
                    stream.Position = frame.DataOffset; byte[] imgBytes = new byte[frame.DataLength];
                    int bytesRead = stream.Read(imgBytes, 0, frame.DataLength);
                    if (bytesRead < frame.DataLength) return null;
                    return imgBytes;
                }
            }
            catch (Exception ex) { DebugLogger.LogError("FrameRepository.GetFrameData", ex); return null; }
        }

        public long GetTotalFileSizeForDate(DateTime date)
        {
            if (string.IsNullOrEmpty(_storagePath)) return 0;

            string mkvPath = GetMkvPath(date);
            if (File.Exists(mkvPath))
            {
                try { return new FileInfo(mkvPath).Length; } catch { }
            }

            string schPath = GetDataPath(date);
            if (File.Exists(schPath))
            {
                try { return new FileInfo(schPath).Length; } catch { }
            }

            return 0;
        }

        public string GetVideoPathForDate(DateTime date)
        {
            string mkvPath = GetMkvPath(date);
            return File.Exists(mkvPath) ? mkvPath : null;
        }

        public List<DateTime> GetDaysWithData()
        {
            var days = new HashSet<DateTime>();
            
            if (!string.IsNullOrEmpty(_storagePath) && Directory.Exists(_storagePath))
            {
                try
                {
                    var files = Directory.GetFiles(_storagePath);
                    foreach (var file in files)
                    {
                        var fileName = Path.GetFileNameWithoutExtension(file);
                        if (DateTime.TryParseExact(fileName, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.None, out DateTime date))
                        {
                            days.Add(date.Date);
                        }
                    }
                }
                catch (Exception ex) { DebugLogger.LogError("FrameRepository.GetDaysWithData.Storage", ex); }
            }

            var list = days.ToList();
            list.Sort();
            return list;
        }

        private bool IsValidAppName(string name)
        {
            if (string.IsNullOrWhiteSpace(name)) return false;
            foreach (char c in name)
            {
                if (char.IsControl(c) || c == 0xFFFD || c == 0) return false;
            }
            if (name.Trim().Length == 0) return false;
            return true;
        }

        #region Fast DB-backed Frame Access

        public async Task SyncDayToDbAsync(DateTime date)
        {
            if (_ocrDb == null) return;

            string dayStr = date.ToString("yyyy-MM-dd");
            
            if (_ocrDb.IsDayIndexed(dayStr)) return;

            var frames = await Task.Run(() => LoadFramesForDate(date));
            
            if (frames == null || frames.Count == 0)
            {
                _ocrDb.MarkDayIndexed(dayStr, 0);
                return;
            }

            var metaList = frames.Select(f => (f.TimestampTicks, dayStr, f.AppName, f.DataOffset, f.DataLength)).ToList();
            await Task.Run(() => _ocrDb.BulkInsertFramesMeta(metaList));
            
            _ocrDb.MarkDayIndexed(dayStr, frames.Count);
        }

        public async Task SyncAllDaysToDbAsync(IProgress<(int current, int total, string day)> progress = null)
        {
            if (_ocrDb == null) return;

            var days = GetDaysWithData();
            int total = days.Count;
            int current = 0;

            foreach (var date in days)
            {
                string dayStr = date.ToString("yyyy-MM-dd");
                
                if (!_ocrDb.IsDayIndexed(dayStr))
                {
                    await SyncDayToDbAsync(date);
                }

                current++;
                progress?.Report((current, total, dayStr));
            }
        }

        public List<FrameIndex> LoadFramesForDateFast(DateTime date, string appFilter = null)
        {
            if (_ocrDb == null) return LoadFramesForDate(date);

            string dayStr = date.ToString("yyyy-MM-dd");
            
            if (_ocrDb.IsDayIndexed(dayStr))
            {
                return _ocrDb.GetFramesMetaForDay(dayStr, appFilter);
            }
            
            var frames = LoadFramesForDate(date);
            
            Task.Run(() => SyncDayToDbAsync(date));
            
            if (!string.IsNullOrEmpty(appFilter) && appFilter != "All Applications")
            {
                frames = frames.Where(f => f.AppName == appFilter).ToList();
            }
            
            return frames;
        }

        public List<MiniFrame> LoadMiniFramesForDateFast(DateTime date)
        {
            if (_ocrDb == null) 
            {
                var fullFrames = LoadFramesForDate(date);
                return new List<MiniFrame>(); 
            }

            string dayStr = date.ToString("yyyy-MM-dd");
            
            if (_ocrDb.IsDayIndexed(dayStr))
            {
                var frames = _ocrDb.GetMiniFramesForDay(dayStr);
                EstimateIntervalsForMiniFrames(frames);
                return frames;
            }
            
            var diskFrames = LoadFramesForDate(date);
            Task.Run(() => SyncDayToDbAsync(date));
            
            var appMap = _ocrDb.GetAppMap();
            var nameToId = appMap.ToDictionary(x => x.Value, x => x.Key);
            
            var miniFrames = new List<MiniFrame>(diskFrames.Count);
            foreach (var f in diskFrames)
            {
                int appId = -1;
                if (f.AppName != null && nameToId.TryGetValue(f.AppName, out int id))
                {
                    appId = id;
                }
                miniFrames.Add(new MiniFrame 
                { 
                    TimestampTicks = f.TimestampTicks, 
                    AppId = appId, 
                    IntervalMs = f.IntervalMs 
                });
            }
            return miniFrames;
        }

        public Dictionary<int, string> GetAppMap()
        {
            return _ocrDb?.GetAppMap() ?? new Dictionary<int, string>();
        }

        public FrameIndex GetFrameIndex(long timestamp)
        {
            if (_ocrDb != null)
            {
                var f = _ocrDb.GetFrameIndex(timestamp);
                if (f.HasValue) return f.Value;
            }

            var date = new DateTime(timestamp).Date;
            var frames = LoadFramesForDate(date);
            var match = frames.FirstOrDefault(x => x.TimestampTicks == timestamp);
            if (match.TimestampTicks == timestamp) return match;

            return default(FrameIndex);
        }

        private void EstimateIntervalsForMiniFrames(List<MiniFrame> frames)
        {
            if (frames == null || frames.Count == 0) return;

            if (frames.Count < 2)
            {
                var f = frames[0];
                f.IntervalMs = DefaultLegacyInterval;
                frames[0] = f;
                return;
            }

            const int MaxValidIntervalMs = 30000;

            for (int i = 0; i < frames.Count - 1; i++)
            {
                var f = frames[i];
                var nextF = frames[i + 1];

                long diffMs = (nextF.TimestampTicks - f.TimestampTicks) / 10000;

                if (diffMs > 0 && diffMs <= MaxValidIntervalMs)
                {
                    f.IntervalMs = (int)diffMs;
                }
                else
                {
                    f.IntervalMs = DefaultLegacyInterval;
                }
                frames[i] = f;
            }

            var lastF = frames[frames.Count - 1];
            lastF.IntervalMs = DefaultLegacyInterval;
            frames[frames.Count - 1] = lastF;
        }

        public List<string> GetAppsForDateFast(DateTime date)
        {
            if (_ocrDb == null) return GetAppsFromFrames(LoadFramesForDate(date));

            string dayStr = date.ToString("yyyy-MM-dd");
            
            if (_ocrDb.IsDayIndexed(dayStr))
            {
                return _ocrDb.GetAppsForDay(dayStr);
            }

            return GetAppsFromFrames(LoadFramesForDate(date));
        }

        private List<string> GetAppsFromFrames(List<FrameIndex> frames)
        {
            return frames
                .Where(f => !string.IsNullOrEmpty(f.AppName))
                .Select(f => f.AppName)
                .Distinct()
                .OrderBy(a => a)
                .ToList();
        }

        public int GetFrameCountForDateFast(DateTime date)
        {
            if (_ocrDb == null) return LoadFramesForDate(date).Count;

            string dayStr = date.ToString("yyyy-MM-dd");
            
            if (_ocrDb.IsDayIndexed(dayStr))
            {
                return _ocrDb.GetFrameCountForDay(dayStr);
            }

            return LoadFramesForDate(date).Count;
        }

        public List<FrameIndex> GlobalSearchFast(string searchText)
        {
            DebugLogger.Log($"[GlobalSearchFast] Started. SearchText: '{searchText}'");
            if (_ocrDb == null) 
            {
                DebugLogger.Log("[GlobalSearchFast] _ocrDb is NULL! Redirecting to GlobalSearch.");
                return GlobalSearch(searchText);
            }

            var results = new List<FrameIndex>();
            
            var indexedDaysSet = _ocrDb.GetIndexedDaysSet();
            DebugLogger.Log($"[GlobalSearchFast] Indexed days in DB: {indexedDaysSet.Count}");

            var allDays = GetDaysWithData();
            DebugLogger.Log($"[GlobalSearchFast] Total days on disk: {allDays.Count}");
            
            var unindexedDays = new List<DateTime>();

            foreach (var date in allDays)
            {
                string dayStr = date.ToString("yyyy-MM-dd");
                if (!indexedDaysSet.Contains(dayStr))
                {
                    unindexedDays.Add(date);
                }
            }
            DebugLogger.Log($"[GlobalSearchFast] Unindexed days to scan manually: {unindexedDays.Count}");

            var swDb = System.Diagnostics.Stopwatch.StartNew();
            var dbResults = _ocrDb.SearchFramesMeta(searchText);
            swDb.Stop();
            DebugLogger.Log($"[GlobalSearchFast] DB Search returned {dbResults.Count} results in {swDb.ElapsedMilliseconds} ms.");
            results.AddRange(dbResults);

            if (unindexedDays.Count > 0)
            {
                DebugLogger.Log($"[GlobalSearchFast] Starting parallel scan for {unindexedDays.Count} unindexed days. MaxDegreeOfParallelism: {Environment.ProcessorCount}");
                var swParallel = System.Diagnostics.Stopwatch.StartNew();

                var dbTimestamps = new HashSet<long>(dbResults.Select(x => x.TimestampTicks));
                var bag = new System.Collections.Concurrent.ConcurrentBag<FrameIndex>();

                Parallel.ForEach(unindexedDays, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, date =>
                {
                    var frames = LoadFramesForDate(date);
                    if (frames != null)
                    {
                        foreach (var frame in frames)
                        {
                            if (dbTimestamps.Contains(frame.TimestampTicks)) continue;

                            if (string.IsNullOrEmpty(searchText) || 
                                (!string.IsNullOrEmpty(frame.AppName) && frame.AppName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0))
                            {
                                bag.Add(frame);
                            }
                        }
                    }
                    
                    _ = SyncDayToDbAsync(date);
                });

                swParallel.Stop();
                DebugLogger.Log($"[GlobalSearchFast] Parallel scan took {swParallel.ElapsedMilliseconds} ms. Found {bag.Count} additional items.");
                results.AddRange(bag);
            }

            results.Sort((a, b) => a.TimestampTicks.CompareTo(b.TimestampTicks));

            if (results.Count > 1)
            {
                var uniqueResults = new List<FrameIndex>(results.Count);
                uniqueResults.Add(results[0]);
                for (int i = 1; i < results.Count; i++)
                {
                    if (results[i].TimestampTicks != results[i - 1].TimestampTicks)
                    {
                        uniqueResults.Add(results[i]);
                    }
                }
                return uniqueResults;
            }

            return results;
        }

        #endregion
    }
}