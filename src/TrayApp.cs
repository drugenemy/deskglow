using Microsoft.Win32;
using static Deskglow.Native;

namespace Deskglow;

/// <summary>Owns the tray icon, the wallpaper window, the mouse feed and auto-pause.</summary>
internal sealed class TrayApp : ApplicationContext
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

    readonly Config cfg = Config.Load();
    readonly DesktopHost host = new();
    readonly NotifyIcon tray = new();
    readonly System.Windows.Forms.Timer mouseTimer = new() { Interval = 16 };
    readonly System.Windows.Forms.Timer pauseTimer = new() { Interval = 1000 };
    readonly ExplorerWatcher explorer;

    WallpaperForm? wall;
    bool userPaused;
    int lastX = int.MinValue, lastY = int.MinValue, lastButtons;

    static string WallpapersDir => Path.Combine(AppContext.BaseDirectory, "wallpapers");

    public TrayApp()
    {
        tray.Icon = SystemIcons.Application;
        tray.Text = "Deskglow";
        tray.Visible = true;
        tray.ContextMenuStrip = new ContextMenuStrip();
        tray.ContextMenuStrip.Opening += (_, _) => BuildMenu();
        tray.MouseClick += (_, e) => { if (e.Button == MouseButtons.Left) TogglePause(); };

        mouseTimer.Tick += (_, _) => FeedMouse();
        pauseTimer.Tick += (_, _) => CheckAutoPause();

        explorer = new ExplorerWatcher(() => Rebuild());
        SystemEvents.DisplaySettingsChanged += (_, _) => { if (wall != null) host.Fit(wall.Handle); };

        Rebuild();
        mouseTimer.Start();
        pauseTimer.Start();
    }

    /// <summary>Creates (or re-creates after an Explorer restart) the wallpaper window.</summary>
    async void Rebuild()
    {
        if (wall != null) { try { wall.Dispose(); } catch { } wall = null; }

        // Explorer may still be starting; retry for a few seconds.
        for (int i = 0; i < 20 && !host.Find(); i++) await Task.Delay(500);
        if (host.Parent == IntPtr.Zero)
        {
            tray.ShowBalloonTip(5000, "Deskglow", "Could not find the desktop layer.", ToolTipIcon.Error);
            return;
        }

        var form = new WallpaperForm();
        var handle = form.Handle; // create the window without showing it on top first
        host.Attach(handle, cfg.LayeredOnNewLayout);
        form.Show();
        host.Fit(handle);
        wall = form;

        await form.InitAsync();
        form.LoadWallpaper(ResolveWallpaper(cfg.Current));
    }

    string ResolveWallpaper(string name)
    {
        var dir = Path.Combine(WallpapersDir, name);
        if (File.Exists(Path.Combine(dir, "index.html"))) return dir;
        var first = ListWallpapers().FirstOrDefault();
        if (first != null) { cfg.Current = first; cfg.Save(); return Path.Combine(WallpapersDir, first); }
        return dir;
    }

    static IEnumerable<string> ListWallpapers() =>
        Directory.Exists(WallpapersDir)
            ? Directory.GetDirectories(WallpapersDir)
                .Where(d => File.Exists(Path.Combine(d, "index.html")))
                .Select(Path.GetFileName).OfType<string>().OrderBy(n => n)
            : Enumerable.Empty<string>();

    void BuildMenu()
    {
        var m = tray.ContextMenuStrip!;
        m.Items.Clear();

        var pick = new ToolStripMenuItem("Wallpaper");
        foreach (var name in ListWallpapers())
        {
            var item = new ToolStripMenuItem(name) { Checked = name == cfg.Current };
            item.Click += (_, _) =>
            {
                cfg.Current = name;
                cfg.Save();
                wall?.LoadWallpaper(Path.Combine(WallpapersDir, name));
            };
            pick.DropDownItems.Add(item);
        }
        if (pick.DropDownItems.Count == 0) pick.DropDownItems.Add(new ToolStripMenuItem("(no wallpapers found)") { Enabled = false });
        m.Items.Add(pick);

        m.Items.Add(new ToolStripMenuItem(userPaused ? "Resume" : "Pause", null, (_, _) => TogglePause()));
        m.Items.Add(new ToolStripMenuItem("Reload", null, (_, _) => wall?.Reload()));
        m.Items.Add(new ToolStripSeparator());

        m.Items.Add(new ToolStripMenuItem("Auto-pause when a window covers the screen", null, (_, _) =>
        {
            cfg.AutoPause = !cfg.AutoPause;
            cfg.Save();
        }) { Checked = cfg.AutoPause });
        m.Items.Add(new ToolStripMenuItem("Start with Windows", null, (_, _) => SetStartup(!IsStartup())) { Checked = IsStartup() });
        m.Items.Add(new ToolStripMenuItem("Open wallpapers folder", null, (_, _) =>
        {
            Directory.CreateDirectory(WallpapersDir);
            System.Diagnostics.Process.Start("explorer.exe", WallpapersDir);
        }));
        m.Items.Add(new ToolStripMenuItem("Developer tools", null, (_, _) => wall?.OpenDevTools()));
        m.Items.Add(new ToolStripMenuItem("Black screen fix (Win 11 24H2)", null, (_, _) =>
        {
            cfg.LayeredOnNewLayout = !cfg.LayeredOnNewLayout;
            cfg.Save();
            Rebuild();
        }) { Checked = cfg.LayeredOnNewLayout, ToolTipText = "Turn this off or on if the wallpaper stays black." });
        m.Items.Add(new ToolStripSeparator());
        m.Items.Add(new ToolStripMenuItem("Exit", null, (_, _) => Quit()));
    }

    void TogglePause()
    {
        userPaused = !userPaused;
        wall?.SetPaused(userPaused);
    }

    /// <summary>Sends cursor moves and clicks to the wallpaper, since the icon layer blocks real input.</summary>
    void FeedMouse()
    {
        if (wall == null || wall.IsPaused) return;
        if (!GetCursorPos(out var p)) return;

        var vs = SystemInformation.VirtualScreen;
        int x = p.X - vs.Left, y = p.Y - vs.Top;

        bool onDesktop = DesktopHost.IsDesktopWindow(WindowFromPoint(p), wall.Handle);
        int buttons = 0;
        if (onDesktop)
        {
            if ((GetAsyncKeyState(VK_LBUTTON) & 0x8000) != 0) buttons |= 1;
            if ((GetAsyncKeyState(VK_RBUTTON) & 0x8000) != 0) buttons |= 2;
        }

        if (x != lastX || y != lastY)
        {
            wall.Post($"{{\"type\":\"mouse\",\"kind\":\"move\",\"x\":{x},\"y\":{y},\"buttons\":{buttons}}}");
            lastX = x; lastY = y;
        }

        for (int bit = 0; bit < 2; bit++)
        {
            int mask = 1 << bit;
            bool now = (buttons & mask) != 0, before = (lastButtons & mask) != 0;
            if (now == before) continue;
            var kind = now ? "down" : "up";
            wall.Post($"{{\"type\":\"mouse\",\"kind\":\"{kind}\",\"x\":{x},\"y\":{y},\"button\":{(bit == 0 ? 0 : 2)},\"buttons\":{buttons}}}");
        }
        lastButtons = buttons;
    }

    /// <summary>Pauses the wallpaper while a maximized or fullscreen window hides it (single monitor only for now).</summary>
    void CheckAutoPause()
    {
        if (wall == null || userPaused) return;
        bool covered = false;
        if (cfg.AutoPause && Screen.AllScreens.Length == 1)
        {
            var fg = GetForegroundWindow();
            if (fg != IntPtr.Zero && IsWindowVisible(fg))
            {
                var cls = ClassOf(fg);
                if (cls is not ("Progman" or "WorkerW" or "Shell_TrayWnd") && GetWindowRect(fg, out var r))
                {
                    var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
                    if (GetMonitorInfo(MonitorFromWindow(fg, MONITOR_DEFAULTTONEAREST), ref mi))
                    {
                        var w = mi.rcWork;
                        covered = r.Left <= w.Left && r.Top <= w.Top && r.Right >= w.Right && r.Bottom >= w.Bottom;
                    }
                }
            }
        }
        if (covered != wall.IsPaused) wall.SetPaused(covered);
    }

    static bool IsStartup()
    {
        using var k = Registry.CurrentUser.OpenSubKey(RunKey);
        return k?.GetValue("Deskglow") != null;
    }

    static void SetStartup(bool on)
    {
        using var k = Registry.CurrentUser.CreateSubKey(RunKey);
        if (on) k.SetValue("Deskglow", $"\"{Environment.ProcessPath}\"");
        else k.DeleteValue("Deskglow", false);
    }

    void Quit()
    {
        mouseTimer.Stop();
        pauseTimer.Stop();
        tray.Visible = false;
        wall?.Dispose();
        explorer.DestroyHandle();
        DesktopHost.RefreshStaticWallpaper();
        ExitThread();
    }

    /// <summary>Hidden window that hears "TaskbarCreated", which Windows broadcasts when Explorer restarts.</summary>
    sealed class ExplorerWatcher : NativeWindow
    {
        readonly int taskbarCreated = RegisterWindowMessage("TaskbarCreated");
        readonly Action onRestart;

        public ExplorerWatcher(Action onRestart)
        {
            this.onRestart = onRestart;
            CreateHandle(new CreateParams());
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == taskbarCreated) onRestart();
            base.WndProc(ref m);
        }

        [System.Runtime.InteropServices.DllImport("user32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
        static extern int RegisterWindowMessage(string msg);
    }
}
