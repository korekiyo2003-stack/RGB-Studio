using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

internal sealed class TriggerWindow : Form
{
    protected override bool ShowWithoutActivation { get { return true; } }
}

internal static class RgbTrigger
{
    [STAThread]
    private static void Main(string[] args)
    {
        int parentId;
        if (args.Length != 2 || !int.TryParse(args[0], out parentId)) return;
        try {
            using (Process parent = Process.GetProcessById(parentId))
            using (var signal = EventWaitHandle.OpenExisting(args[1]))
            using (var window = new TriggerWindow { Text = "RGB Studio", ShowInTaskbar = false,
                FormBorderStyle = FormBorderStyle.None, Size = new Size(1, 1), Opacity = 0.01,
                StartPosition = FormStartPosition.Manual, Location = new Point(2, 2) })
            using (var timer = new System.Windows.Forms.Timer { Interval = 250 }) {
                timer.Tick += delegate { if (parent.HasExited || signal.WaitOne(0)) window.Close(); };
                timer.Start();
                Application.Run(window);
            }
        } catch (ArgumentException) { }
          catch (WaitHandleCannotBeOpenedException) { }
    }
}
