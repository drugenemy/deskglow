using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;
using static Deskglow.Native;

namespace Deskglow;

/// <summary>The window that lives behind the desktop icons and shows one HTML wallpaper.</summary>
internal sealed class WallpaperForm : Form
{
    const string Host = "deskglow.wall";

    readonly WebView2 web = new() { Dock = DockStyle.Fill, DefaultBackgroundColor = Color.Black };
    bool ready;
    string? pendingFolder;
    bool paused;

    public WallpaperForm()
    {
        FormBorderStyle = FormBorderStyle.None;
        ShowInTaskbar = false;
        StartPosition = FormStartPosition.Manual;
        BackColor = Color.Black;
        Bounds = SystemInformation.VirtualScreen;
        Text = "Deskglow";
        Controls.Add(web);
    }

    protected override bool ShowWithoutActivation => true;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            cp.ExStyle |= WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE;
            return cp;
        }
    }

    public async Task InitAsync()
    {
        Directory.CreateDirectory(Config.Dir);
        var options = new CoreWebView2EnvironmentOptions("--autoplay-policy=no-user-gesture-required");
        var env = await CoreWebView2Environment.CreateAsync(null, Path.Combine(Config.Dir, "WebView2"), options);
        await web.EnsureCoreWebView2Async(env);

        var s = web.CoreWebView2.Settings;
        s.AreDefaultContextMenusEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;

        await web.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(Bridge.Script);
        web.CoreWebView2.WebMessageReceived += OnPageMessage;

        ready = true;
        if (pendingFolder != null) LoadWallpaper(pendingFolder);
    }

    /// <summary>Loads index.html from the given wallpaper folder.</summary>
    public void LoadWallpaper(string folder)
    {
        if (!ready) { pendingFolder = folder; return; }
        pendingFolder = null;
        var core = web.CoreWebView2;
        try { core.ClearVirtualHostNameToFolderMapping(Host); } catch { }
        core.SetVirtualHostNameToFolderMapping(Host, folder, CoreWebView2HostResourceAccessKind.Allow);
        core.Navigate($"https://{Host}/index.html?t={DateTime.Now.Ticks}");
    }

    public void Reload()
    {
        if (ready) web.CoreWebView2.Reload();
    }

    public void OpenDevTools()
    {
        if (ready) web.CoreWebView2.OpenDevToolsWindow();
    }

    /// <summary>Sends a JSON message to the page. The bridge script turns it into DOM events.</summary>
    public void Post(string json)
    {
        if (ready && !paused) web.CoreWebView2.PostWebMessageAsJson(json);
    }

    public bool IsPaused => paused;

    // ── lock screen ──────────────────────────────────────────────────────────

    TaskCompletionSource<string?>? lockTcs;
    int lockW, lockH;
    string lockData = "{}";

    /// <summary>
    /// Asks the page for a lock screen image (JPEG data URL) of the given size with widgets drawn on it.
    /// Returns null if the page is paused or does not answer in time.
    /// </summary>
    public async Task<string?> RenderLockAsync(int w, int h, string dataJson, int timeoutMs = 20000)
    {
        if (!ready || paused) return null;
        lockTcs = new TaskCompletionSource<string?>(TaskCreationOptions.RunContinuationsAsynchronously);
        lockW = w; lockH = h; lockData = dataJson;
        web.CoreWebView2.PostWebMessageAsJson($"{{\"type\":\"lock\",\"w\":{w},\"h\":{h},\"data\":{dataJson}}}");
        var done = await Task.WhenAny(lockTcs.Task, Task.Delay(timeoutMs));
        return done == lockTcs.Task ? lockTcs.Task.Result : null;
    }

    void OnPageMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        try
        {
            var msg = System.Text.Json.Nodes.JsonNode.Parse(e.WebMessageAsJson);
            switch ((string?)msg?["type"])
            {
                case "lock-image": lockTcs?.TrySetResult((string?)msg?["data"]); break;
                case "lock-error": lockTcs?.TrySetResult(null); break;
                case "lock-need-bg": _ = SendScreenshotAsync(); break;
            }
        }
        catch { }
    }

    /// <summary>For wallpapers without their own lock background: screenshot the primary monitor part and send it back.</summary>
    async Task SendScreenshotAsync()
    {
        try
        {
            using var ms = new MemoryStream();
            await web.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, ms);
            ms.Position = 0;
            using var full = new Bitmap(ms);
            var vs = SystemInformation.VirtualScreen;
            var pb = Screen.PrimaryScreen?.Bounds ?? vs;
            double sx = full.Width / (double)vs.Width, sy = full.Height / (double)vs.Height;
            var rect = new Rectangle((int)((pb.X - vs.X) * sx), (int)((pb.Y - vs.Y) * sy), (int)(pb.Width * sx), (int)(pb.Height * sy));
            rect.Intersect(new Rectangle(0, 0, full.Width, full.Height));
            using var crop = full.Clone(rect, full.PixelFormat);
            using var outMs = new MemoryStream();
            crop.Save(outMs, System.Drawing.Imaging.ImageFormat.Jpeg);
            var bg = "data:image/jpeg;base64," + Convert.ToBase64String(outMs.ToArray());
            var json = new System.Text.Json.Nodes.JsonObject
            {
                ["type"] = "lock", ["w"] = lockW, ["h"] = lockH, ["bg"] = bg,
                ["data"] = System.Text.Json.Nodes.JsonNode.Parse(lockData),
            };
            web.CoreWebView2.PostWebMessageAsJson(json.ToJsonString());
        }
        catch
        {
            lockTcs?.TrySetResult(null);
        }
    }

    public async void SetPaused(bool value)
    {
        if (!ready || paused == value) return;
        if (value)
        {
            web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"pause\"}");
            paused = true;
            web.Visible = false;
            try { await web.CoreWebView2.TrySuspendAsync(); } catch { }
        }
        else
        {
            paused = false;
            try { web.CoreWebView2.Resume(); } catch { }
            web.Visible = true;
            web.CoreWebView2.PostWebMessageAsJson("{\"type\":\"resume\"}");
        }
    }
}
