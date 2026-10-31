using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;

namespace ControllerHub.Portable
{
    internal sealed class PayloadFile
    {
        internal string Path;
        internal long Length;
        internal string Hash;
    }

    internal static class PortableFiles
    {
        internal static string Hash(Stream stream)
        {
            using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "").ToLowerInvariant();
        }

        internal static string Destination(string root, string relative)
        {
            if (String.IsNullOrWhiteSpace(relative) || System.IO.Path.IsPathRooted(relative) || relative.Contains(":") || relative.Contains("\\"))
                throw new InvalidDataException("Invalid payload path.");
            var parts = relative.Split('/');
            if (parts.Any(p => p.Length == 0 || p == "." || p == ".." || p.EndsWith(".") || p.EndsWith(" ")))
                throw new InvalidDataException("Invalid payload path component.");
            var prefix = System.IO.Path.GetFullPath(root).TrimEnd(System.IO.Path.DirectorySeparatorChar) + System.IO.Path.DirectorySeparatorChar;
            var result = System.IO.Path.GetFullPath(System.IO.Path.Combine(prefix, relative));
            if (!result.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) throw new InvalidDataException("Payload path escapes cache.");
            return result;
        }

        internal static bool Valid(string root, IList<PayloadFile> files)
        {
            foreach (var entry in files)
            {
                var path = Destination(root, entry.Path);
                if (!File.Exists(path)) return false;
                var info = new FileInfo(path);
                if (info.Length != entry.Length || (info.Attributes & FileAttributes.ReparsePoint) != 0) return false;
                using (var stream = File.OpenRead(path)) if (Hash(stream) != entry.Hash) return false;
            }
            return true;
        }

        internal static void Extract(Stream source, string root, IList<PayloadFile> files)
        {
            var expected = files.ToDictionary(f => f.Path, StringComparer.OrdinalIgnoreCase);
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            using (var archive = new ZipArchive(source, ZipArchiveMode.Read))
            {
                foreach (var entry in archive.Entries)
                {
                    if (entry.FullName.EndsWith("/")) continue;
                    var path = Destination(root, entry.FullName);
                    PayloadFile item;
                    if (!expected.TryGetValue(entry.FullName, out item) || !seen.Add(entry.FullName) || entry.Length != item.Length)
                        throw new InvalidDataException("Unexpected or duplicate payload entry.");
                    Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path));
                    using (var input = entry.Open())
                    using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                    using (var sha = SHA256.Create())
                    {
                        var buffer = new byte[65536]; long length = 0; int count;
                        while ((count = input.Read(buffer, 0, buffer.Length)) != 0)
                        {
                            length = checked(length + count);
                            if (length > item.Length) throw new InvalidDataException("Payload exceeds declared length.");
                            output.Write(buffer, 0, count); sha.TransformBlock(buffer, 0, count, buffer, 0);
                        }
                        sha.TransformFinalBlock(new byte[0], 0, 0);
                        if (length != item.Length || BitConverter.ToString(sha.Hash).Replace("-", "").ToLowerInvariant() != item.Hash)
                            throw new InvalidDataException("Payload file checksum mismatch.");
                    }
                }
            }
            if (seen.Count != expected.Count) throw new InvalidDataException("Incomplete payload.");
        }

        internal static string Prepare(string parent, string id, IList<PayloadFile> files, Func<Stream> open)
        {
            if (id.Length != 64 || id.Any(c => !Uri.IsHexDigit(c)) || files.Count == 0) throw new InvalidDataException("Invalid payload manifest.");
            var cache = Destination(parent, id);
            foreach (var file in files) Destination(cache, file.Path);
            using (var mutex = new Mutex(false, "Local\\ControllerHub.Portable." + id))
            {
                bool acquired;
                try { acquired = mutex.WaitOne(60000); } catch (AbandonedMutexException) { acquired = true; }
                if (!acquired) throw new IOException("Another portable launch is still preparing files. Try again shortly.");
                string stage = null;
                try
                {
                    if (Directory.Exists(cache) && Valid(cache, files)) return cache;
                    Directory.CreateDirectory(parent);
                    stage = Destination(parent, ".partial-" + id + "-" + Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(stage);
                    using (var stream = open()) if (Hash(stream) != id) throw new InvalidDataException("Embedded payload checksum mismatch.");
                    using (var stream = open()) Extract(stream, stage, files);
                    if (Directory.Exists(cache))
                    {
                        foreach (var process in Process.GetProcessesByName("ControllerHub"))
                        {
                            using (process)
                            {
                                string path = null;
                                try { path = process.MainModule.FileName; } catch { }
                                if (path != null && path.StartsWith(cache + System.IO.Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                                    throw new IOException("Close ControllerHub before repairing its portable cache.");
                            }
                        }
                        Directory.Move(cache, cache + ".previous-" + Guid.NewGuid().ToString("N"));
                    }
                    Directory.Move(stage, cache); stage = null;
                    return cache;
                }
                finally
                {
                    // Only remove this invocation's fresh staging directory.
                    if (stage != null && System.IO.Path.GetDirectoryName(stage) == System.IO.Path.GetFullPath(parent).TrimEnd(System.IO.Path.DirectorySeparatorChar))
                        try { Directory.Delete(stage, true); } catch { }
                    mutex.ReleaseMutex();
                }
            }
        }
    }
}
