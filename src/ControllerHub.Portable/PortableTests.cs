using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ControllerHub.Portable
{
    internal static class PortableTests
    {
        [DllImport("shell32.dll", SetLastError = true)] static extern IntPtr CommandLineToArgvW([MarshalAs(UnmanagedType.LPWStr)] string command, out int count);
        [DllImport("kernel32.dll")] static extern IntPtr LocalFree(IntPtr pointer);
        static readonly List<string> results = new List<string>();
        static void Check(bool success, string name) { if (!success) throw new Exception(name); results.Add("PASS: " + name); }
        static void Reject(Action action, string name) { bool rejected = false; try { action(); } catch (InvalidDataException) { rejected = true; } Check(rejected, name); }
        static byte[] Archive(params string[] names)
        {
            using (var stream = new MemoryStream())
            {
                using (var zip = new ZipArchive(stream, ZipArchiveMode.Create, true))
                    foreach (var name in names) using (var target = zip.CreateEntry(name).Open()) { var bytes = Encoding.UTF8.GetBytes("hello"); target.Write(bytes, 0, bytes.Length); }
                return stream.ToArray();
            }
        }
        static string Digest(byte[] bytes) { using (var stream = new MemoryStream(bytes)) return PortableFiles.Hash(stream); }
        static PayloadFile FileEntry(string path) { return new PayloadFile { Path = path, Length = 5, Hash = Digest(Encoding.UTF8.GetBytes("hello")) }; }
        public static int Main(string[] args)
        {
            var root = Path.Combine(Path.GetTempPath(), "ControllerHub-PortableTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                foreach (var invalid in new[] { "../escape", "a/../escape", "C:/escape", "/escape", "a:stream", "a\\b", "./a", "a//b", "a./b" })
                    Reject(delegate { PortableFiles.Destination(root, invalid); }, "reject unsafe path " + invalid);
                Check(PortableFiles.Destination(root, "docs/readme.txt").StartsWith(root + Path.DirectorySeparatorChar), "nested path stays inside cache");
                var bytes = Archive("readme.txt"); var id = Digest(bytes); var files = new[] { FileEntry("readme.txt") };
                string cache = PortableFiles.Prepare(Path.Combine(root, "cache"), id, files, delegate { return new MemoryStream(bytes); });
                Check(PortableFiles.Valid(cache, files), "cold extraction verifies every file");
                string warm = PortableFiles.Prepare(Path.Combine(root, "cache"), id, files, delegate { throw new Exception("Warm cache must not extract again."); });
                Check(cache == warm, "warm launch reuses verified cache");
                File.WriteAllText(Path.Combine(cache, "readme.txt"), "wrong");
                cache = PortableFiles.Prepare(Path.Combine(root, "cache"), id, files, delegate { return new MemoryStream(bytes); });
                Check(PortableFiles.Valid(cache, files), "same-length cache corruption is repaired");
                Check(Directory.GetDirectories(Path.Combine(root, "cache"), id + ".previous-*").Length == 1, "corrupt cache retained separately");
                int opens = 0;
                var tasks = Enumerable.Range(0, 4).Select(i => Task.Factory.StartNew(delegate {
                    return PortableFiles.Prepare(Path.Combine(root, "parallel"), id, files, delegate { Interlocked.Increment(ref opens); return new MemoryStream(bytes); });
                })).ToArray();
                Task.WaitAll(tasks);
                Check(opens == 2 && tasks.Select(t => t.Result).Distinct().Count() == 1, "concurrent launches extract once");
                Reject(delegate { PortableFiles.Extract(new MemoryStream(Archive("../outside")), Path.Combine(root, "traversal"), new[] { FileEntry("../outside") }); }, "archive traversal rejected");
                Reject(delegate { PortableFiles.Extract(new MemoryStream(Archive("readme.txt", "readme.txt")), Path.Combine(root, "duplicates"), files); }, "duplicate archive entry rejected");
                Reject(delegate { PortableFiles.Extract(new MemoryStream(Archive("other.txt")), Path.Combine(root, "unexpected"), files); }, "unexpected archive entry rejected");
                Reject(delegate { PortableFiles.Extract(new MemoryStream(bytes), Path.Combine(root, "missing"), new[] { FileEntry("readme.txt"), FileEntry("missing.txt") }); }, "missing archive entry rejected");
                var wrong = FileEntry("readme.txt"); wrong.Hash = new string('0', 64);
                Reject(delegate { PortableFiles.Extract(new MemoryStream(bytes), Path.Combine(root, "checksum"), new[] { wrong }); }, "file checksum mismatch rejected");
                Reject(delegate { PortableFiles.Prepare(Path.Combine(root, "bad-payload"), new string('1', 64), files, delegate { return new MemoryStream(bytes); }); }, "embedded payload checksum mismatch rejected");
                Check(Directory.GetDirectories(Path.Combine(root, "bad-payload"), ".partial-*").Length == 0, "failed extraction retires owned staging directory");
                var expected = new[] { "", "a b", "繁體 English path\\", "quote\"inside", "C:\\trailing\\", "\\\"", "x\tq" };
                int count; var argv = CommandLineToArgvW("test.exe " + String.Join(" ", expected.Select(Launcher.Quote)), out count);
                try
                {
                    var actual = Enumerable.Range(1, count - 1).Select(i => Marshal.PtrToStringUni(Marshal.ReadIntPtr(argv, i * IntPtr.Size))).ToArray();
                    Check(actual.SequenceEqual(expected), "Windows quoting preserves Unicode, spaces, quotes and trailing slashes");
                }
                finally { LocalFree(argv); }
                File.WriteAllLines(args[0], results, Encoding.UTF8); return 0;
            }
            catch (Exception error) { results.Add("FAIL: " + error); File.WriteAllLines(args[0], results, Encoding.UTF8); return 1; }
        }
    }
}
