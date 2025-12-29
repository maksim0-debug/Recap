using System;
using System.IO;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Recap
{
    public class BrowserTracker : IDisposable
    {
        private readonly HttpListener _listener;
        private string _currentDomain = "";
        private bool _isRunning = false;

        public string CurrentDomain => _currentDomain;

        public BrowserTracker()
        {
            try
            {
                _listener = new HttpListener();
                _listener.Prefixes.Add($"http://127.0.0.1:{AdvancedSettings.Instance.BrowserTrackerPort}/track/");
                _listener.Start();
                _isRunning = true;

                Task.Run(ListenLoop);
            }
            catch (Exception ex)
            {
                DebugLogger.LogError("BrowserTracker Init", ex);
                _isRunning = false;
            }
        }

        private async Task ListenLoop()
        {
            while (_isRunning && _listener.IsListening)
            {
                try
                {
                    var context = await _listener.GetContextAsync();

                    using (var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding))
                    {
                        string json = await reader.ReadToEndAsync();
                        ParseAndSetDomain(json);
                    }

                    context.Response.StatusCode = 200;
                    context.Response.Close();
                }
                catch
                {
                }
            }
        }

        private void ParseAndSetDomain(string json)
        {
            try
            {
                string key = "\"domain\":\"";
                int start = json.IndexOf(key);

                if (start != -1)
                {
                    start += key.Length;

                    StringBuilder sb = new StringBuilder();
                    bool escaped = false;

                    for (int i = start; i < json.Length; i++)
                    {
                        char c = json[i];

                        if (escaped)
                        {
                            sb.Append(c);
                            escaped = false;
                        }
                        else
                        {
                            if (c == '\\')
                            {
                                escaped = true;
                            }
                            else if (c == '"')
                            {
                                break;
                            }
                            else
                            {
                                sb.Append(c);
                            }
                        }
                    }

                    _currentDomain = UnescapeJson(sb.ToString());
                }
            }
            catch { }
        }

        private string UnescapeJson(string str)
        {
            if (string.IsNullOrEmpty(str)) return str;
            return str.Replace("\\/", "/");
        }

        public void Dispose()
        {
            _isRunning = false;
            try
            {
                if (_listener != null && _listener.IsListening)
                {
                    _listener.Stop();
                    _listener.Close();
                }
            }
            catch { }
        }
    }
}