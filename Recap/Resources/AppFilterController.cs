using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class FilterItem
    {
        public string RawName;
        public string DisplayName;
        public long DurationMs;
        public int FrameCount;   
        public int Level;
        public bool HasChildren;
        public bool IsExpanded;
        public bool IsVideo;
        public string VideoId;

        public override string ToString() => DisplayName;
    }

    public class AppFilterController : IDisposable
    {
        public event Action<string> FilterChanged;

        private readonly DarkListBox _lstAppFilter;
        private readonly TextBox _txtAppSearch;
        private readonly IconManager _iconManager;

        private List<MiniFrame> _currentFrames = new List<MiniFrame>();
        private Dictionary<int, string> _appMap = new Dictionary<int, string>();

        private HashSet<string> _expandedApps = new HashSet<string>();
        private HashSet<string> _expandedGroups = new HashSet<string>();

        private List<FilterItem> _viewItems = new List<FilterItem>();
        private bool _isLoading = false;

        private readonly HashSet<string> _hierarchicalApps = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "chrome.exe", "google chrome.exe",
            "msedge.exe", "edge.exe",
            "opera.exe", "opera gx.exe",
            "brave.exe",
            "yandex.exe", "browser.exe",
            "firefox.exe",
            "telegram.exe", "ayugram.exe", "kotatogram.exe", "64gram.exe"
        };

        private readonly HashSet<string> _messengers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "telegram.exe", "ayugram.exe", "kotatogram.exe", "64gram.exe"
        };

        private Dictionary<string, NodeData> _cachedStats = new Dictionary<string, NodeData>();
        private long _cachedGlobalTotal = 0;
        private int _cachedGlobalFrameCount = 0;

        public AppFilterController(DarkListBox lstAppFilter, TextBox txtAppSearch, IconManager iconManager)
        {
            _lstAppFilter = lstAppFilter;
            _txtAppSearch = txtAppSearch;
            _iconManager = iconManager;

            _lstAppFilter.IconManager = _iconManager;
            _iconManager.IconLoaded += OnIconLoaded;

            _lstAppFilter.MouseClick += LstAppFilter_MouseClick;
            _lstAppFilter.MouseDoubleClick += LstAppFilter_MouseDoubleClick;
            _lstAppFilter.SelectedIndexChanged += LstAppFilter_SelectedIndexChanged;
            _txtAppSearch.TextChanged += TxtAppSearch_TextChanged;
        }

        public async System.Threading.Tasks.Task SetDataAsync(List<MiniFrame> frames, Dictionary<int, string> appMap)
        {
            _isLoading = true;
            _currentFrames = frames;
            _appMap = appMap;

            DebugLogger.Log($"[AppFilter] SetDataAsync started for {frames.Count} frames.");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            await System.Threading.Tasks.Task.Run(() => RebuildStats());

            sw.Stop();
            DebugLogger.Log($"[AppFilter] RebuildStats took {sw.ElapsedMilliseconds} ms.");

            sw.Restart();
            RefreshAppFilterList();
            sw.Stop();
            DebugLogger.Log($"[AppFilter] RefreshAppFilterList took {sw.ElapsedMilliseconds} ms.");

            if (_lstAppFilter.Items.Count > 0)
            {
                _lstAppFilter.SelectedIndex = 0;
            }
            _isLoading = false;
        }

        public void AddFrame(MiniFrame newFrame)
        {
            if (!_currentFrames.Any(f => f.TimestampTicks == newFrame.TimestampTicks))
            {
                _currentFrames.Add(newFrame);
            }

            UpdateStatsWithFrame(newFrame);
            RefreshAppFilterList();
        }

        public void RegisterApp(int id, string name)
        {
            if (!_appMap.ContainsKey(id))
            {
                _appMap[id] = name;
            }
        }

        private class NodeData
        {
            public long TotalMs;
            public int FrameCount;   
            public Dictionary<string, NodeData> Children = new Dictionary<string, NodeData>();
            public bool IsVideoNode;
            public string VideoId;
        }

        private class ParsedAppInfo
        {
            public string EffectiveExe;
            public string GroupKey;
            public string DetailKey;
            public bool IsVideo;
            public string VideoId;
        }

        private void RebuildStats()
        {
            var stats = new Dictionary<string, NodeData>();
            long globalTotal = 0;
            int globalFrameCount = 0;

            var frames = _currentFrames;
            var appMap = _appMap;

            if (frames == null || frames.Count == 0)
            {
                _cachedStats = stats;
                _cachedGlobalTotal = 0;
                _cachedGlobalFrameCount = 0;
                return;
            }

            int count = frames.Count;

            var durations = new long[count];
            var parsedInfos = new ParsedAppInfo[count];

            var rangePartitioner = System.Collections.Concurrent.Partitioner.Create(0, count);

            Parallel.ForEach(rangePartitioner, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, range =>
            {
                var localAppCache = new Dictionary<string, ParsedAppInfo>(100);
                long localLastInterval = 3000;

                for (int i = range.Item1; i < range.Item2; i++)
                {
                    var f = frames[i];

                    long duration = 0;
                    if (f.IntervalMs > 0)
                    {
                        duration = f.IntervalMs;
                        localLastInterval = duration;
                    }
                    else
                    {
                        if (i < count - 1)
                        {
                            var nextF = frames[i + 1];
                            long diffMs = (nextF.TimestampTicks - f.TimestampTicks) / 10000;
                            if (diffMs > 0 && diffMs <= 90000)
                            {
                                duration = diffMs;
                                localLastInterval = duration;
                            }
                            else duration = localLastInterval;
                        }
                        else duration = localLastInterval;
                    }
                    if (duration <= 0) duration = 3000;
                    durations[i] = duration;
                    string currentAppName = "";
                    if (appMap.TryGetValue(f.AppId, out string name)) currentAppName = name;

                    if (currentAppName.Contains("|YouTube") || currentAppName.Contains("youtube.com"))
                    {
                        for (int j = 1; j <= 5; j++)
                        {
                            if (i + j >= count) break;
                            var nextF = frames[i + j];
                            long gapMs = (nextF.TimestampTicks - f.TimestampTicks) / 10000;
                            if (gapMs > 20000) break;

                            string nextAppName = "";
                            if (appMap.TryGetValue(nextF.AppId, out string nName)) nextAppName = nName;

                            if (nextAppName.Contains("|YouTube|") &&
                               !nextAppName.Contains("|Home") &&
                               !nextAppName.EndsWith("|YouTube"))
                            {
                                currentAppName = nextAppName;
                                break;
                            }
                        }
                    }

                    if (!localAppCache.TryGetValue(currentAppName, out var info))
                    {
                        info = ParseAppInfo(currentAppName);
                        localAppCache[currentAppName] = info;
                    }
                    parsedInfos[i] = info;
                }
            });

            for (int i = 0; i < count; i++)
            {
                var info = parsedInfos[i];
                long duration = durations[i];

                globalTotal += duration;
                globalFrameCount++;

                if (!stats.TryGetValue(info.EffectiveExe, out var exeNode))
                {
                    exeNode = new NodeData();
                    stats[info.EffectiveExe] = exeNode;
                }
                exeNode.TotalMs += duration;
                exeNode.FrameCount++;

                if (info.GroupKey != null)
                {
                    if (!exeNode.Children.TryGetValue(info.GroupKey, out var groupNode))
                    {
                        groupNode = new NodeData();
                        exeNode.Children[info.GroupKey] = groupNode;
                    }
                    groupNode.TotalMs += duration;
                    groupNode.FrameCount++;

                    if (info.DetailKey != null)
                    {
                        if (!groupNode.Children.TryGetValue(info.DetailKey, out var detailNode))
                        {
                            detailNode = new NodeData();
                            groupNode.Children[info.DetailKey] = detailNode;
                        }
                        detailNode.TotalMs += duration;
                        detailNode.FrameCount++;

                        if (info.IsVideo)
                        {
                            detailNode.IsVideoNode = true;
                            detailNode.VideoId = info.VideoId;
                        }
                    }
                }
            }

            _cachedStats = stats;
            _cachedGlobalTotal = globalTotal;
            _cachedGlobalFrameCount = globalFrameCount;
        }

        private void UpdateStatsWithFrame(MiniFrame f)
        {
            long duration = f.IntervalMs > 0 ? f.IntervalMs : 1000;   
            _cachedGlobalTotal += duration;
            _cachedGlobalFrameCount++;
            ProcessFrameForStats(f, duration, _cachedStats, _currentFrames.Count - 1, null);
        }

        private void ProcessFrameForStats(MiniFrame f, long duration, Dictionary<string, NodeData> stats, int index, Dictionary<string, ParsedAppInfo> cache)
        {
            string currentAppName = "";
            if (_appMap.TryGetValue(f.AppId, out string name)) currentAppName = name;

            if (currentAppName.Contains("|YouTube") || currentAppName.Contains("youtube.com"))
            {
                for (int j = 1; j <= 5; j++)
                {
                    if (index + j >= _currentFrames.Count) break;
                    var nextF = _currentFrames[index + j];

                    long gapMs = (nextF.TimestampTicks - f.TimestampTicks) / 10000;
                    if (gapMs > 20000) break;

                    string nextAppName = "";
                    if (_appMap.TryGetValue(nextF.AppId, out string nName)) nextAppName = nName;

                    if (nextAppName.Contains("|YouTube|") &&
                       !nextAppName.Contains("|Home") &&
                       !nextAppName.EndsWith("|YouTube"))
                    {
                        currentAppName = nextAppName;
                        break;
                    }
                }
            }

            ParsedAppInfo info;
            if (cache != null && cache.TryGetValue(currentAppName, out info))
            {
            }
            else
            {
                info = ParseAppInfo(currentAppName);
                if (cache != null) cache[currentAppName] = info;
            }

            if (!stats.ContainsKey(info.EffectiveExe)) stats[info.EffectiveExe] = new NodeData();
            var exeNode = stats[info.EffectiveExe];
            exeNode.TotalMs += duration;
            exeNode.FrameCount++;

            if (info.GroupKey != null)
            {
                if (!exeNode.Children.ContainsKey(info.GroupKey)) exeNode.Children[info.GroupKey] = new NodeData();
                var groupNode = exeNode.Children[info.GroupKey];
                groupNode.TotalMs += duration;
                groupNode.FrameCount++;

                if (info.DetailKey != null)
                {
                    if (!groupNode.Children.ContainsKey(info.DetailKey))
                        groupNode.Children[info.DetailKey] = new NodeData();

                    var detailNode = groupNode.Children[info.DetailKey];
                    detailNode.TotalMs += duration;
                    detailNode.FrameCount++;

                    if (info.IsVideo)
                    {
                        detailNode.IsVideoNode = true;
                        detailNode.VideoId = info.VideoId;
                    }
                }
            }
        }

        private ParsedAppInfo ParseAppInfo(string currentAppName)
        {
            var info = new ParsedAppInfo();
            var parts = FrameHelper.ParseAppName(currentAppName);
            info.EffectiveExe = parts.ExeName;
            string rest = parts.WebDomain;

            if (!_hierarchicalApps.Contains(info.EffectiveExe))
            {
                info.EffectiveExe = currentAppName;
                rest = null;
            }

            if (!string.IsNullOrEmpty(rest))
            {
                info.GroupKey = rest;

                int pipeIndex = rest.IndexOf('|');

                bool isYouTube = rest.StartsWith("youtube.com", StringComparison.OrdinalIgnoreCase) ||
                                 rest.StartsWith("www.youtube.com", StringComparison.OrdinalIgnoreCase) ||
                                 rest.StartsWith("YouTube", StringComparison.OrdinalIgnoreCase);

                if (!_messengers.Contains(info.EffectiveExe))
                {
                    if (pipeIndex > 0)
                    {
                        info.GroupKey = rest.Substring(0, pipeIndex);
                        info.DetailKey = rest.Substring(pipeIndex + 1);
                    }
                    else if (isYouTube)
                    {
                        info.GroupKey = "YouTube";
                        info.DetailKey = "Home";         
                    }
                }

                if (info.GroupKey.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                    info.GroupKey = info.GroupKey.Substring(4);

                if (info.GroupKey.Equals("YouTube", StringComparison.OrdinalIgnoreCase))
                {
                    if (string.IsNullOrEmpty(info.DetailKey) ||
                        info.DetailKey.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ||
                        info.DetailKey.Contains("youtube.com") ||
                        info.DetailKey.StartsWith("YouTube [v=", StringComparison.OrdinalIgnoreCase))
                    {
                        info.DetailKey = "Home";
                    }
                }
                if (isYouTube && !string.IsNullOrEmpty(info.DetailKey) && !info.DetailKey.Equals("Home", StringComparison.OrdinalIgnoreCase))
                {
                    info.IsVideo = true;
                    info.VideoId = info.DetailKey;
                }
            }
            return info;
        }

        private void RefreshAppFilterList()
        {
            if (_lstAppFilter == null || _lstAppFilter.IsDisposed) return;

            string savedSelection = null;
            if (_lstAppFilter.SelectedIndex >= 0 && _lstAppFilter.SelectedIndex < _viewItems.Count)
                savedSelection = _viewItems[_lstAppFilter.SelectedIndex].RawName;

            int savedTopIndex = _lstAppFilter.TopIndex;

            _lstAppFilter.BeginUpdate();
            _lstAppFilter.Items.Clear();
            _viewItems.Clear();

            var stats = _cachedStats;
            long globalTotal = _cachedGlobalTotal;
            int globalFrameCount = _cachedGlobalFrameCount;

            string searchText = _txtAppSearch.Text.Trim().ToLower();
            bool isSearch = !string.IsNullOrWhiteSpace(searchText) && searchText != Localization.Get("searchApps").ToLower();

            var allItem = new FilterItem
            {
                RawName = Localization.Get("allApps"),
                DisplayName = Localization.Get("allApps"),
                DurationMs = globalTotal,
                FrameCount = globalFrameCount,
                Level = 0,
                HasChildren = false
            };
            _viewItems.Add(allItem);
            _lstAppFilter.Items.Add(allItem);

            bool sortByFrames = _lstAppFilter.ShowFrameCount;

            var sortedApps = sortByFrames
                ? stats.OrderByDescending(kv => kv.Value.FrameCount)
                : stats.OrderByDescending(kv => kv.Value.TotalMs);

            foreach (var appKvp in sortedApps)
            {
                string exeName = appKvp.Key;
                var appNode = appKvp.Value;
                bool hasChildren = appNode.Children.Count > 0;

                bool appMatches = exeName.ToLower().Contains(searchText);
                bool anyChildMatches = appNode.Children.Any(c =>
                    c.Key.ToLower().Contains(searchText) ||
                    c.Value.Children.Any(gc => gc.Key.ToLower().Contains(searchText)));

                if (isSearch && !appMatches && !anyChildMatches) continue;
                if (isSearch && anyChildMatches) _expandedApps.Add(exeName);

                bool isAppExpanded = _expandedApps.Contains(exeName) || isSearch;
                string displayName = exeName == "Legacy" ? Localization.Get("legacyApp") : exeName;

                _viewItems.Add(new FilterItem
                {
                    RawName = exeName,
                    DisplayName = displayName,
                    DurationMs = appNode.TotalMs,
                    FrameCount = appNode.FrameCount,
                    Level = 0,
                    HasChildren = hasChildren,
                    IsExpanded = isAppExpanded
                });
                _lstAppFilter.Items.Add(_viewItems.Last());

                if (hasChildren && isAppExpanded)
                {
                    var sortedGroups = sortByFrames
                        ? appNode.Children.OrderByDescending(x => x.Value.FrameCount)
                        : appNode.Children.OrderByDescending(x => x.Value.TotalMs);

                    foreach (var groupKvp in sortedGroups)
                    {
                        string groupName = groupKvp.Key;
                        var groupNode = groupKvp.Value;
                        bool hasGrandChildren = groupNode.Children.Count > 0;
                        string fullGroupKey = $"{exeName}|{groupName}";

                        bool groupMatches = groupName.ToLower().Contains(searchText);
                        bool anyGrandChildMatches = groupNode.Children.Keys.Any(k => k.ToLower().Contains(searchText));

                        if (isSearch && !appMatches && !groupMatches && !anyGrandChildMatches) continue;
                        if (isSearch && anyGrandChildMatches) _expandedGroups.Add(fullGroupKey);

                        bool isGroupExpanded = _expandedGroups.Contains(fullGroupKey) || isSearch;

                        _viewItems.Add(new FilterItem
                        {
                            RawName = fullGroupKey,
                            DisplayName = groupName,
                            DurationMs = groupNode.TotalMs,
                            FrameCount = groupNode.FrameCount,
                            Level = 1,
                            HasChildren = hasGrandChildren,
                            IsExpanded = isGroupExpanded
                        });
                        _lstAppFilter.Items.Add(_viewItems.Last());

                        if (hasGrandChildren && isGroupExpanded)
                        {
                            var sortedDetails = sortByFrames
                                ? groupNode.Children.OrderByDescending(x => x.Value.FrameCount)
                                : groupNode.Children.OrderByDescending(x => x.Value.TotalMs);

                            foreach (var detailKvp in sortedDetails)
                            {
                                string detailName = detailKvp.Key;
                                if (isSearch && !detailName.ToLower().Contains(searchText) && !groupMatches && !appMatches) continue;

                                string fullDetailKey = $"{exeName}|{groupName}|{detailName}";

                                _viewItems.Add(new FilterItem
                                {
                                    RawName = fullDetailKey,
                                    DisplayName = detailName,
                                    DurationMs = detailKvp.Value.TotalMs,
                                    FrameCount = detailKvp.Value.FrameCount,
                                    Level = 2,
                                    HasChildren = false,
                                    IsVideo = detailKvp.Value.IsVideoNode,
                                    VideoId = detailKvp.Value.VideoId
                                });
                                _lstAppFilter.Items.Add(_viewItems.Last());
                            }
                        }
                    }
                }
            }

            RestoreSelection(savedSelection, savedTopIndex);

            _lstAppFilter.EndUpdate();
            _lstAppFilter.Invalidate();
        }

        private void RestoreSelection(string savedKey, int topIndex)
        {
            if (savedKey != null)
            {
                int newIndex = _viewItems.FindIndex(x => x.RawName == savedKey);
                if (newIndex >= 0) _lstAppFilter.SelectedIndex = newIndex;
            }

            if (topIndex >= 0 && _lstAppFilter.Items.Count > 0)
            {
                int safeTopIndex = Math.Min(topIndex, _lstAppFilter.Items.Count - 1);
                _lstAppFilter.TopIndex = safeTopIndex;
            }
        }

        private void LstAppFilter_MouseClick(object sender, MouseEventArgs e)
        {
            int index = _lstAppFilter.IndexFromPoint(e.Location);
            if (index < 0 || index >= _viewItems.Count) return;

            var item = _viewItems[index];

            int clickArea = 20 + (item.Level * 10);

            if (item.HasChildren && e.X < clickArea)
            {
                if (item.Level == 0)
                {
                    if (_expandedApps.Contains(item.RawName))
                        _expandedApps.Remove(item.RawName);
                    else
                        _expandedApps.Add(item.RawName);
                }
                else if (item.Level == 1)
                {
                    if (_expandedGroups.Contains(item.RawName))
                        _expandedGroups.Remove(item.RawName);
                    else
                        _expandedGroups.Add(item.RawName);
                }

                RefreshAppFilterList();
            }
        }

        private void LstAppFilter_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            int index = _lstAppFilter.IndexFromPoint(e.Location);
            if (index < 0 || index >= _viewItems.Count) return;

            var item = _viewItems[index];

            if (item.HasChildren)
            {
                if (item.Level == 0)
                {
                    if (_expandedApps.Contains(item.RawName))
                        _expandedApps.Remove(item.RawName);
                    else
                        _expandedApps.Add(item.RawName);
                }
                else if (item.Level == 1)
                {
                    if (_expandedGroups.Contains(item.RawName))
                        _expandedGroups.Remove(item.RawName);
                    else
                        _expandedGroups.Add(item.RawName);
                }

                RefreshAppFilterList();
            }
        }

        private void LstAppFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isLoading) return;
            int index = _lstAppFilter.SelectedIndex;
            if (index < 0 || index >= _viewItems.Count) return;

            string selectedKey = _viewItems[index].RawName;

            if (selectedKey == Localization.Get("allApps"))
                FilterChanged?.Invoke(null);
            else
                FilterChanged?.Invoke(selectedKey);
        }

        private void TxtAppSearch_TextChanged(object sender, EventArgs e)
        {
            string searchText = _txtAppSearch.Text.Trim().ToLower();
            bool isSearch = !string.IsNullOrWhiteSpace(searchText) && searchText != Localization.Get("searchApps").ToLower();

            if (!isSearch)
            {
                _expandedApps.Clear();
                _expandedGroups.Clear();
            }

            RefreshAppFilterList();
        }

        private void OnIconLoaded(string name)
        {
            if (!_lstAppFilter.IsDisposed)
            {
                if (_lstAppFilter.InvokeRequired)
                    _lstAppFilter.BeginInvoke(new Action(() => _lstAppFilter.Invalidate()));
                else
                    _lstAppFilter.Invalidate();
            }
        }

        public void Dispose()
        {
            _iconManager.IconLoaded -= OnIconLoaded;
            _lstAppFilter.MouseClick -= LstAppFilter_MouseClick;
            _lstAppFilter.MouseDoubleClick -= LstAppFilter_MouseDoubleClick;
            _lstAppFilter.SelectedIndexChanged -= LstAppFilter_SelectedIndexChanged;
            _txtAppSearch.TextChanged -= TxtAppSearch_TextChanged;
        }
    }
}