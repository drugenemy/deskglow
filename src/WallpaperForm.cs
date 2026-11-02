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
