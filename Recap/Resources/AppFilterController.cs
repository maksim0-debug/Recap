using System;
using System.ComponentModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace Recap
{
    public class FilterItem
    {
        public string RequestExpandKey;
        public string RawName;
        public List<string> RawNames = new List<string>();
        public string DisplayName;
        public long DurationMs;
        public int FrameCount;   
        public int Level;
        public bool HasChildren;
        public bool IsExpanded;
        public bool IsVideo;
        public string VideoId;
        public bool IsNote;
        public long NoteTimestamp;
        public string NoteDescription;

        public override string ToString() => DisplayName;
    }

    public class AppFilterController : IDisposable
    {
        public event Action<List<string>> FilterChanged;

        private readonly DarkListBox _lstAppFilter;
        private readonly TextBox _txtAppSearch;
        private readonly IconManager _iconManager;
        private readonly OcrDatabase _db;
        private readonly FrameRepository _repo;
        private readonly AppSettings _settings;
        private Dictionary<string, string> _aliases;
        private ContextMenuStrip _ctxMenuApps;
        private ToolStripMenuItem _toggleOcrItem;
        public event EventHandler AppHidden;
        public event Action<string, bool> OcrBlacklistToggled;

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
            "browser.exe",
            "firefox.exe",
            "telegram.exe", "ayugram.exe", "kotatogram.exe", "64gram.exe",
            "code.exe", "devenv.exe", "antigravity.exe"
        };

        private readonly HashSet<string> _messengers = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "telegram.exe", "ayugram.exe", "kotatogram.exe", "64gram.exe"
        };

        private Dictionary<string, NodeData> _cachedStats = new Dictionary<string, NodeData>();
        private long _cachedGlobalTotal = 0;
        private int _cachedGlobalFrameCount = 0;
        private Dictionary<string, ParsedAppInfo> _parsedAppInfoCache = new Dictionary<string, ParsedAppInfo>();

        public AppFilterController(DarkListBox lstAppFilter, TextBox txtAppSearch, IconManager iconManager, OcrDatabase db, FrameRepository repo, AppSettings settings)
        {
            _lstAppFilter = lstAppFilter;
            _txtAppSearch = txtAppSearch;
            _iconManager = iconManager;
            _db = db;
            _repo = repo;
            _settings = settings;
            _aliases = _db.LoadAppAliases();

            InitializeContextMenu();

            _lstAppFilter.IconManager = _iconManager;
            _iconManager.IconLoaded += OnIconLoaded;

            _lstAppFilter.MouseClick += LstAppFilter_MouseClick;
            _lstAppFilter.MouseDown += LstAppFilter_MouseDown;
            _lstAppFilter.MouseDoubleClick += LstAppFilter_MouseDoubleClick;
            _lstAppFilter.SelectedIndexChanged += LstAppFilter_SelectedIndexChanged;
            _txtAppSearch.TextChanged += TxtAppSearch_TextChanged;
        }

        public void SetEnabled(bool enabled)
        {
            _lstAppFilter.Enabled = enabled;
        }

        public async System.Threading.Tasks.Task SetDataAsync(List<MiniFrame> frames, Dictionary<int, string> appMap)
        {
            _isLoading = true;
            _currentFrames = frames ?? new List<MiniFrame>();
            _appMap = appMap;
            _parsedAppInfoCache.Clear();

            DebugLogger.Log($"[AppFilter] SetDataAsync started for {frames.Count} frames.");
            var sw = System.Diagnostics.Stopwatch.StartNew();

            await System.Threading.Tasks.Task.Run(() => RebuildStats());

            sw.Stop();
            DebugLogger.Log($"[AppFilter] RebuildStats took {sw.ElapsedMilliseconds} ms.");

            sw.Restart();
            RebuildAggregatedStats();
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
            RebuildAggregatedStats();
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
            public List<KeyValuePair<string, NodeData>> SortedChildrenByTime;
            public List<KeyValuePair<string, NodeData>> SortedChildrenByFrames;
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

            var uniqueAppNames = new HashSet<string>();
            foreach (var kvp in appMap)
            {
                uniqueAppNames.Add(kvp.Value);
            }
            
            foreach (var name in uniqueAppNames)
            {
                if (!_parsedAppInfoCache.ContainsKey(name))
                {
                    _parsedAppInfoCache[name] = ParseAppInfo(name);
                }
            }

            var rangePartitioner = System.Collections.Concurrent.Partitioner.Create(0, count);

            Parallel.ForEach(rangePartitioner, new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount }, range =>
            {
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

                    ParsedAppInfo info;
                    lock (_parsedAppInfoCache)
                    {
                        if (!_parsedAppInfoCache.TryGetValue(currentAppName, out info))
                        {
                            info = ParseAppInfo(currentAppName);
                            _parsedAppInfoCache[currentAppName] = info;
                        }
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
            if (_parsedAppInfoCache.TryGetValue(currentAppName, out info))
            {
            }
            else
            {
                info = ParseAppInfo(currentAppName);
                _parsedAppInfoCache[currentAppName] = info;
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

                if (info.EffectiveExe.IndexOf("devenv", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                     if (!string.IsNullOrEmpty(info.DetailKey)) 
                     {
                         info.DetailKey = info.DetailKey.TrimEnd('*');
                         info.DetailKey = System.Text.RegularExpressions.Regex.Replace(info.DetailKey, @"\s\([^\)]+\)$", "");
                     }
                     if (!string.IsNullOrEmpty(info.GroupKey)) 
                     {
                         info.GroupKey = info.GroupKey.TrimEnd('*');
                         info.GroupKey = System.Text.RegularExpressions.Regex.Replace(info.GroupKey, @"\s\([^\)]+\)$", "");
                     }
                }
            }
            return info;
        }

        private List<KeyValuePair<string, (NodeData Node, List<string> Raws, string PrimaryRaw)>> _cachedSortedGroupsByTime;
        private List<KeyValuePair<string, (NodeData Node, List<string> Raws, string PrimaryRaw)>> _cachedSortedGroupsByFrames;

        private void RebuildAggregatedStats()
        {
            var stats = _cachedStats;
            var groupedParams = new Dictionary<string, (NodeData Node, List<string> Raws, string PrimaryRaw)>(StringComparer.OrdinalIgnoreCase);

            foreach (var kvp in stats)
            {
                string rawName = kvp.Key;
                var node = kvp.Value;
                
                string displayName = rawName == "Legacy" ? Localization.Get("legacyApp") : rawName;
                if (_aliases != null && _aliases.ContainsKey(rawName)) displayName = _aliases[rawName];

                if (!groupedParams.ContainsKey(displayName))
                {
                    groupedParams[displayName] = (new NodeData(), new List<string>(), rawName);
                }

                var entry = groupedParams[displayName];
                entry.Raws.Add(rawName);
                entry.Node.TotalMs += node.TotalMs;
                entry.Node.FrameCount += node.FrameCount;

                if (stats.ContainsKey(entry.PrimaryRaw) && node.TotalMs > stats[entry.PrimaryRaw].TotalMs)
                {
                     groupedParams[displayName] = (entry.Node, entry.Raws, rawName);
                }

                foreach(var childKvp in node.Children)
                {
                    if (!entry.Node.Children.ContainsKey(childKvp.Key)) entry.Node.Children[childKvp.Key] = new NodeData();
                    var aggChild = entry.Node.Children[childKvp.Key];
                    aggChild.TotalMs += childKvp.Value.TotalMs;
                    aggChild.FrameCount += childKvp.Value.FrameCount;

                    foreach(var grandKvp in childKvp.Value.Children)
                    {
                        if (!aggChild.Children.ContainsKey(grandKvp.Key)) aggChild.Children[grandKvp.Key] = new NodeData();
                        var aggGrand = aggChild.Children[grandKvp.Key];
                        aggGrand.TotalMs += grandKvp.Value.TotalMs;
                        aggGrand.FrameCount += grandKvp.Value.FrameCount;
                        if (grandKvp.Value.IsVideoNode)
                        {
                            aggGrand.IsVideoNode = true;
                            aggGrand.VideoId = grandKvp.Value.VideoId;
                        }
                    }
                }
            }

            foreach (var kvp in groupedParams)
            {
                SortNodeChildren(kvp.Value.Node);
            }

            _cachedSortedGroupsByTime = groupedParams.OrderByDescending(kv => kv.Value.Node.TotalMs).ToList();
            _cachedSortedGroupsByFrames = groupedParams.OrderByDescending(kv => kv.Value.Node.FrameCount).ToList();
        }

        private void SortNodeChildren(NodeData node)
        {
            node.SortedChildrenByTime = node.Children.OrderByDescending(x => x.Value.TotalMs).ToList();
            node.SortedChildrenByFrames = node.Children.OrderByDescending(x => x.Value.FrameCount).ToList();
            foreach (var child in node.Children.Values)
            {
                SortNodeChildren(child);
            }
        }

        private bool CheckIfAnyChildMatches(NodeData node, string searchText)
        {
            if (node.Children == null || node.Children.Count == 0) return false;
            
            foreach (var child in node.Children)
            {
                if (child.Key.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0) 
                    return true;
                    
                if (CheckIfAnyChildMatches(child.Value, searchText)) 
                    return true;
            }
            return false;
        }

        private void RefreshAppFilterList()
        {
            if (_lstAppFilter == null || _lstAppFilter.IsDisposed) return;

            string savedSelection = null;
            if (_lstAppFilter.SelectedIndex >= 0 && _lstAppFilter.SelectedIndex < _viewItems.Count)
                savedSelection = _viewItems[_lstAppFilter.SelectedIndex].DisplayName;

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
            allItem.RawNames.Add(Localization.Get("allApps"));
            _viewItems.Add(allItem);
            _lstAppFilter.Items.Add(allItem);

            bool sortByFrames = _lstAppFilter.ShowFrameCount;

            if (_cachedSortedGroupsByTime == null || _cachedSortedGroupsByFrames == null)
            {
                RebuildAggregatedStats();
            }

            var sortedGroups = sortByFrames ? _cachedSortedGroupsByFrames : _cachedSortedGroupsByTime;

            foreach (var groupKvp in sortedGroups)
            {
                string displayName = groupKvp.Key;
                var info = groupKvp.Value;
                var appNode = info.Node;
                
                bool isMergedGroup = info.Raws.Count > 1;
                bool hasChildren = isMergedGroup || appNode.Children.Count > 0;

                if (isSearch)
                {
                    bool parentMatches = displayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                    
                    if (!parentMatches)
                    {
                        bool childrenMatch = CheckIfAnyChildMatches(appNode, searchText);
                        
                        if (!childrenMatch && isMergedGroup)
                        {
                            childrenMatch = info.Raws.Any(r => r.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0);
                        }

                        if (!childrenMatch) continue; 
                    }
                }
                string expandKey = displayName; 
                bool isAppExpanded = _expandedApps.Contains(expandKey) || isSearch;

                string effectiveRawName = info.PrimaryRaw;
                if (isMergedGroup)
                {
                    var sortedMembers = info.Raws
                        .Select(r => new { Name = r, Total = stats.ContainsKey(r) ? stats[r].TotalMs : 0 })
                        .OrderByDescending(x => x.Total)
                        .ToList();
                    
                    var topApps = sortedMembers.Take(4).Select(x => x.Name);
                    effectiveRawName = "$$COMPOSITE$$|" + string.Join("|", topApps);
                }

                var newItem = new FilterItem
                {
                    RawName = effectiveRawName, 
                    RawNames = info.Raws,
                    DisplayName = displayName,
                    DurationMs = appNode.TotalMs,
                    FrameCount = appNode.FrameCount,
                    Level = 0,
                    HasChildren = hasChildren,
                    IsExpanded = isAppExpanded
                };
                _viewItems.Add(newItem);
                _lstAppFilter.Items.Add(newItem);

                if (isAppExpanded)
                {
                    if (isMergedGroup)
                    {
                        var memberStats = info.Raws
                            .Where(r => stats.ContainsKey(r))
                            .Select(r => new { RawName = r, Node = stats[r] })
                            .OrderByDescending(x => sortByFrames ? x.Node.FrameCount : x.Node.TotalMs);

                        foreach (var member in memberStats)
                        {
                            string memberName = member.RawName;
                            
                            if (isSearch)
                            {
                                bool match = memberName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0 ||
                                             CheckIfAnyChildMatches(member.Node, searchText);
                                if (!match && displayName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) < 0) 
                                    continue; 
                            }
                            var memberNode = member.Node;
                            bool memberHasChildren = memberNode.Children.Count > 0;
                            string memberExpandKey = $"{expandKey}|{memberName}";
                            
                            bool isMemberExpanded = _expandedGroups.Contains(memberExpandKey) || isSearch;

                            var memberItem = new FilterItem
                            {
                                RawName = memberName,       
                                RequestExpandKey = memberExpandKey,
                                RawNames = new List<string> { memberName },
                                DisplayName = memberName,
                                DurationMs = memberNode.TotalMs,
                                FrameCount = memberNode.FrameCount,
                                Level = 1,
                                HasChildren = memberHasChildren,
                                IsExpanded = isMemberExpanded
                            };
                            _viewItems.Add(memberItem);
                            _lstAppFilter.Items.Add(memberItem);

                            if (memberHasChildren && isMemberExpanded)
                            {
                                RenderChildren(memberNode, memberName, memberExpandKey, sortByFrames, isSearch, searchText, 2);
                            }
                        }
                    }
                    else
                    {
                        RenderChildren(appNode, info.PrimaryRaw, expandKey, sortByFrames, isSearch, searchText, 1);
                    }
                }
            }
            
            RestoreSelection(savedSelection, savedTopIndex);

            _lstAppFilter.EndUpdate();
            _lstAppFilter.Invalidate();
        }

        private void RenderChildren(NodeData parentNode, string parentRaw, string parentKey, bool sortByFrames, bool isSearch, string searchText, int level)
        {
            var sortedChildren = sortByFrames ? parentNode.SortedChildrenByFrames : parentNode.SortedChildrenByTime;

            foreach (var childKvp in sortedChildren)
            {
                string childName = childKvp.Key;
                var childNode = childKvp.Value;
                
                if (isSearch)
                {
                    bool childMatches = childName.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                    
                    if (!childMatches)
                    {
                        childMatches = CheckIfAnyChildMatches(childNode, searchText);
                    }
                    
                    if (!childMatches)
                    {
                        bool parentMatches = parentRaw.IndexOf(searchText, StringComparison.OrdinalIgnoreCase) >= 0;
                        if (!parentMatches) continue; 
                    }
                }
                bool hasGrandChildren = childNode.Children.Count > 0;
                string fullGroupKey = $"{parentKey}|{childName}";

                bool isGroupExpanded = _expandedGroups.Contains(fullGroupKey) || isSearch;

                var childItem = new FilterItem
                {
                    RawName = fullGroupKey,
                    RawNames = new List<string> { $"{parentRaw}|{childName}" },
                    DisplayName = childName,
                    DurationMs = childNode.TotalMs,
                    FrameCount = childNode.FrameCount,
                    Level = level,
                    HasChildren = hasGrandChildren,
                    IsExpanded = isGroupExpanded
                };
                _viewItems.Add(childItem);
                _lstAppFilter.Items.Add(childItem);

                if (hasGrandChildren && isGroupExpanded)
                {
                    RenderChildren(childNode, $"{parentRaw}|{childName}", fullGroupKey, sortByFrames, isSearch, searchText, level + 1);
                }
                else if (level >= 2 && childNode.IsVideoNode)
                {
                     childItem.IsVideo = true;
                     childItem.VideoId = childNode.VideoId;
                }
            }
        }

        private void RestoreSelection(string savedName, int topIndex)
        {
            if (savedName != null)
            {
                int newIndex = _viewItems.FindIndex(x => x.DisplayName == savedName);
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
                string key = item.RequestExpandKey ?? (item.Level == 0 ? item.DisplayName : item.RawName);

                if (item.Level == 0)
                {
                    if (_expandedApps.Contains(key))
                        _expandedApps.Remove(key);
                    else
                        _expandedApps.Add(key);
                }
                else if (item.Level >= 1)
                {
                    if (_expandedGroups.Contains(key))
                        _expandedGroups.Remove(key);
                    else
                        _expandedGroups.Add(key);
                }

                _ignoreNextSelectionChange = true;
                RefreshAppFilterList();
                _ignoreNextSelectionChange = false;
            }
        }

        private void LstAppFilter_MouseDoubleClick(object sender, MouseEventArgs e)
        {
            int index = _lstAppFilter.IndexFromPoint(e.Location);
            if (index < 0 || index >= _viewItems.Count) return;

            var item = _viewItems[index];

            int clickArea = 20 + (item.Level * 10);
            if (item.HasChildren && e.X >= clickArea)
            {
                string key = item.RequestExpandKey ?? (item.Level == 0 ? item.DisplayName : item.RawName);

                if (item.Level == 0)
                {
                    if (_expandedApps.Contains(key))
                        _expandedApps.Remove(key);
                    else
                        _expandedApps.Add(key);
                }
                else if (item.Level >= 1)
                {
                    if (_expandedGroups.Contains(key))
                        _expandedGroups.Remove(key);
                    else
                        _expandedGroups.Add(key);
                }

                _ignoreNextSelectionChange = true;
                RefreshAppFilterList();
                _ignoreNextSelectionChange = false;
            }
        }

        private bool _ignoreNextSelectionChange = false;

        private void LstAppFilter_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (_isLoading || _ignoreNextSelectionChange) return;
            int index = _lstAppFilter.SelectedIndex;
            if (index < 0 || index >= _viewItems.Count) return;

            var item = _viewItems[index];

            if (item.DisplayName == Localization.Get("allApps"))
                FilterChanged?.Invoke(null);
            else
            {
                var list = item.RawNames != null && item.RawNames.Count > 0 ? item.RawNames : new List<string> { item.RawName };
                FilterChanged?.Invoke(list);
            }
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

        private ToolStripMenuItem _excludeAppItem;
        private ToolStripMenuItem _openFileLocationItem;
        private AppPathResolver _pathResolver;

        private void InitializeContextMenu()
        {
            _pathResolver = new AppPathResolver(_db);

            _ctxMenuApps = new ContextMenuStrip();
            var renameItem = new ToolStripMenuItem(Localization.Get("rename"));
            renameItem.Click += RenameMenuItem_Click;
            _ctxMenuApps.Items.Add(renameItem);

            var resetItem = new ToolStripMenuItem(Localization.Get("resetName"));
            resetItem.Click += ResetNameMenuItem_Click;
            _ctxMenuApps.Items.Add(resetItem);

            _excludeAppItem = new ToolStripMenuItem(Localization.Get("excludeApp"));
            _excludeAppItem.Click += ExcludeAppMenuItem_Click;
            _ctxMenuApps.Items.Add(_excludeAppItem);

            var hideItem = new ToolStripMenuItem(Localization.Get("hideApp"));
            hideItem.Click += HideAppMenuItem_Click;
            _ctxMenuApps.Items.Add(hideItem);

            _ctxMenuApps.Items.Add(new ToolStripSeparator());

            _toggleOcrItem = new ToolStripMenuItem();
            _toggleOcrItem.Click += ToggleOcrMenuItem_Click;
            _ctxMenuApps.Items.Add(_toggleOcrItem);

            _openFileLocationItem = new ToolStripMenuItem(Localization.Get("openFileLocation"));
            _openFileLocationItem.Click += OpenFileLocationMenuItem_Click;
            _ctxMenuApps.Items.Add(_openFileLocationItem);

            _ctxMenuApps.Items.Add(new ToolStripSeparator());

            var changeIconItem = new ToolStripMenuItem(Localization.Get("changeIcon"));
            changeIconItem.Click += ChangeIconMenuItem_Click;
            _ctxMenuApps.Items.Add(changeIconItem);

            var resetIconItem = new ToolStripMenuItem(Localization.Get("resetIcon"));
            resetIconItem.Click += ResetIconMenuItem_Click;
            _ctxMenuApps.Items.Add(resetIconItem);
        }

        private void ChangeIconMenuItem_Click(object sender, EventArgs e)
        {
            if (_lstAppFilter.SelectedIndex < 0 || _lstAppFilter.SelectedIndex >= _viewItems.Count) return;
            var item = _viewItems[_lstAppFilter.SelectedIndex];
            
            using (var ofd = new OpenFileDialog())
            {
                ofd.Filter = "Images|*.png;*.jpg;*.jpeg;*.webp;*.bmp;*.ico";
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _iconManager.SetCustomIcon(item.RawName, ofd.FileName);
                }
            }
        }

        private void ResetIconMenuItem_Click(object sender, EventArgs e)
        {
            if (_lstAppFilter.SelectedIndex < 0 || _lstAppFilter.SelectedIndex >= _viewItems.Count) return;
            var item = _viewItems[_lstAppFilter.SelectedIndex];
            _iconManager.ResetCustomIcon(item.RawName);
        }

        private string GetTargetProcessName(FilterItem item)
        {
            if (item.RawName.StartsWith("$$COMPOSITE$$")) return null;

            string raw = (item.RawNames != null && item.RawNames.Count > 0) ? item.RawNames[0] : item.RawName;
            string exeName = FrameHelper.ParseAppName(raw).ExeName;
            return exeName.ToLower().Replace(".exe", "");
        }

        private void LstAppFilter_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right)
            {
                int index = _lstAppFilter.IndexFromPoint(e.Location);
                if (index >= 0 && index < _viewItems.Count)
                {
                    _lstAppFilter.SelectedIndex = index;
                    var item = _viewItems[index];

                    if (item.Level >= 0 && item.RawName != Localization.Get("allApps"))
                    {
                        bool isMerged = item.RawNames != null && item.RawNames.Count > 1;
                        _excludeAppItem.Visible = isMerged;

                        string targetName = GetTargetProcessName(item);
                        bool canOpenFileLocation = targetName != null && !item.RawName.Contains("|");
                        _openFileLocationItem.Visible = canOpenFileLocation;

                        if (targetName == null || item.IsNote)
                        {
                            _toggleOcrItem.Visible = false;
                        }
                        else
                        {
                            _toggleOcrItem.Visible = true;
                            bool isInBlacklist = _settings.OcrBlacklist.Cast<string>().Contains(targetName, StringComparer.OrdinalIgnoreCase);
                            _toggleOcrItem.Text = isInBlacklist ? Localization.Get("enableOcr") : Localization.Get("excludeFromOcr");
                        }

                        _ctxMenuApps.Show(_lstAppFilter, e.Location);
                    }
                }
            }
        }

        private void ToggleOcrMenuItem_Click(object sender, EventArgs e)
        {
            if (_lstAppFilter.SelectedIndex < 0 || _lstAppFilter.SelectedIndex >= _viewItems.Count) return;
            var item = _viewItems[_lstAppFilter.SelectedIndex];

            string cleanName = GetTargetProcessName(item);
            if (cleanName == null) return;

            bool isInBlacklist = _settings.OcrBlacklist.Cast<string>().Contains(cleanName, StringComparer.OrdinalIgnoreCase);

            OcrBlacklistToggled?.Invoke(cleanName, !isInBlacklist);
        }

        private async void OpenFileLocationMenuItem_Click(object sender, EventArgs e)
        {
            if (_lstAppFilter.SelectedIndex < 0 || _lstAppFilter.SelectedIndex >= _viewItems.Count) return;
            var item = _viewItems[_lstAppFilter.SelectedIndex];

            string appName = item.RawName;
            if (string.IsNullOrEmpty(appName)) return;

            Cursor.Current = Cursors.WaitCursor;
            try
            {
                string resolvedPath = await _pathResolver.ResolveAsync(appName);
                if (!string.IsNullOrEmpty(resolvedPath))
                {
                    System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{resolvedPath}\"");
                }
                else
                {
                    ShowInfoMessage(Localization.Get("errFileNotFound"));
                }
            }
            catch (Win32Exception)
            {
                ShowInfoMessage(Localization.Get("errOpenFileLocationDenied"));
            }
            catch (Exception ex)
            {
                ShowInfoMessage(Localization.Get("errFileNotFound") + "\n\n" + ex.Message);
            }
            finally
            {
                Cursor.Current = Cursors.Default;
            }
        }

        private void ShowInfoMessage(string message)
        {
            using (var dialog = new Form())
            {
                dialog.Text = Localization.Get("windowTitle");
                dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
                dialog.StartPosition = FormStartPosition.CenterParent;
                dialog.MaximizeBox = false;
                dialog.MinimizeBox = false;
                dialog.ClientSize = new System.Drawing.Size(500, 165);

                var iconBox = new PictureBox
                {
                    Image = _iconManager?.GetInfoIcon(),
                    Size = new System.Drawing.Size(32, 32),
                    Location = new System.Drawing.Point(20, 20),
                    SizeMode = PictureBoxSizeMode.Zoom
                };

                var lbl = new Label
                {
                    Text = message,
                    AutoSize = false,
                    Location = new System.Drawing.Point(65, 20),
                    Size = new System.Drawing.Size(410, 80)
                };

                var ok = new Button
                {
                    Text = Localization.Get("ok"),
                    DialogResult = DialogResult.OK,
                    Size = new System.Drawing.Size(100, 30),
                    Location = new System.Drawing.Point(375, 115)
                };

                dialog.Controls.Add(iconBox);
                dialog.Controls.Add(lbl);
                dialog.Controls.Add(ok);
                dialog.AcceptButton = ok;

                AppStyler.Apply(dialog);
                dialog.ShowDialog();
            }
        }

        private void ExcludeAppMenuItem_Click(object sender, EventArgs e)
        {
             if (_lstAppFilter.SelectedIndex < 0) return;
             var item = _viewItems[_lstAppFilter.SelectedIndex];
             
             if (item.RawNames == null || item.RawNames.Count < 2) return;

             using (var form = new ExcludeAppsForm(item.DisplayName, item.RawNames, _iconManager))
             {
                 if (form.ShowDialog() == DialogResult.OK)
                 {
                     if (form.ExcludeAll)
                     {
                         foreach(var raw in item.RawNames)
                         {
                             if (_aliases.ContainsKey(raw)) {
                                 _aliases.Remove(raw);
                                 _db.RemoveAppAlias(raw);
                             }
                         }
                     }
                     else
                     {
                         foreach(var raw in form.SelectedApps)
                         {
                             if (_aliases.ContainsKey(raw)) {
                                 _aliases.Remove(raw);
                                 _db.RemoveAppAlias(raw);
                             }
                         }
                     }
                     RebuildAggregatedStats();
                     RefreshAppFilterList();
                 }
             }
        }

        private void HideAppMenuItem_Click(object sender, EventArgs e)
        {
            if (_lstAppFilter.SelectedIndex < 0) return;
            var item = _viewItems[_lstAppFilter.SelectedIndex];

            if (MessageBox.Show(Localization.Get("hideAppConfirm"), Localization.Get("windowTitle"), MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                var targets = item.RawNames != null && item.RawNames.Count > 0 ? item.RawNames : new List<string> { item.RawName };
                foreach (var raw in targets)
                {
                    _repo.HideApp(raw);
                }
                AppHidden?.Invoke(this, EventArgs.Empty);
            }
        }

        private void RenameMenuItem_Click(object sender, EventArgs e)
        {
            if (_lstAppFilter.SelectedIndex < 0) return;
            var item = _viewItems[_lstAppFilter.SelectedIndex];

            using (var form = new RenameForm(item.DisplayName))
            {
                if (form.ShowDialog() == DialogResult.OK)
                {
                    string newName = form.NewName.Trim();
                    if (string.IsNullOrEmpty(newName)) return;

                    if (_aliases == null) _aliases = new Dictionary<string, string>();

                    bool alreadyExists = _aliases.Values.Any(v => v.Equals(newName, StringComparison.OrdinalIgnoreCase));

                    if (alreadyExists)
                    {
                        var dr = ChoiceDialog.Show(
                            string.Format(Localization.Get("mergeAppsConfirm"), newName),
                            Localization.Get("windowTitle"),
                            Localization.Get("yes"),
                            Localization.Get("no"),
                            Localization.Get("cancel"),
                            _iconManager.GetIcon(item.RawName));

                        if (dr == DialogResult.Cancel) return;
                        if (dr == DialogResult.No)
                        {
                            int attempt = 2;
                            string candidate = newName + " " + attempt;
                            while (_aliases.Values.Any(v => v.Equals(candidate, StringComparison.OrdinalIgnoreCase)))
                            {
                                attempt++;
                                candidate = newName + " " + attempt;
                            }
                            newName = candidate;
                        }
                    }

                    var targets = item.RawNames != null && item.RawNames.Count > 0 ? item.RawNames : new List<string> { item.RawName };
                    
                    foreach (var raw in targets)
                    {
                        _aliases[raw] = newName;
                        _db.SetAppAlias(raw, newName);
                    }
                    RebuildAggregatedStats();
                    RefreshAppFilterList();
                }
            }
        }

        private void ResetNameMenuItem_Click(object sender, EventArgs e)
        {
            if (_lstAppFilter.SelectedIndex < 0) return;
            var item = _viewItems[_lstAppFilter.SelectedIndex];

            if (_aliases != null)
            {
                var targets = item.RawNames != null && item.RawNames.Count > 0 ? item.RawNames : new List<string> { item.RawName };
                foreach (var raw in targets)
                {
                    if (_aliases.ContainsKey(raw))
                    {
                        _aliases.Remove(raw);
                        _db.RemoveAppAlias(raw);
                    }
                }
                RebuildAggregatedStats();
                RefreshAppFilterList();
            }
        }

        public void Dispose()
        {
            OcrBlacklistToggled = null;
            _iconManager.IconLoaded -= OnIconLoaded;
            _lstAppFilter.MouseClick -= LstAppFilter_MouseClick;
            _lstAppFilter.MouseDown -= LstAppFilter_MouseDown;
            _lstAppFilter.MouseDoubleClick -= LstAppFilter_MouseDoubleClick;
            _lstAppFilter.SelectedIndexChanged -= LstAppFilter_SelectedIndexChanged;
            _txtAppSearch.TextChanged -= TxtAppSearch_TextChanged;
            _ctxMenuApps?.Dispose();
        }
    }
}