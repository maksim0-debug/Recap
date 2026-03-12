using System;
using System.Linq;
using System.Text.RegularExpressions;

namespace Recap.Utilities
{
    public static class AppNameParser
    {
        public static string ParseWindowName(string processName, string domain, string title)
        {
            string procLower = processName.ToLower();

            if (procLower.Contains("chrome") ||
                procLower.Contains("msedge") ||
                procLower.Contains("brave") ||
                procLower.Contains("opera"))
            {
                if (!string.IsNullOrEmpty(domain))
                {
                    title = CleanupBrowserSuffixes(title);

                    if (domain.Contains("kick.com"))
                    {
                        string clean = CleanupTitle(title, new[] { " - Kick", " | Kick" });
                        if (!string.IsNullOrEmpty(clean))
                        {
                            return $"{processName}|kick.com|{clean}";
                        }
                        return $"{processName}|kick.com|Stream";
                    }
                    else if (domain.Contains("aistudio.google.com"))
                    {
                        string clean = CleanupTitle(title, new[] { " - Google AI Studio", " | Google AI Studio" });
                        if (!string.IsNullOrEmpty(clean))
                        {
                            return $"{processName}|aistudio.google.com|{clean}";
                        }
                        return $"{processName}|aistudio.google.com|Prompt";
                    }
                    else if (domain.Contains("github.com"))
                    {
                        string[] cleanSuffixes = { " - GitHub", " | GitHub", " · GitHub" };
                        string cleanTitle = CleanupTitle(title, cleanSuffixes);
                        return ParseGitHubTitle(processName, cleanTitle);
                    }
                    else if (domain.IndexOf("rezka", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        if (!string.IsNullOrEmpty(title) && title != domain && !title.Equals("New Tab", StringComparison.OrdinalIgnoreCase))
                        {
                            return $"{processName}|{domain}|{title}";
                        }
                        return $"{processName}|{domain}";
                    }
                    else
                    {
                        if (string.IsNullOrEmpty(title))
                            return $"{processName}|{domain}";
                        else
                            return $"{processName}|{domain}|{title}";
                    }
                }
            }
            else if (procLower.Contains("code"))
            {
                if (title.StartsWith("● ")) title = title.Substring(2);

                string project = CleanupTitle(title, new[] { " - Visual Studio Code" });

                if (!string.IsNullOrEmpty(project))
                {
                    int lastDash = project.LastIndexOf(" - ");
                    if (lastDash >= 0 && lastDash < project.Length - 3)
                    {
                        string projName = project.Substring(lastDash + 3);
                        string fileName = project.Substring(0, lastDash);
                        return $"{processName}|{projName}|{fileName}";
                    }
                    else
                    {
                        return $"{processName}|{project}";
                    }
                }
            }
            else if (procLower.Contains("devenv"))
            {
                string solution = CleanupTitle(title, new[] { " - Microsoft Visual Studio", " - Visual Studio" });

                if (!string.IsNullOrEmpty(solution))
                {
                    int lastDash = solution.LastIndexOf(" - ");
                    if (lastDash >= 0 && lastDash < solution.Length - 3)
                    {
                        string solName = solution.Substring(0, lastDash);
                        string fileName = solution.Substring(lastDash + 3);

                        fileName = fileName.TrimEnd('*');
                        fileName = Regex.Replace(fileName, @"\s\([^\)]+\)$", "");
                        return $"{processName}|{solName}|{fileName}";
                    }
                    else
                    {
                        return $"{processName}|{solution}";
                    }
                }
            }
            else if (procLower.Contains("antigravity"))
            {
                string project = CleanupTitle(title, new[] { " - Antigravity - " });
                if (!string.IsNullOrEmpty(project))
                {
                    int lastDash = project.IndexOf(" - Antigravity - ");
                    if (lastDash >= 0)
                    {
                        string projName = project.Substring(0, lastDash);
                        string fileName = project.Substring(lastDash + 17);
                        return $"{processName}|{projName}|{fileName}";
                    }
                    
                    return $"{processName}|{project}";
                }
            }
            else if (procLower.Contains("telegram") || procLower.Contains("ayugram") || procLower.Contains("kotatogram"))
            {
                string chatName = title;
                int dashIndex = chatName.IndexOf(" - ");
                if (dashIndex > 0)
                {
                    chatName = chatName.Substring(0, dashIndex);
                }
                
                chatName = Regex.Replace(chatName, @"\s*\(\d+\)$", "");
                
                if (!string.IsNullOrEmpty(chatName) && chatName != "Telegram" && chatName != "AyuGram" && chatName != "Kotatogram")
                {
                    return $"{processName}|{chatName}";
                }
            }

            return processName;
        }

        private static string CleanupBrowserSuffixes(string title)
        {
            if (string.IsNullOrEmpty(title)) return title;

            string[] suffixes = {
                " - Google Chrome", " - Microsoft​ Edge", " - Brave", " - Opera",
                " - Google Chrome (Incognito)", " - Microsoft Edge (InPrivate)"
            };

            return CleanupTitle(title, suffixes);
        }

        private static string CleanupTitle(string title, string[] suffixesToRemove)
        {
            if (string.IsNullOrEmpty(title)) return title;

            string current = title;
            foreach (var suffix in suffixesToRemove)
            {
                if (current.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                {
                    current = current.Substring(0, current.Length - suffix.Length);
                }
            }
            return current.Trim();
        }

        public static string ParseGitHubTitle(string processName, string title)
        {
            if (string.IsNullOrWhiteSpace(title)) return $"{processName}|github.com";

            string groupKey = "github.com";
            string detailKey = "";
            string subDetailKey = "";
            
            if (title.Contains(" (") && title.EndsWith(")"))
            {
                string possibleUser = title.Substring(0, title.IndexOf(" (")).Trim();
                if (!possibleUser.Contains(" "))
                {
                    detailKey = $"User: {possibleUser}";
                    return $"{processName}|{groupKey}|{detailKey}";
                }
            }

            int lastDotId = title.LastIndexOf(" · ");
            if (lastDotId >= 0 && lastDotId < title.Length - 3)
            {
                string context = title.Substring(0, lastDotId).Trim();
                string ownerRepo = title.Substring(lastDotId + 3).Trim();

                string commitHash = null;
                int atIndex = ownerRepo.IndexOf('@');
                if (atIndex > 0)
                {
                    commitHash = ownerRepo.Substring(atIndex + 1).Trim();
                    ownerRepo = ownerRepo.Substring(0, atIndex).Trim();
                }

                if (ownerRepo.Contains("/") && ownerRepo.Split('/').Length == 2)
                {
                    detailKey = ownerRepo;
                    string extraDetail = null;
                    
                    if (context.Contains(" · Issue #") || context.StartsWith("Issue #"))
                    {
                        subDetailKey = "Issues";
                        extraDetail = context;
                    }
                    else if (context.EndsWith("Issues"))
                        subDetailKey = "Issues";
                    else if (context.Contains(" · Pull Request #") || context.StartsWith("Pull Request #"))
                    {
                        subDetailKey = "Pull Requests";
                        extraDetail = context;
                    }
                    else if (context.EndsWith("Pull requests"))
                        subDetailKey = "Pull Requests";
                    else if (context == "Commits" || context.EndsWith("Commits"))
                        subDetailKey = "Commits";
                    else if (context == "Releases" || context.EndsWith("Releases"))
                        subDetailKey = "Releases";
                    else if (context.StartsWith("Search") || context.Contains("Search ·"))
                        subDetailKey = "Search";
                    else
                        subDetailKey = "Code";

                    if (commitHash != null)
                    {
                        subDetailKey = "Commits";
                        return $"{processName}|{groupKey}|{detailKey}|{subDetailKey}|@{commitHash}";
                    }

                    if (extraDetail != null)
                    {
                        return $"{processName}|{groupKey}|{detailKey}|{subDetailKey}|{extraDetail}";
                    }

                    return $"{processName}|{groupKey}|{detailKey}|{subDetailKey}";
                }
            }
            
            int colonIndex = title.IndexOf(": ");
            if (colonIndex > 0)
            {
                string possibleRepo = title.Substring(0, colonIndex).Trim();
                if (possibleRepo.Contains("/") && possibleRepo.Split('/').Length == 2 && !possibleRepo.Contains(" "))
                {
                    detailKey = possibleRepo;
                    subDetailKey = "Code";
                    return $"{processName}|{groupKey}|{detailKey}|{subDetailKey}";
                }
            }

            if (title.Contains("/") && title.Split('/').Length == 2 && !title.Contains(" "))
            {
                detailKey = title.Trim();
                subDetailKey = "Code";
                return $"{processName}|{groupKey}|{detailKey}|{subDetailKey}";
            }

            detailKey = title;
            return $"{processName}|{groupKey}|{detailKey}";
        }

        public static string UpgradeLegacyName(string oldName)
        {
            if (string.IsNullOrEmpty(oldName) || !oldName.Contains("|")) return oldName;

            var parts = oldName.Split('|');
            string processName = parts[0];
            string procLower = processName.ToLower();

            bool isBrowser = procLower.Contains("chrome") || procLower.Contains("msedge") ||
                             procLower.Contains("brave") || procLower.Contains("opera");

            if (isBrowser)
            {
                if (parts.Length >= 3)
                {
                    string domain = parts[1];
                    string title = string.Join("|", parts.Skip(2));
                    return ParseWindowName(processName, domain, title);
                }
            }
            else
            {
                if (parts.Length >= 2)
                {
                    string title = string.Join("|", parts.Skip(1));
                    return ParseWindowName(processName, "", title);
                }
            }

            return oldName;
        }
    }
}