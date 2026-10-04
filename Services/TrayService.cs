using System.Runtime.InteropServices;

namespace KeyboardController.Services;

public class TrayService
{
    [DllImport("user32.dll")]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetConsoleWindow();

    private const int SW_HIDE = 0;
    private const int SW_SHOW = 5;

    private NotifyIcon? _notifyIcon;
    private IntPtr _consoleHandle;
    private bool _isConsoleVisible = true;
    private string _publicUrl = string.Empty;

    public void Initialize(string initialUrl)
    {
        _publicUrl = initialUrl;
        _consoleHandle = GetConsoleWindow();

        // Starty tray icon in a separate thread
        var trayThread = new Thread(() =>
        {
            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Remote Keyboard Controller",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();

            var toggleItem = new ToolStripMenuItem("Open/Hide window", null, (_, _) => ToggleConsole());
            var copyLinkItem = new ToolStripMenuItem("Copy link", null, (_, _) => CopyLinkToClipboard());
            var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

            contextMenu.Items.Add(toggleItem);
            contextMenu.Items.Add(copyLinkItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.DoubleClick += (_, _) => ToggleConsole();
            _notifyIcon.ShowBalloonTip(3000, "Remote Controller", "Application started and minimized to tray.", ToolTipIcon.Info);

            Application.Run();
        });

        trayThread.SetApartmentState(ApartmentState.STA);
        trayThread.IsBackground = true;
        trayThread.Start();
    }

    public void ToggleConsole()
    {
        if (_consoleHandle == IntPtr.Zero) return;

        if (_isConsoleVisible)
        {
            ShowWindow(_consoleHandle, SW_HIDE);
            _isConsoleVisible = false;
        }
        else
        {
            ShowWindow(_consoleHandle, SW_SHOW);
            _isConsoleVisible = true;
        }
    }

    private void CopyLinkToClipboard()
    {
        if (string.IsNullOrEmpty(_publicUrl)) return;

        var staThread = new Thread(() => Clipboard.SetText(_publicUrl));
        staThread.SetApartmentState(ApartmentState.STA);
        staThread.Start();
        staThread.Join();

        _notifyIcon?.ShowBalloonTip(2000, "Link copied", _publicUrl, ToolTipIcon.Info);
    }

    private void ExitApplication()
    {
        if (_notifyIcon != null)
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
        }

        Environment.Exit(0);
    }
}