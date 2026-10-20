/*Copyright © SLT Softwares. All rights reserved.
Use of this source code is governed by a GNU license that can be found in the LICENSE file.*/

using Microsoft.Web.WebView2.Core;
using Microsoft.Win32;
using SLBr.Controls;
using SLBr.Protocols;
using System.Buffers;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.Text;
using System.Windows;
using System.Windows.Input;

namespace SLBr.WebView
{
    public static class WebViewManager
    {
        public static CoreWebView2Environment WebView2Environment { get; set; }
        public static CoreWebView2ControllerOptions WebView2ControllerOptions { get; set; }
        public static CoreWebView2ControllerOptions WebView2PrivateControllerOptions { get; set; }
        public static CoreWebView2FindOptions WebView2FindOptions { get; set; }

                                                                
        public static List<IWebView> WebViews = [];
        
        public static WebViewSettings Settings { get; set; }
        public static WebViewRuntimeSettings RuntimeSettings { get; } = new();

        public static WebDownloadManager DownloadManager { get; } = new();

        public static bool IsWebView2Initialized { get; private set; } = false;
                public static bool IsTridentInitialized { get; private set; } = false;

        public static string WebView2Version { get; private set; } = "";

        public static async Task<IWebView> Create(WebEngineType EngineType, List<WebNavigationEntry> Urls, WebViewBrowserSettings _BrowserSettings)

        {

            switch (EngineType)

            {

                case WebEngineType.ChromiumEdge:

                    if (!IsWebView2Initialized)

                    {

                        try { WebView2Version = CoreWebView2Environment.GetAvailableBrowserVersionString(); }

                        catch (WebView2RuntimeNotFoundException) { }

                    }

                    ChromiumEdgeWebView EView = new(Urls, _BrowserSettings);

                    await EView.InitializeAsync();

                    return EView;

                case WebEngineType.Trident:

                    return new TridentWebView(Urls, _BrowserSettings);

                default:

                    ChromiumEdgeWebView DefView = new(Urls, _BrowserSettings);

                    await DefView.InitializeAsync();

                    return DefView;

            }

        }



        public static string? WebView2ExtensionsPath;
        public static string? WebView2UnpackedExtensionsPath;

        private static Task<bool>? WebView2InitializeTask;
        private static readonly Lock WebView2InitializeLock = new();

        public static Task<bool> InitializeWebView2()
        {
            lock (WebView2InitializeLock)
            {
                if (WebView2InitializeTask != null)
                    return WebView2InitializeTask;

                WebView2InitializeTask = InitializeWebView2Internal();
                return WebView2InitializeTask;
            }
        }

        private static async Task<bool> InitializeWebView2Internal()
        {
            if (IsWebView2Initialized)
                return true;
            if (Settings.UserDataPath != null)
                WebView2ExtensionsPath = Path.Combine(Settings.UserDataPath, "EBWebView", "Default", "Extensions");

            if (Settings.ExtensionsPath != null)
            {
                WebView2UnpackedExtensionsPath = Path.Combine(Settings.ExtensionsPath, "WebView2");
                if (!Directory.Exists(WebView2UnpackedExtensionsPath))
                    Directory.CreateDirectory(WebView2UnpackedExtensionsPath);
            }
            //https://learn.microsoft.com/en-us/microsoft-edge/webview2/concepts/webview-features-flags
            //msWebView2TreatAppSuspendAsDeviceSuspend
            List<CoreWebView2CustomSchemeRegistration> CustomSchemeRegistrations = [];
            foreach (var Scheme in Settings.Schemes.Where(i => i.Key != "*"))
                CustomSchemeRegistrations.Add(new(Scheme.Key) { HasAuthorityComponent = true, TreatAsSecure = true });

            CoreWebView2EnvironmentOptions EnvironmentOptions = new(Settings.BuildFlags(true), Settings.Language, null, false, CustomSchemeRegistrations);
            try { WebView2Version = CoreWebView2Environment.GetAvailableBrowserVersionString(null, EnvironmentOptions); }
            catch (WebView2RuntimeNotFoundException) { return false; }

            EnvironmentOptions.AreBrowserExtensionsEnabled = true;
            EnvironmentOptions.IsCustomCrashReportingEnabled = true;
            EnvironmentOptions.ScrollBarStyle = CoreWebView2ScrollbarStyle.FluentOverlay;

            WebView2Environment = await CoreWebView2Environment.CreateAsync(null, Settings.UserDataPath, EnvironmentOptions);

            WebView2ControllerOptions = WebView2Environment.CreateCoreWebView2ControllerOptions();
            if (Settings.UserDataPath == null)
                WebView2ControllerOptions.IsInPrivateModeEnabled = true;
            WebView2ControllerOptions.DefaultBackgroundColor = Color.Black;
            //WebView2ControllerOptions.ProfileName = "Default";

            //WARNING: Enabling AllowHostInputProcessing freezes WebView2 on drag & drop.
            //Related? https://github.com/MicrosoftEdge/WebView2Feedback/issues/5141
            //To no avail https://gist.github.com/ivanjx/b026ba331796e20a717778ae56760e3c, Disabling AllowExternalDrop may help, investigate?

            //Ignore above, keyboard hotkeys remain functional on disabled AllowHostInputProcessing.
            WebView2ControllerOptions.AllowHostInputProcessing = false;

            WebView2PrivateControllerOptions = WebView2Environment.CreateCoreWebView2ControllerOptions();
            WebView2PrivateControllerOptions.IsInPrivateModeEnabled = true;
            WebView2PrivateControllerOptions.DefaultBackgroundColor = Color.Black;
            WebView2PrivateControllerOptions.AllowHostInputProcessing = false;

            WebView2FindOptions = WebView2Environment.CreateFindOptions();
            WebView2FindOptions.ShouldHighlightAllMatches = true;
            WebView2FindOptions.ShouldMatchWord = false;
            WebView2FindOptions.SuppressDefaultFindDialog = true;
            IsWebView2Initialized = true;
            await InstallWebView2Extensions();
            return true;
        }
        public static void DeleteWebView2HighDPIRegistry()
        {
            try
            {
                using (RegistryKey? Key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers", true))
                {
                    if (Key == null)
                        return;
                    string[] ValueNames = Key.GetValueNames();
                    foreach (string ValueName in ValueNames)
                    {
                        if (ValueName.EndsWith("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase))
                        {
                            object Value = Key.GetValue(ValueName);
                            if (Value != null && Value.ToString().Contains("HIGHDPIAWARE"))
                                Key.DeleteValue(ValueName);
                        }
                    }
                }
            }
            catch { }
        }
        public static CoreWebView2 HeadlessEdgeCore;
        public static CoreWebView2Controller HeadlessEdgeController;
        public static async Task CreateHeadlessWebView2()
        {
            HeadlessEdgeController = await WebView2Environment.CreateCoreWebView2ControllerAsync(DllUtils.GetDesktopWindow(), WebView2ControllerOptions);
            if (HeadlessEdgeController != null && HeadlessEdgeController.CoreWebView2 != null)
            {
                IntPtr HWND = HeadlessEdgeController.ParentWindow;
                if (HWND != IntPtr.Zero)
                {
                    int ExStyle = DllUtils.GetWindowLong(HWND, DllUtils.GWL_EXSTYLE);

                    ExStyle &= ~DllUtils.WS_EX_APPWINDOW;
                    ExStyle |= DllUtils.WS_EX_TOOLWINDOW;

                    DllUtils.SetWindowLong(HWND, DllUtils.GWL_EXSTYLE, ExStyle);
                }
                HeadlessEdgeController.IsVisible = false;
                HeadlessEdgeController.Bounds = new Rectangle(0, 0, 0, 0);

                HeadlessEdgeCore = HeadlessEdgeController.CoreWebView2;
            }
        }

        public static async Task CheckHeadlessWebView2Availability()
        {
            if (HeadlessEdgeCore == null || HeadlessEdgeController == null)
                await CreateHeadlessWebView2();

            try
            {
                _ = HeadlessEdgeCore.Profile;
            }
            catch (InvalidOperationException)
            {
                HeadlessEdgeController?.Close();
                await CreateHeadlessWebView2();
            }
        }

        public static async Task InstallWebView2Extensions()
        {
            if (WebView2Environment == null) return;
            await CheckHeadlessWebView2Availability();

            CoreWebView2Profile Profile = HeadlessEdgeCore.Profile;
            if (!string.IsNullOrEmpty(WebView2ExtensionsPath) && !string.IsNullOrEmpty(WebView2UnpackedExtensionsPath))
            {
                List<string> ExtensionsDirectory = [];
                if (Directory.Exists(WebView2ExtensionsPath))
                    ExtensionsDirectory.AddRange(Directory.GetDirectories(WebView2ExtensionsPath));
                if (Directory.Exists(WebView2UnpackedExtensionsPath))
                    ExtensionsDirectory.AddRange(Directory.GetDirectories(WebView2UnpackedExtensionsPath));
                foreach (string ProfilePath in ExtensionsDirectory)
                {
                    string ExtensionPath = ProfilePath;
                    if (!File.Exists(Path.Combine(ProfilePath, "manifest.json")))
                        ExtensionPath = Directory.GetDirectories(ProfilePath).FirstOrDefault(i => File.Exists(Path.Combine(i, "manifest.json"))) ?? ProfilePath;
                    if (ExtensionPath != null)
                    {
                        try
                        {
                            await Profile.AddBrowserExtensionAsync(ExtensionPath);
                        }
                        catch { }
                    }
                }
                await App.Instance.ExtensionManager.LoadWebView2Extensions();
            }
        }
        public static void ShutdownWebView2()
        {
            HeadlessEdgeController?.Close();
        }

        public static void InitializeTrident()
        {
            if (IsTridentInitialized)
                return;
            SetIEFeatureControlKey("FEATURE_BROWSER_EMULATION", (uint)Settings.TridentVersion);
            SetIEFeatureControlKey("FEATURE_GPU_RENDERING", (uint)(Settings.GPUAcceleration ? 1 : 0));
            SetIEFeatureControlKey("FEATURE_ALLOW_HIGHFREQ_TIMERS", (uint)(Settings.Performance != PerformancePreset.Low ? 1 : 0));

            //SetIEFeatureControlKey("FEATURE_ENABLE_CLIPCHILDREN_OPTIMIZATION", 1);
            SetIEFeatureControlKey("FEATURE_AJAX_CONNECTIONEVENTS", 1);
            SetIEFeatureControlKey("FEATURE_NINPUT_LEGACYMODE", 0);

            SetIEFeatureControlKey("FEATURE_SCRIPTURL_MITIGATION", 1);

            SetIEFeatureControlKey("FEATURE_BLOCK_LMZ_IMG", 1);
            SetIEFeatureControlKey("FEATURE_BLOCK_LMZ_OBJECT", 1);
            SetIEFeatureControlKey("FEATURE_BLOCK_LMZ_SCRIPT", 1);

            SetIEFeatureControlKey("FEATURE_SPELLCHECKING", (uint)(RuntimeSettings.SpellCheck ? 1 : 0));
            SetIEFeatureControlKey("FEATURE_VIEWLINKEDWEBOC_IS_UNSAFE", 1);
            SetIEFeatureControlKey("FEATURE_BLOCK_CROSS_PROTOCOL_FILE_NAVIGATION", 1);
            SetIEFeatureControlKey("FEATURE_RESTRICT_ABOUT_PROTOCOL_IE7", 1);
            SetIEFeatureControlKey("FEATURE_SHOW_APP_PROTOCOL_WARN_DIALOG", 1);
            SetIEFeatureControlKey("FEATURE_IFRAME_MAILTO_THRESHOLD", 1);
            SetIEFeatureControlKey("FEATURE_LOCALMACHINE_LOCKDOWN", 1);
            //SetIEFeatureControlKey("FEATURE_MIME_HANDLING", 1);
            //SetIEFeatureControlKey("FEATURE_RESTRICT_ACTIVEXINSTALL", 1);

            SetIEFeatureControlKey("FEATURE_UNC_SAVEDFILECHECK", 1);
            SetIEFeatureControlKey("FEATURE_DISABLE_TELNET_PROTOCOL", 1);
            IsTridentInitialized = true;
        }
        private static void SetIEFeatureControlKey(string Feature, uint Value)
        {
            using (var Key = Registry.CurrentUser.CreateSubKey(string.Concat(@"Software\Microsoft\Internet Explorer\Main\FeatureControl\", Feature), RegistryKeyPermissionCheck.ReadWriteSubTree))
            {
                Key?.SetValue("SLBr.exe", Value, RegistryValueKind.DWord);
                Key?.Close();
            }
        }

        public static async Task<ProtocolResponse> GopherHandler(string Url, string Extra = "", CancellationToken? Token = null)
        {
            try
            {
                GeminiGopherIResponse Response = await Gopher.Fetch(new Uri(Url), CancellationToken: Token ?? CancellationToken.None);
                if (Response.Mime.Contains("application/gopher-menu"))
                    return ProtocolResponse.FromString(TextGopher.NewFormat(Response), "text/html", Response.StatusCode, Response.ErrorCode);
                else
                    return ProtocolResponse.FromBytes(Response.Bytes.ToArray(), Response.Mime, Response.StatusCode, Response.ErrorCode);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception _Exception)
            {
                return ProtocolResponse.FromString($"<h1>Gopher Error</h1><pre>{_Exception.Message}</pre>", "text/html", 0, WebErrorCode.Failed);
            }
        }
        public static async Task<ProtocolResponse> GeminiHandler(string Url, string Extra = "", CancellationToken? Token = null)
        {
            try
            {
                GeminiGopherIResponse Response = await Gemini.Fetch(new Uri(Url), CancellationToken: Token ?? CancellationToken.None);
                if (Response.Mime.Contains("text/gemini"))
                    return ProtocolResponse.FromString(TextGemini.NewFormat(Response), "text/html", Response.StatusCode, Response.ErrorCode);
                else
                    return ProtocolResponse.FromBytes(Response.Bytes.ToArray(), Response.Mime, Response.StatusCode, Response.ErrorCode);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception _Exception)
            {
                return ProtocolResponse.FromString($"<h1>Gemini Error</h1><pre>{_Exception.Message}</pre>", "text/html", 0, WebErrorCode.Failed);
            }
            /*if (Response.SSLStatus.X509Certificate != null)
            {
                MessageBox.Show(Response.SSLStatus.X509Certificate.Subject);
                MessageBox.Show(Response.SSLStatus.X509Certificate.Issuer);
                MessageBox.Show(Response.SSLStatus.X509Certificate.NotBefore.Date.ToShortDateString());
                MessageBox.Show(Response.SSLStatus.X509Certificate.NotAfter.Date.ToShortDateString());
            }*/
        }
        private static Lazy<string[]> SLBrURLs = new(() => ["credits", "newtab", "downloads", "history", "settings", "tetris", "favourites"]);
        public static async Task<ProtocolResponse> SLBrHandler(string Url, string Extra = "", CancellationToken? Token = null)
        {
            try
            {
                string Host = Utils.FastHost(Url);
                if (SLBrURLs.Value.Contains(Host))
                {
                    string Page = Url[(7 + Host.Length)..].TrimStart('/');
                    string FileName = string.IsNullOrWhiteSpace(Page) ? $"{Host}.html" : Page;
                    if (string.IsNullOrWhiteSpace(Page) && Host == "newtab")
                    {
                        if (App.Instance.ReadOnlyInstance)
                            FileName = "guest.html";
                        else if (Extra == "1")
                            FileName = "private.html";
                    }
                    string FilePath = Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "Resources", FileName);
                    if (File.Exists(FilePath))
                        return ProtocolResponse.FromBytes(File.ReadAllBytes(FilePath), Utils.GetMimeType(Path.GetExtension(FilePath)), 200);
                    else if (App.CustomPageOverlays.TryGetValue(Host, out _))
                        return ProtocolResponse.FromString(string.Format(App.OverlayPagePlaceholder, Host.ToTitleCase()), "text/html", 200);
                }
                return ProtocolResponse.FromString($"<h1>404 Not Found</h1>", "text/html", 404);
            }
            catch (Exception ex)
            {
                return ProtocolResponse.FromString($"<h1>Error</h1><pre>{WebUtility.HtmlEncode(ex.Message)}</pre>", "text/html", 0, WebErrorCode.Failed);
            }
        }
        public static ProtocolResponse OverrideHandler(string Url, string Extra = "")
        {
            if (OverrideRequests.TryGetValue(Url, out RequestOverrideItem Item))
            {
                if (Item.Uses != -1)
                {
                    Item.Uses -= 1;
                    if (Item.Uses == 0)
                        OverrideRequests.Remove(Url, out Item);
                }
                return ProtocolResponse.FromBytes(Item.Data, Item.MimeType);
            }
            return null;
        }

        public static bool RegisterOverrideRequest(string Url, byte[] Data, string MimeType = "text/html"/*, bool limitedUse = false*/, int Uses = 1, int Error = -1)
        {
            if (Uri.TryCreate(Url, UriKind.Absolute, out Uri? URI))
            {
                RequestOverrideItem Entry = new(Data, MimeType, Uses, Error);
                OverrideRequests.AddOrUpdate(URI.AbsoluteUri, Entry, (k, v) => Entry);
                return true;
            }
            return false;
        }

        public static bool UnregisterOverrideRequest(string Url) => OverrideRequests.TryRemove(Url, out _);
        public static ConcurrentDictionary<string, RequestOverrideItem> OverrideRequests = new(StringComparer.OrdinalIgnoreCase);
    }

    public class RequestOverrideItem(byte[] _Data, string _MimeType, int _Uses = 1, int _Error = -1)
    {
        public byte[] Data = _Data;
        public string MimeType = _MimeType;
        public int Error = _Error;
        public int Uses = _Uses;
    }

    //https://learn.microsoft.com/en-us/previous-versions/windows/internet-explorer/ie-developer/general-info/ee330720(v=vs.85)
    public enum TridentEmulationVersion: uint
    {
        IE7 = 7000,
        IE8 = 8888,
        IE9 = 9999,
        IE10 = 10001,
        IE11 = 11001,
        Edge = 12001
    }
    public enum PerformancePreset
    {
        Low,
        Default,
        High
    }

    public class WebViewSettings
    {
        public string Language;
        public string[] Languages = [];

        public string? UserDataPath = null;
        public string? ExtensionsPath = null;
        public string LogFile;

        public PerformancePreset Performance = PerformancePreset.Default;
        public TridentEmulationVersion TridentVersion = TridentEmulationVersion.IE11;
        
        public string JavaScriptFlags = string.Empty;
        public Dictionary<string, string> Flags = [];
        //https://www.chromium.org/developers/how-tos/run-chromium-with-flags/
        public string BuildFlags(bool IncludeJavaScript = false)
        {
            StringBuilder _StringBuilder = new();
            foreach (var Flag in Flags)
            {
                if (string.IsNullOrWhiteSpace(Flag.Value))
                    _StringBuilder.Append($"--{Flag.Key} ");
                else
                    _StringBuilder.Append($"--{Flag.Key}={Flag.Value.Replace("\"", "\\\"")} ");
            }
            if (IncludeJavaScript)
                _StringBuilder.Append($"--js-flags=\"{JavaScriptFlags.Replace("\"", "\\\"")}\"");
            return _StringBuilder.ToString().Trim();
        }
        public readonly Dictionary<string, ProtocolHandler> Schemes = [];
        public void RegisterProtocol(string Scheme, ProtocolHandler Handler) => Schemes[Scheme] = Handler;

        public void AddFlag(string Key, string Value) => Flags.Add(Key, Value);

        public void AddFlag(string Value) => Flags.Add(Value, string.Empty);

        public bool GPUAcceleration = true;
        /*public bool PrintPreview = true;*/
    }
    public class WebViewRuntimeSettings
    {
        public bool SpellCheck = true;

        //TODO: WebView2 Profile.DefaultDownloadFolderPath = DownloadFolderPath;
        public string DownloadFolderPath = string.Empty;
        public bool DownloadPrompt = true;
        public bool PDFViewer { get; set; } = true;

    }



    public class RelayCommand : ICommand
    {
        private readonly Action<object> CommandHandler;
        private readonly Func<object, bool> CanExecuteHandler;

        public event EventHandler CanExecuteChanged;

        public RelayCommand(Action<object> _CommandHandler, Func<object, bool> _CanExecuteHandler = null)
        {
            CommandHandler = _CommandHandler;
            CanExecuteHandler = _CanExecuteHandler;
        }
        public RelayCommand(Action _CommandHandler, Func<bool> _CanExecuteHandler = null) : this(_ => _CommandHandler(), _CanExecuteHandler == null ? null : new Func<object, bool>(_ => _CanExecuteHandler()))
        {
        }

        public void Execute(object parameter)
        {
            CommandHandler(parameter);
        }

        public bool CanExecute(object parameter)
        {
            return CanExecuteHandler == null || CanExecuteHandler(parameter);
        }

        public void RaiseCanExecuteChanged()
        {
            if (CanExecuteChanged != null)
                CanExecuteChanged(this, EventArgs.Empty);
        }
    }

    
    public class WebDownloadManager
    {
        public event Action<WebDownloadItem> DownloadStarted;
        public event Action<WebDownloadItem> DownloadUpdated;
        public event Action<WebDownloadItem> DownloadCompleted;

        public void Started(WebDownloadItem Item) => DownloadStarted?.RaiseUIAsync(Item);
        public void Updated(WebDownloadItem Item)
        {
            if (!Item.EndTime.HasValue && Item.State == WebDownloadState.InProgress && !App.Instance.LiteMode)
            {
                if (Item.TotalBytes <= 0 || Item.ReceivedBytes >= Item.TotalBytes)
                    Item.CalculatedEndTime = DateTime.Now;
                else
                {
                    DateTime Now = DateTime.Now;
                    double ElapsedSeconds = (Now - Item.LastCheckTime).TotalSeconds;
                    if (ElapsedSeconds > 0)
                    {
                        double BytesPerSecond = (Item.ReceivedBytes - Item.LastReceivedBytes) / ElapsedSeconds;
                        Item.LastCheckTime = Now;
                        Item.LastReceivedBytes = Item.ReceivedBytes;
                        if (BytesPerSecond > 0)
                            Item.CalculatedEndTime = DateTime.Now.AddSeconds((Item.TotalBytes - Item.ReceivedBytes) / BytesPerSecond);
                    }
                }
            }
            DownloadUpdated?.RaiseUIAsync(Item);
        }
        public void Completed(WebDownloadItem Item) => DownloadCompleted?.RaiseUIAsync(Item);

        public void RemoveFileStaging(WebDownloadItem Item)
        {
            if (File.Exists(Item.TempPath))
            {
                switch (Item.State)
                {
                    case WebDownloadState.Canceled:
                        try { File.Delete(Item.TempPath); } catch { }
                        break;
                    case WebDownloadState.Completed:
                        if (Item.TempPath != Item.FullPath)
                        {
                            try
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(Item.FullPath));
                                File.Move(Item.TempPath, Item.FullPath, true);
                            }
                            catch { }
                        }
                        break;
                }
            }
        }

        private static Lazy<HttpClient> DownloadHttpClient = new(() => HttpClientFactory.Create(new SocketsHttpHandler
        {
            AutomaticDecompression = DecompressionMethods.All,
            EnableMultipleHttp2Connections = true,
            EnableMultipleHttp3Connections = true,
            PooledConnectionLifetime = TimeSpan.FromMinutes(15),
            ConnectTimeout = TimeSpan.FromSeconds(30)
        }));

        public async Task StartDownloadAsync(string Url, string? TargetPath, bool ShowDialog, string? DialogFilter = null)
        {
            if (ShowDialog)
            {
                SaveFileDialog SaveDialog = new()
                {
                    FileName = Path.GetFileName(Url),
                    InitialDirectory = WebViewManager.RuntimeSettings.DownloadFolderPath,
                    //Guide on proper file dialog wild cards https://learn.microsoft.com/en-us/previous-versions/windows/silverlight/dotnet-windows-silverlight/dd459587(v=vs.95)
                    Filter = string.IsNullOrEmpty(DialogFilter) ? "All Files (*.*)|*.*" : DialogFilter
                };
                if (SaveDialog.ShowDialog() == true)
                    TargetPath = SaveDialog.FileName;
                else
                    return;
            }
            else
            {
                if (string.IsNullOrEmpty(TargetPath))
                    TargetPath = Path.Combine(WebViewManager.RuntimeSettings.DownloadFolderPath, Path.GetFileName(Url));
                else
                {
                    if (Directory.Exists(TargetPath) || !Path.HasExtension(TargetPath))
                        TargetPath = Path.Combine(TargetPath, Path.GetFileName(Url));
                }
            }

            string TempPath;
            if (Path.GetExtension(TargetPath) == ".crx")
                TempPath = TargetPath;
            else
                TempPath = TargetPath + ".part";

            WebDownloadItem Item = new()
            {
                Engine = WebEngineType.Trident,
                ID = Guid.NewGuid().ToString(),
                Url = Url,
                FileName = Path.GetFileName(TargetPath),
                FullPath = TargetPath,
                TempPath = TempPath,
                State = WebDownloadState.InProgress,
                Interruptible = true
            };

            CancellationTokenSource _CancellationTokenSource = new();
            ManualResetEventSlim PauseEvent = new(true);

            Item.Pause = () =>
            {
                if (Item.State == WebDownloadState.InProgress)
                {
                    Item.State = WebDownloadState.Paused;
                    PauseEvent.Reset();
                    Updated(Item);
                }
            };

            Item.Resume = () =>
            {
                if (Item.State == WebDownloadState.Paused || Item.State == WebDownloadState.Interrupted)
                {
                    Item.State = WebDownloadState.InProgress;
                    PauseEvent.Set();
                    Updated(Item);
                }
            };

            Item.Cancel = () =>
            {
                if (Item.State == WebDownloadState.InProgress || Item.State == WebDownloadState.Paused || Item.State == WebDownloadState.Interrupted)
                {
                    Item.State = WebDownloadState.Canceled;
                    _CancellationTokenSource.Cancel();
                }
            };

            Started(Item);

            try
            {
                long ExistingLength = 0;
                FileMode FileMode = FileMode.Create;

                if (File.Exists(TempPath))
                {
                    ExistingLength = new FileInfo(TempPath).Length;
                    if (ExistingLength > 0)
                    {
                        FileMode = FileMode.Append;
                        Item.ReceivedBytes = ExistingLength;
                    }
                }

                using HttpRequestMessage Request = new(HttpMethod.Get, Url);

                if (ExistingLength > 0)
                    Request.Headers.Range = new RangeHeaderValue(ExistingLength, null);

                using HttpResponseMessage Response = await DownloadHttpClient.Value.SendAsync(Request, HttpCompletionOption.ResponseHeadersRead, _CancellationTokenSource.Token);

                if (Response.StatusCode == HttpStatusCode.PartialContent)
                    Item.TotalBytes = (Response.Content.Headers.ContentLength ?? 0) + ExistingLength;
                else
                {
                    Response.EnsureSuccessStatusCode();
                    Item.TotalBytes = Response.Content.Headers.ContentLength ?? -1;
                    FileMode = FileMode.Create;
                    Item.ReceivedBytes = 0;
                }

                await using FileStream _FileStream = new(TempPath, FileMode, FileAccess.Write, FileShare.None);
                await using Stream _Stream = await Response.Content.ReadAsStreamAsync(_CancellationTokenSource.Token);

                var Buffer = new byte[8192];
                int Read;

                while (true)
                {
                    _CancellationTokenSource.Token.ThrowIfCancellationRequested();
                    PauseEvent.Wait(_CancellationTokenSource.Token);

                    Read = await _Stream.ReadAsync(Buffer, _CancellationTokenSource.Token);
                    if (Read <= 0) break;

                    await _FileStream.WriteAsync(Buffer.AsMemory(0, Read), _CancellationTokenSource.Token);
                    Item.ReceivedBytes += Read;
                    Updated(Item);
                }

                Item.State = WebDownloadState.Completed;
                Item.EndTime = DateTime.Now;
                Completed(Item);
            }
            /*catch (OperationCanceledException)
            {
                Item.State = WebDownloadState.Canceled;
                Completed(Item);
            }*/
            catch
            {
                Item.State = WebDownloadState.Canceled;
                Completed(Item);
            }
            finally
            {
                PauseEvent.Dispose();
                _CancellationTokenSource.Dispose();

                Item.Pause = null;
                Item.Resume = null;
                Item.Cancel = null;
            }
        }

        public void WriteDownload(byte[] Data, string TargetPath, bool ShowDialog, string? DialogFilter = null)
        {
            if (ShowDialog)
            {
                SaveFileDialog SaveDialog = new()
                {
                    FileName = Path.GetFileName(TargetPath),
                    InitialDirectory = Path.GetDirectoryName(TargetPath),
                    //Guide on proper file dialog wild cards https://learn.microsoft.com/en-us/previous-versions/windows/silverlight/dotnet-windows-silverlight/dd459587(v=vs.95)
                    Filter = string.IsNullOrEmpty(DialogFilter) ? "All Files (*.*)|*.*" : DialogFilter
                };
                if (SaveDialog.ShowDialog() == true)
                    TargetPath = SaveDialog.FileName;
                else
                    return;
            }

            string TempPath;
            if (Path.GetExtension(TargetPath) == ".crx")
                TempPath = TargetPath;
            else
                TempPath = TargetPath + ".part";

            WebDownloadItem Item = new()
            {
                Engine = WebEngineType.Trident,
                ID = Guid.NewGuid().ToString(),
                Url = string.Empty,
                FileName = Path.GetFileName(TargetPath),
                FullPath = TargetPath,
                TempPath = TempPath,
                State = WebDownloadState.InProgress,
                TotalBytes = Data.Length,
                ReceivedBytes = Data.Length,
                Interruptible = false
            };
            Started(Item);
            if (Data != null)
            {
                try
                {
                    string? _Directory = Path.GetDirectoryName(TempPath);
                    if (!Directory.Exists(_Directory))
                        Directory.CreateDirectory(_Directory);
                    File.WriteAllBytes(TempPath, Data);
                    Item.State = WebDownloadState.Completed;
                }
                catch
                {
                    //TODO: Interrupt reason.
                    Item.State = WebDownloadState.Canceled;
                }
            }
            Completed(Item);
        }
    }

}

