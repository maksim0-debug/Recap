using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Recap
{
    public class TimelineDataManager
    {
        private readonly FrameRepository _frameRepository;
        private readonly OcrDatabase _ocrDb;
        private readonly AppSettings _settings;

        private List<MiniFrame> _allLoadedFrames = new List<MiniFrame>();
        private List<MiniFrame> _filteredFrames = new List<MiniFrame>();
        
        private readonly Dictionary<DateTime, List<MiniFrame>> _dayCache = new Dictionary<DateTime, List<MiniFrame>>();
        private readonly object _cacheLock = new object();
        private readonly object _allFramesLock = new object();
        private Dictionary<int, string> _appMap = new Dictionary<int, string>();

        public List<MiniFrame> AllLoadedFrames => _allLoadedFrames;

        public List<MiniFrame> GetAllLoadedFramesCopy()
        {
            lock (_allFramesLock)
            {
                return _allLoadedFrames != null ? new List<MiniFrame>(_allLoadedFrames) : null;
            }
        }
        public List<MiniFrame> FilteredFrames => _filteredFrames;
        public Dictionary<int, string> AppMap => _appMap;

        public TimelineDataManager(FrameRepository frameRepository, OcrDatabase ocrDb, AppSettings settings)
        {
            _frameRepository = frameRepository;
            _ocrDb = ocrDb;
            _settings = settings;
        }

        public async Task LoadFramesAsync(DateTime? startDate, DateTime? endDate, string forceGlobalSearchText)
        {
            lock (_allFramesLock)
            {
                _allLoadedFrames = null;
                _filteredFrames = null;
            }
            
            bool isGlobalMode = _settings.GlobalSearch;

            await Task.Run(() =>
            {
                _appMap = _frameRepository.GetAppMap();

                if (isGlobalMode)
                {
                    var fullFrames = _frameRepository.GlobalSearch(forceGlobalSearchText);
                    lock (_allFramesLock)
                    {
                        _allLoadedFrames = ConvertToMiniFrames(fullFrames);
                    }
                }
                else
                {
                    GC.Collect();
                    GC.WaitForPendingFinalizers();

                    var newFrames = new List<MiniFrame>();

                    DateTime start = startDate ?? DateTime.Today;
                    DateTime end = endDate ?? start;

                    for (DateTime date = start.Date; date <= end.Date; date = date.AddDays(1))
                    {
                        List<MiniFrame> frames = null;

                        lock (_cacheLock)
                        {
                            if (_dayCache.ContainsKey(date))
                            {
                                frames = _dayCache[date];
                            }
                        }

                        if (frames == null)
                        {
                            frames = _frameRepository.LoadMiniFramesForDateFast(date);
                            lock (_cacheLock)
                            {
                                if (!_dayCache.ContainsKey(date))
                                {
                                    _dayCache[date] = frames;
                                }
                                else
                                {
                                    frames = _dayCache[date];
                                }
                            }
                        }

                        if (frames != null)
                        {
                            newFrames.AddRange(frames);
                        }
                    }

                    lock (_allFramesLock)
                    {
                        _allLoadedFrames = newFrames;
                    }
                }
            });
        }

        public void ApplyFilter(List<string> appFilter, string ocrText)
        {
            List<MiniFrame> appFiltered;

            lock (_allFramesLock)
            {
                if (appFilter == null || appFilter.Count == 0)
                {
                    appFiltered = _allLoadedFrames != null ? new List<MiniFrame>(_allLoadedFrames) : new List<MiniFrame>();
                }
                else
                {
                    HashSet<int> validAppIds = new HashSet<int>();
                    foreach (var kvp in _appMap)
                    {
                        if (DoesAppMatchFilter(kvp.Value, appFilter)) 
                        {
                            validAppIds.Add(kvp.Key);
                        }
                    }
                    if (DoesAppMatchFilter("", appFilter))
                    {
                        validAppIds.Add(-1);
                    }

                    appFiltered = new List<MiniFrame>();
                    if (_allLoadedFrames != null)
                    {
                        foreach (var f in _allLoadedFrames)
                        {
                            if (validAppIds.Contains(f.AppId))
                            {
                                appFiltered.Add(f);
                            }
                        }
                    }
                }
            }

            if (!string.IsNullOrEmpty(ocrText) && _ocrDb != null)
            {
                try
                {
                    List<string> dbFilter = null;
                    if (appFilter != null && appFilter.Count > 0 && !appFilter.Any(x => x.Contains("|YouTube")))
                    {
                        dbFilter = appFilter;
                    }

                    var matchingFrames = _ocrDb.Search(ocrText, dbFilter);
                    
                    if (matchingFrames != null && matchingFrames.Count > 0)
                    {
                        var matchingTicks = new HashSet<long>(matchingFrames.Select(f => f.TimestampTicks));
                        _filteredFrames = appFiltered.Where(f => matchingTicks.Contains(f.TimestampTicks)).ToList();
                        
                        DebugLogger.Log($"OCR Search: '{ocrText}' found {_filteredFrames.Count} matches");
                    }
                    else
                    {
                        _filteredFrames = new List<MiniFrame>();
                        DebugLogger.Log($"OCR Search: '{ocrText}' - no matches found");
                    }
                }
                catch (Exception ex)
                {
                    DebugLogger.LogError("TimelineDataManager.OcrSearch", ex);
                    _filteredFrames = appFiltered;    
                }
            }
            else
            {
                _filteredFrames = appFiltered;
            }
        }

        public void ClearCache()
        {
            lock (_cacheLock)
            {
                _dayCache.Clear();
            }
        }

        public void AddFrameToCache(MiniFrame frame)
        {
             lock (_cacheLock)
            {
                if (_dayCache.ContainsKey(DateTime.Today))
                {
                    if (!_dayCache[DateTime.Today].Any(x => x.TimestampTicks == frame.TimestampTicks))
                    {
                        _dayCache[DateTime.Today].Add(frame);
                    }
                }
            }
        }

        public void AddFrameToAll(MiniFrame frame)
        {
            lock (_allFramesLock)
            {
                if (_allLoadedFrames != null && !_allLoadedFrames.Any(x => x.TimestampTicks == frame.TimestampTicks))
                {
                    _allLoadedFrames.Add(frame);
                }
            }
        }

        public bool MatchesAppFilter(MiniFrame f, List<string> filters)
        {
            if (filters == null || filters.Count == 0) return true;

            string appName = "";
            if (_appMap.TryGetValue(f.AppId, out string name)) appName = name;

            return DoesAppMatchFilter(appName, filters);
        }

        public bool DoesAppMatchFilter(string appName, List<string> filters)
        {
            if (filters == null || filters.Count == 0) return true;
            if (appName == null) appName = "";

            foreach (var filter in filters)
            {
                if (appName == filter) return true;
                if (appName.StartsWith(filter + "|")) return true;

                if (filter.Contains("|YouTube|"))
                {
                    string dbStyleKey = filter.Replace("|YouTube|", "|");
                    if (appName == dbStyleKey) return true;
                }
                if (filter.EndsWith("|YouTube"))
                {
                    string prefix = filter.Replace("|YouTube", "|youtube.com");
                    if (appName.StartsWith(prefix)) return true;
                    string prefixWww = filter.Replace("|YouTube", "|www.youtube.com");
                    if (appName.StartsWith(prefixWww)) return true;
                }

                string normalizedFilter = filter.Replace("|www.", "|");
                string normalizedAppName = appName.Replace("|www.", "|");

                if (normalizedAppName == normalizedFilter) return true;
                if (normalizedAppName.StartsWith(normalizedFilter + "|")) return true;

                if (normalizedFilter == filter)
                {
                    string filterWithWww = filter.Contains("|") ? filter.Insert(filter.IndexOf('|') + 1, "www.") : filter;
                    if (appName == filterWithWww) return true;
                    if (appName.StartsWith(filterWithWww + "|")) return true;
                }
            }

            return false;
        }

        public int GetAppId(string appName)
        {
            foreach(var kvp in _appMap) { if (kvp.Value == appName) return kvp.Key; }
            return -1;
        }

        public void UpdateAppMap()
        {
            _appMap = _frameRepository.GetAppMap();
        }

        public void RegisterApp(int appId, string appName)
        {
            _appMap[appId] = appName;
        }

        public void RestoreState(List<MiniFrame> frames)
        {
            lock (_allFramesLock)
            {
                _allLoadedFrames = frames ?? new List<MiniFrame>();
            }
            if (_appMap.Count == 0) _appMap = _frameRepository.GetAppMap();
        }

        private List<MiniFrame> ConvertToMiniFrames(List<FrameIndex> fullFrames)
        {
            var mini = new List<MiniFrame>(fullFrames.Count);
            var nameToId = _appMap.ToDictionary(x => x.Value, x => x.Key);
            
            foreach (var f in fullFrames)
            {
                int id = -1;
                if (f.AppName != null && nameToId.TryGetValue(f.AppName, out int val)) id = val;
                mini.Add(new MiniFrame { TimestampTicks = f.TimestampTicks, AppId = id, IntervalMs = f.IntervalMs });
            }
            return mini;
        }
    }
}
