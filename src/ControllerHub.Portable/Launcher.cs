using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

[assembly: AssemblyTitle("ControllerHub Portable")]
[assembly: AssemblyProduct("ControllerHub")]
[assembly: AssemblyVersion("0.3.0.0")]
[assembly: AssemblyFileVersion("0.3.0.0")]

namespace ControllerHub.Portable
{
    internal static class Launcher
    {
        static Stream Resource(string name)
        {
            var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("ControllerHub.Payload." + name);
            if (stream == null) throw new InvalidDataException("Missing portable resource.");
            return stream;
        }

        internal static string Quote(string value)
        {
            // Windows command-line quoting: preserve quotes and trailing slashes.
            var result = new StringBuilder("\""); int slashes = 0;
            foreach (char c in value)
            {
                if (c == '\\') { slashes++; continue; }
                result.Append('\\', c == '"' ? slashes * 2 + 1 : slashes); slashes = 0; result.Append(c);
            }
            result.Append('\\', slashes * 2); return result.Append('"').ToString();
        }

        static int Run(string[] args)
        {
            string id, runtime; var files = new List<PayloadFile>();
            using (var reader = new StreamReader(Resource("manifest"), Encoding.UTF8))
            {
                id = reader.ReadLine(); runtime = reader.ReadLine(); string line;
                while ((line = reader.ReadLine()) != null)
                {
                    var parts = line.Split('\t');
                    if (parts.Length != 3) throw new InvalidDataException("Invalid portable manifest entry.");
                    files.Add(new PayloadFile { Hash = parts[0], Length = Int64.Parse(parts[1], CultureInfo.InvariantCulture), Path = parts[2] });
                }
            }
            var parent = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerHub", "Portable");
            var cache = PortableFiles.Prepare(parent, id, files, delegate { return Resource("zip"); });
            if (args.Length > 0 && args[0] == "--portable-verify")
            {
                if (args.Length != 2) throw new ArgumentException("Usage: --portable-verify <report path>");
                var report = Path.GetFullPath(args[1]); Directory.CreateDirectory(Path.GetDirectoryName(report));
                File.WriteAllLines(report, new[] { "PayloadHash=" + id, "CachePath=" + cache, "Files=" + files.Count, "RuntimeVersion=" + runtime }, Encoding.UTF8);
                return 0;
            }
            var executable = Path.Combine(cache, "ControllerHub.exe");
            // Resolve caller-relative diagnostic paths before changing working
            // directory; ordinary double-click launches use the payload folder.
            var info = new ProcessStartInfo(executable, String.Join(" ", args.Select(Quote))) { UseShellExecute = false, WorkingDirectory = Environment.CurrentDirectory };
            using (var child = Process.Start(info))
            {
                if (child == null) throw new IOException("Could not start ControllerHub.");
                if (args.Length == 0) return 0;
                child.WaitForExit(); return child.ExitCode;
            }
        }

        static void RecordError(Exception error)
        {
            try
            {
                var folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ControllerHub", "Portable");
                Directory.CreateDirectory(folder); File.WriteAllText(Path.Combine(folder, "launcher-error.txt"), error.ToString());
            }
            catch { }
        }

        [STAThread]
        static int Main(string[] args)
        {
            if (args.Length > 0)
            {
                try { return Run(args); } catch (Exception error) { RecordError(error); return 1; }
            }
            Application.EnableVisualStyles(); int result = 0;
            using (var splash = new Form { Text = "ControllerHub", ClientSize = new Size(360, 82), StartPosition = FormStartPosition.CenterScreen, FormBorderStyle = FormBorderStyle.FixedDialog, MaximizeBox = false, MinimizeBox = false, ControlBox = false })
            {
                bool chinese = CultureInfo.CurrentUICulture.Name.StartsWith("zh", StringComparison.OrdinalIgnoreCase);
                splash.Controls.Add(new Label { Text = chinese ? "正在準備 ControllerHub…" : "Preparing ControllerHub…", AutoSize = false, Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Segoe UI", 11) });
                splash.Shown += delegate
                {
                    Task.Factory.StartNew(delegate { return Run(args); }).ContinueWith(task =>
                    {
                        if (task.IsFaulted)
                        {
                            result = 1; var error = task.Exception.GetBaseException(); RecordError(error);
                            MessageBox.Show(splash, (chinese ? "無法準備程式。請關閉 ControllerHub 後重試。\n\n" : "Could not prepare the app. Close ControllerHub and retry.\n\n") + error.Message, "ControllerHub", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                        splash.Close();
                    }, TaskScheduler.FromCurrentSynchronizationContext());
                };
                Application.Run(splash);
            }
            return result;
        }
    }
}
