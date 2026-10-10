using System.Reflection;
using System.Runtime.InteropServices;

namespace KeyboardController.Services;

public class TrayService
{
    private NotifyIcon? _notifyIcon;
    private string _publicUrl = string.Empty;

    public void Initialize(string initialUrl)
    {
        _publicUrl = initialUrl;

        // Starty tray icon in a separate thread
        var trayThread = new Thread(() =>
        {
            Icon? appIcon = null;
            try
            {
                var assembly = Assembly.GetExecutingAssembly();
                using var stream = assembly.GetManifestResourceStream("KeyboardController.favicon.ico");

                if (stream != null)
                {
                    appIcon = new Icon(stream);
                }
            }
            catch
            {
                // Fallback на случай ошибки
                appIcon = SystemIcons.Application;
            }

            _notifyIcon = new NotifyIcon
            {
                Icon = SystemIcons.Application,
                Text = "Remote Keyboard Controller",
                Visible = true
            };

            var contextMenu = new ContextMenuStrip();
            
            var copyLinkItem = new ToolStripMenuItem("Copy link", null, (_, _) => CopyLinkToClipboard());
            var exitItem = new ToolStripMenuItem("Exit", null, (_, _) => ExitApplication());

            contextMenu.Items.Add(copyLinkItem);
            contextMenu.Items.Add(new ToolStripSeparator());
            contextMenu.Items.Add(exitItem);

            _notifyIcon.ContextMenuStrip = contextMenu;
            _notifyIcon.ShowBalloonTip(3000, "Remote Controller", "Application started and minimized to tray.", ToolTipIcon.Info);

            Application.Run();
        });

        trayThread.SetApartmentState(ApartmentState.STA);
        trayThread.IsBackground = true;
        trayThread.Start();
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