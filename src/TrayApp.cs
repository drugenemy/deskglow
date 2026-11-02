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
    readonly System.Windows.Forms.Timer lockTimer = new() { Interval = 30 * 60 * 1000 };
    readonly ExplorerWatcher explorer;
    readonly LockScreen lockScreen;

    WallpaperForm? wall;
    bool lockBusy;
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

        lockScreen = new LockScreen(cfg);
        lockTimer.Tick += (_, _) => UpdateLock(false);
        SystemEvents.SessionSwitch += OnSessionSwitch;

        Rebuild();
        mouseTimer.Start();
        pauseTimer.Start();
        lockTimer.Start();
    }

    // ── lock screen ──────────────────────────────────────────────────────────

    async void OnSessionSwitch(object? sender, SessionSwitchEventArgs e)
    {
        // Refresh right after unlock, so the image is fresh the next time the PC locks.
        if (e.Reason == SessionSwitchReason.SessionUnlock) { await Task.Delay(6000); UpdateLock(false); }
    }

    async void ScheduleLockUpdate(int delayMs = 6000)
    {
        if (!cfg.LockEnabled) return;
        await Task.Delay(delayMs);
        UpdateLock(false);
    }

    /// <summary>Renders the current wallpaper + widgets and sets it as the Windows lock screen.</summary>
    async void UpdateLock(bool manual)
    {
        if ((!cfg.LockEnabled && !manual) || lockBusy) return;
        if (wall == null) return;
        lockBusy = true;
        try
        {
            var data = await lockScreen.BuildDataAsync();
            var b = Screen.PrimaryScreen?.Bounds ?? new Rectangle(0, 0, 1920, 1080);
            var image = await wall.RenderLockAsync(b.Width, b.Height, data);
            if (image == null)
            {
                if (manual) tray.ShowBalloonTip(4000, "Deskglow", "Could not make the lock screen image. Is the wallpaper paused?", ToolTipIcon.Warning);
                return;
            }
            await lockScreen.ApplyAsync(image);
            if (manual) tray.ShowBalloonTip(3000, "Deskglow", "Lock screen updated. Press Win + L to see it.", ToolTipIcon.Info);
        }
        catch (Exception ex)
        {
            if (manual) tray.ShowBalloonTip(6000, "Deskglow", "Windows did not accept the lock screen image: " + ex.Message, ToolTipIcon.Error);
        }
        finally { lockBusy = false; }
    }

    ToolStripMenuItem BuildLockMenu()
    {
        var lk = new ToolStripMenuItem("Lock screen");
        lk.DropDownItems.Add(new ToolStripMenuItem("Use wallpaper as lock screen", null, (_, _) =>
        {
            cfg.LockEnabled = !cfg.LockEnabled;
            cfg.Save();
            if (cfg.LockEnabled) UpdateLock(true);
        }) { Checked = cfg.LockEnabled });
        lk.DropDownItems.Add(new ToolStripMenuItem("Update now", null, (_, _) => UpdateLock(true)));
        lk.DropDownItems.Add(new ToolStripSeparator());

        foreach (var (id, label) in new[] { ("weather", "Weather"), ("note", "Note"), ("year", "Year progress") })
        {
            lk.DropDownItems.Add(new ToolStripMenuItem(label, null, (_, _) =>
            {
                if (!cfg.LockWidgets.Remove(id)) cfg.LockWidgets.Add(id);
                cfg.Save();
                UpdateLock(false);
            }) { Checked = cfg.LockWidgets.Contains(id) });
        }
        lk.DropDownItems.Add(new ToolStripSeparator());

        var cityLabel = string.IsNullOrEmpty(cfg.CityName) ? "Set city for weather…" : $"City: {cfg.CityName}…";
        lk.DropDownItems.Add(new ToolStripMenuItem(cityLabel, null, async (_, _) =>
        {
            var city = Prompt.Ask("Weather city", "Which city? (for example: Tashkent)", cfg.City);
            if (string.IsNullOrWhiteSpace(city)) return;
            try
            {
                var found = await lockScreen.SetCityAsync(city.Trim());
                if (found == null) { tray.ShowBalloonTip(4000, "Deskglow", $"Could not find \"{city}\".", ToolTipIcon.Warning); return; }
                if (!cfg.LockWidgets.Contains("weather")) { cfg.LockWidgets.Insert(0, "weather"); cfg.Save(); }
                UpdateLock(true);
            }
            catch { tray.ShowBalloonTip(4000, "Deskglow", "No internet, could not look up the city.", ToolTipIcon.Warning); }
        }));
        lk.DropDownItems.Add(new ToolStripMenuItem(cfg.Fahrenheit ? "Units: °F" : "Units: °C", null, (_, _) =>
        {
            cfg.Fahrenheit = !cfg.Fahrenheit;
            cfg.Save();
            UpdateLock(false);
        }));
        lk.DropDownItems.Add(new ToolStripMenuItem("Write note…", null, (_, _) =>
        {
            var note = Prompt.Ask("Lock screen note", "A short note for your lock screen:", cfg.Note);
            if (note == null) return;
            cfg.Note = note.Trim();
            if (cfg.Note.Length > 0 && !cfg.LockWidgets.Contains("note")) cfg.LockWidgets.Add("note");
            cfg.Save();
            UpdateLock(true);
        }));
        return lk;
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
        ScheduleLockUpdate();
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
                ScheduleLockUpdate();
            };
            pick.DropDownItems.Add(item);
        }
        if (pick.DropDownItems.Count == 0) pick.DropDownItems.Add(new ToolStripMenuItem("(no wallpapers found)") { Enabled = false });
        m.Items.Add(pick);

        m.Items.Add(new ToolStripMenuItem(userPaused ? "Resume" : "Pause", null, (_, _) => TogglePause()));
        m.Items.Add(new ToolStripMenuItem("Reload", null, (_, _) => wall?.Reload()));
        m.Items.Add(BuildLockMenu());
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
        lockTimer.Stop();
        SystemEvents.SessionSwitch -= OnSessionSwitch;
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
