using System;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using Newtonsoft.Json.Linq;

namespace FieldGraphX.Controls
{
    /// <summary>
    /// Hosts the vis-network dependency graph in a WebView2.
    ///
    /// The embedded graph.html + vis-network.min.js are extracted to a per-user folder and
    /// served through a virtual host mapping (https://fieldgraphx.local/) — file:// URLs
    /// would restrict script execution. The WebView2 user-data folder is set explicitly
    /// because the XrmToolBox install directory is usually not writable.
    /// </summary>
    public sealed class GraphViewPanel : UserControl
    {
        private const string VirtualHost = "fieldgraphx.local";

        private WebView2 _webView;
        private Label _fallbackLabel;
        private bool _initialized;
        private string _pendingGraphJson;

        /// <summary>Raised when a node is clicked in the graph (graph node id).</summary>
        public event Action<string> NodeSelected;

        /// <summary>Raised when a node is double-clicked in the graph (graph node id).</summary>
        public event Action<string> NodeOpened;

        public GraphViewPanel()
        {
            Dock = DockStyle.Fill;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            if (!_initialized && !DesignMode)
            {
                _initialized = true;
                InitializeWebView();
            }
        }

        private async void InitializeWebView()
        {
            try
            {
                if (CoreWebView2Environment.GetAvailableBrowserVersionString() == null)
                {
                    ShowFallback();
                    return;
                }
            }
            catch (WebView2RuntimeNotFoundException)
            {
                ShowFallback();
                return;
            }

            try
            {
                var baseDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "MscrmTools", "XrmToolBox", "FieldGraphX");
                var webDir = ExtractWebAssets(baseDir);

                _webView = new WebView2 { Dock = DockStyle.Fill };
                Controls.Add(_webView);

                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: Path.Combine(baseDir, "WebView2"));
                await _webView.EnsureCoreWebView2Async(environment);

                _webView.CoreWebView2.SetVirtualHostNameToFolderMapping(
                    VirtualHost, webDir, CoreWebView2HostResourceAccessKind.Allow);
                _webView.CoreWebView2.WebMessageReceived += OnWebMessageReceived;
                _webView.CoreWebView2.NavigationCompleted += OnNavigationCompleted;
                _webView.CoreWebView2.Navigate($"https://{VirtualHost}/graph.html");
            }
            catch (Exception ex)
            {
                ShowFallback($"Could not initialize the graph view:\n{ex.Message}");
            }
        }

        private void OnNavigationCompleted(object sender, CoreWebView2NavigationCompletedEventArgs e)
        {
            if (e.IsSuccess && _pendingGraphJson != null)
            {
                var json = _pendingGraphJson;
                _pendingGraphJson = null;
                PushGraph(json);
            }
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            try
            {
                var message = JObject.Parse(e.WebMessageAsJson);
                var type = message["type"]?.ToString();
                var id = message["id"]?.ToString();

                if (type == "select" && id != null) NodeSelected?.Invoke(id);
                else if (type == "open" && id != null) NodeOpened?.Invoke(id);
            }
            catch
            {
                // Malformed message from the page — ignore.
            }
        }

        /// <summary>Renders a graph (JSON produced by VisJsGraphSerializer).</summary>
        public void ShowGraph(string visJson)
        {
            if (_webView?.CoreWebView2 == null)
            {
                _pendingGraphJson = visJson;
                return;
            }
            PushGraph(visJson);
        }

        /// <summary>Highlights the node in the graph (sync from TreeView selection).</summary>
        public void SelectNode(string nodeId)
        {
            if (_webView?.CoreWebView2 == null || nodeId == null) return;
            _ = _webView.CoreWebView2.ExecuteScriptAsync(
                $"selectNode({JToken.FromObject(nodeId).ToString(Newtonsoft.Json.Formatting.None)})");
        }

        private void PushGraph(string visJson)
        {
            // visJson is already valid JSON — inject it as an object literal.
            _ = _webView.CoreWebView2.ExecuteScriptAsync($"renderGraph({visJson})");
        }

        private void ShowFallback(string message = null)
        {
            _fallbackLabel = new Label
            {
                Dock = DockStyle.Fill,
                TextAlign = System.Drawing.ContentAlignment.MiddleCenter,
                Text = message ??
                    "The Microsoft Edge WebView2 Runtime is required for the graph view.\n" +
                    "Download: https://developer.microsoft.com/microsoft-edge/webview2/\n\n" +
                    "The tree view works without it."
            };
            Controls.Add(_fallbackLabel);
        }

        private static string ExtractWebAssets(string baseDir)
        {
            var assembly = Assembly.GetExecutingAssembly();
            // Version-stamped folder so plugin upgrades re-extract fresh assets.
            var version = assembly.GetName().Version?.ToString() ?? "0";
            var webDir = Path.Combine(baseDir, "web", version);
            Directory.CreateDirectory(webDir);

            ExtractResource(assembly, "FieldGraphX.Resources.graph.html",
                Path.Combine(webDir, "graph.html"));
            ExtractResource(assembly, "FieldGraphX.Resources.vis-network.min.js",
                Path.Combine(webDir, "vis-network.min.js"));

            return webDir;
        }

        private static void ExtractResource(Assembly assembly, string resourceName, string targetPath)
        {
            if (File.Exists(targetPath)) return;

            using (var stream = assembly.GetManifestResourceStream(resourceName))
            {
                if (stream == null)
                    throw new InvalidOperationException($"Embedded resource '{resourceName}' not found.");
                using (var file = File.Create(targetPath))
                    stream.CopyTo(file);
            }
        }
    }
}
