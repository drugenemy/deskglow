using static Deskglow.Native;

namespace Deskglow;

/// <summary>
/// Puts a window behind the desktop icons.
///
/// Explorer draws the icons in a "SHELLDLL_DefView" window. Sending the
/// undocumented message 0x052C to "Progman" makes Explorer create a "WorkerW"
/// layer that sits between the static wallpaper and the icons. We parent our
/// window into that layer.
///
/// Two layouts exist:
///  - Windows 10 and Windows 11 before 24H2: the WorkerW is a top-level window
///    right after the top-level window that holds SHELLDLL_DefView.
///  - Windows 11 24H2 and later: the WorkerW and SHELLDLL_DefView are both
///    children of Progman. We parent into Progman and slot our window just below
///    SHELLDLL_DefView. In this layout the window must be layered or DWM skips it.
/// </summary>
internal sealed class DesktopHost
{
    public IntPtr Parent { get; private set; }
    public IntPtr InsertAfter { get; private set; }
    public bool IsNewLayout { get; private set; }

    public bool Find()
    {
        Parent = IntPtr.Zero;
        InsertAfter = IntPtr.Zero;

        var progman = FindWindow("Progman", null);
        if (progman == IntPtr.Zero) return false;

        SendMessageTimeout(progman, 0x052C, new IntPtr(0xD), new IntPtr(0x1), SMTO_NORMAL, 1000, out _);
        SendMessageTimeout(progman, 0x052C, IntPtr.Zero, IntPtr.Zero, SMTO_NORMAL, 1000, out _);

        // New layout (Windows 11 24H2+): WorkerW is a child of Progman.
        var childWorker = FindWindowEx(progman, IntPtr.Zero, "WorkerW", null);
        if (childWorker != IntPtr.Zero)
        {
            IsNewLayout = true;
            Parent = progman;
            InsertAfter = FindWindowEx(progman, IntPtr.Zero, "SHELLDLL_DefView", null);
            return true;
        }

        // Classic layout: find the top-level window holding the icons,
        // then the WorkerW that comes right after it.
        IntPtr worker = IntPtr.Zero;
        EnumWindows((top, _) =>
        {
            var defView = FindWindowEx(top, IntPtr.Zero, "SHELLDLL_DefView", null);
            if (defView != IntPtr.Zero)
            {
                worker = FindWindowEx(IntPtr.Zero, top, "WorkerW", null);
            }
            return true;
        }, IntPtr.Zero);

        if (worker == IntPtr.Zero) return false;
        IsNewLayout = false;
        Parent = worker;
        InsertAfter = IntPtr.Zero;
        return true;
    }

    /// <summary>Parents the window into the desktop and stretches it over every monitor.</summary>
    public void Attach(IntPtr window, bool layeredOnNewLayout)
    {
        var ex = GetWindowLong(window, GWL_EXSTYLE);
        ex = (ex | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE) & ~WS_EX_APPWINDOW;
        if (IsNewLayout && layeredOnNewLayout) ex |= WS_EX_LAYERED;
        SetWindowLong(window, GWL_EXSTYLE, ex);
        if (IsNewLayout && layeredOnNewLayout) SetLayeredWindowAttributes(window, 0, 255, LWA_ALPHA);

        SetParent(window, Parent);
        Fit(window);
    }

    /// <summary>Re-sizes the window to cover the whole virtual screen (all monitors).</summary>
    public void Fit(IntPtr window)
    {
        var vs = SystemInformation.VirtualScreen;
        var r = new RECT { Left = vs.Left, Top = vs.Top, Right = vs.Right, Bottom = vs.Bottom };
        MapWindowPoints(IntPtr.Zero, Parent, ref r, 2);
        SetWindowPos(window, InsertAfter, r.Left, r.Top, r.Right - r.Left, r.Bottom - r.Top,
            SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    /// <summary>Makes Explorer redraw the normal wallpaper, so nothing stale is left after we quit.</summary>
    public static void RefreshStaticWallpaper()
    {
        var sb = new System.Text.StringBuilder(1024);
        if (SystemParametersInfo(SPI_GETDESKWALLPAPER, (uint)sb.Capacity, sb, 0))
        {
            SystemParametersInfo(SPI_SETDESKWALLPAPER, 0, sb, SPIF_UPDATEINIFILE);
        }
    }

    /// <summary>True if the window at the cursor belongs to the desktop (wallpaper or icon layer).</summary>
    public static bool IsDesktopWindow(IntPtr hwnd, IntPtr ours)
    {
        if (hwnd == IntPtr.Zero) return false;
        var root = GetAncestor(hwnd, 2 /* GA_ROOT */);
        if (root == ours) return true;
        var cls = ClassOf(root);
        return cls == "Progman" || cls == "WorkerW";
    }
}
