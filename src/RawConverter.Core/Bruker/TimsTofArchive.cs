using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RawConverter.Common;
using RawConverter.Converter;

namespace RawConverter.Bruker
{
    /// <summary>
    /// Extracts a timsTOF .d folder from a tar archive (.tar, .tar.gz, .tgz) whose entries all sit under one
    /// top-level "name.d/" folder, into the folder that holds the archive; and packs a .d folder into such an
    /// archive (name.d.tar).
    /// </summary>
    internal static class TimsTofArchive
    {
        public static bool IsArchive(string path)
        {
            string name = Path.GetFileName(path).ToLowerInvariant();
            return File.Exists(path) && (name.EndsWith(".tar") || name.EndsWith(".tar.gz") || name.EndsWith(".tgz"));
        }

        /// <summary>
        /// Extract the .d folder next to the archive and return its path. If that folder already exists it is
        /// used as it is. The archive is extracted into a temporary folder and renamed when complete, so an
        /// interrupted extraction never leaves a partial .d folder. Returns null if cancelled.
        /// </summary>
        /// <param name="progressEnd">progress percentage reached when extraction is complete</param>
        public static string Extract(string archive, TaskProgress progress, int progressEnd)
        {
            string parent = Path.GetDirectoryName(Path.GetFullPath(archive));
            using FileStream file = File.OpenRead(archive);
            using Stream tarStream = IsGzip(file) ? new GZipStream(file, CompressionMode.Decompress) : file;
            using TarReader reader = new TarReader(tarStream);

            string topName = null;
            string finalDir = null;
            string tempDir = null;
            try
            {
                TarEntry entry;
                while ((entry = reader.GetNextEntry()) != null)
                {
                    if (entry.EntryType == TarEntryType.GlobalExtendedAttributes)
                    {
                        continue;
                    }

                    List<string> segments = EntrySegments(entry.Name);
                    if (segments.Count == 0)
                    {
                        continue;
                    }
                    if (topName == null)
                    {
                        topName = segments[0];
                        if (!topName.EndsWith(".d", StringComparison.OrdinalIgnoreCase))
                        {
                            throw new InvalidDataException("Archive does not contain a timsTOF .d folder at its top level: " + archive);
                        }
                        finalDir = Path.Combine(parent, topName);
                        if (Directory.Exists(finalDir))
                        {
                            if (!TimsTofConverter.IsTimsTofDirectory(finalDir))
                            {
                                throw new IOException(finalDir + " already exists but is not a complete timsTOF .d folder; "
                                    + "remove it to extract " + archive);
                            }
                            Log.Info(" Using the already extracted " + finalDir);
                            return finalDir;
                        }
                        tempDir = Path.Combine(parent, "." + topName + ".extracting-" + Guid.NewGuid().ToString("N"));
                        Log.Info(" Extracting " + archive + " to " + finalDir);
                        Directory.CreateDirectory(tempDir);
                    }
                    else if (segments[0] != topName)
                    {
                        throw new InvalidDataException("Archive has more than one top-level entry (" + topName + ", "
                            + segments[0] + "); expected a single .d folder: " + archive);
                    }

                    string target = Path.Combine(new[] { tempDir }.Concat(segments.Skip(1)).ToArray());
                    if (!Path.GetFullPath(target).StartsWith(tempDir + Path.DirectorySeparatorChar) && segments.Count > 1)
                    {
                        throw new InvalidDataException("Archive entry escapes the .d folder: " + entry.Name);
                    }

                    switch (entry.EntryType)
                    {
                        case TarEntryType.Directory:
                            // stored directory permissions are not applied; some archives mark them read-only;
                            Directory.CreateDirectory(target);
                            break;
                        case TarEntryType.RegularFile:
                        case TarEntryType.V7RegularFile:
                        case TarEntryType.ContiguousFile:
                            if (segments.Count > 1)
                            {
                                Directory.CreateDirectory(Path.GetDirectoryName(target));
                                entry.ExtractToFile(target, true);
                            }
                            break;
                        default:
                            Log.Warning("Skipping " + entry.EntryType + " entry " + entry.Name + " in " + archive);
                            break;
                    }

                    progress.CurrentProgress = (int)(file.Position * progressEnd / Math.Max(1, file.Length));
                    if (progress.Aborted)
                    {
                        Directory.Delete(tempDir, true);
                        return null;
                    }
                }

                if (topName == null)
                {
                    throw new InvalidDataException("Archive is empty: " + archive);
                }
                if (!TimsTofConverter.IsTimsTofDirectory(tempDir))
                {
                    throw new InvalidDataException("Archive's " + topName + " folder has no analysis.tdf/analysis.tdf_bin: " + archive);
                }
                Directory.Move(tempDir, finalDir);
                return finalDir;
            }
            catch
            {
                if (tempDir != null && Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
                throw;
            }
        }

        /// <summary>
        /// Pack a .d folder into "name.d.tar" (uncompressed; the .tdf_bin data is already compressed), with every
        /// entry under "name.d/", in outFolder, or next to the .d folder if outFolder is null or empty. An existing
        /// archive is replaced. The archive is written to a temporary file and renamed when complete, so an
        /// interrupted run never leaves a partial archive. Returns the archive path, or null if cancelled.
        /// </summary>
        /// <param name="progressEnd">progress percentage reached when the archive is complete</param>
        public static string Create(string analysisDir, string outFolder, TaskProgress progress, int progressEnd)
        {
            string source = Path.GetFullPath(analysisDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string name = Path.GetFileName(source);
            string folder = Path.GetFullPath(string.IsNullOrEmpty(outFolder) ? Path.GetDirectoryName(source) : outFolder);
            if ((folder + Path.DirectorySeparatorChar).StartsWith(source + Path.DirectorySeparatorChar, PathComparison))
            {
                throw new ArgumentException("The .d.tar archive cannot be written inside the .d folder it packs: " + folder);
            }
            Directory.CreateDirectory(folder);
            string archive = Path.Combine(folder, name + ".tar");
            string temp = Path.Combine(folder, "." + name + ".tar.writing-" + Guid.NewGuid().ToString("N"));

            // sorted, so the same folder always gives the same archive;
            List<string> entries = Directory.EnumerateFileSystemEntries(source, "*", SearchOption.AllDirectories)
                .OrderBy(p => p, StringComparer.Ordinal).ToList();
            long totalBytes = Math.Max(1, entries.Where(File.Exists).Sum(p => new FileInfo(p).Length));
            long doneBytes = 0;

            Log.Info(" Packing " + source + " into " + archive);
            try
            {
                using (FileStream file = new FileStream(temp, FileMode.CreateNew, FileAccess.Write))
                using (TarWriter writer = new TarWriter(file, TarEntryFormat.Pax, false))
                {
                    writer.WriteEntry(DirectoryEntry(name + "/", source));
                    foreach (string path in entries)
                    {
                        string entryName = name + "/" + Path.GetRelativePath(source, path).Replace(Path.DirectorySeparatorChar, '/');
                        if (Directory.Exists(path))
                        {
                            writer.WriteEntry(DirectoryEntry(entryName + "/", path));
                            continue;
                        }
                        using FileStream data = File.OpenRead(path);
                        using ProgressStream counted = new ProgressStream(data, read =>
                        {
                            doneBytes += read;
                            progress.CurrentProgress = (int)(doneBytes * progressEnd / totalBytes);
                            if (progress.Aborted)
                            {
                                throw new OperationCanceledException();
                            }
                        });
                        writer.WriteEntry(new PaxTarEntry(TarEntryType.RegularFile, entryName)
                        {
                            DataStream = counted,
                            Mode = FileMode644,
                            ModificationTime = File.GetLastWriteTimeUtc(path),
                        });
                    }
                }
                File.Move(temp, archive, true);
                progress.CurrentProgress = progressEnd;
                Log.Info(" Packed " + archive);
                return archive;
            }
            catch (OperationCanceledException)
            {
                File.Delete(temp);
                return null;
            }
            catch
            {
                File.Delete(temp);
                throw;
            }
        }

        private const UnixFileMode FileMode644 =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead;

        private const UnixFileMode DirectoryMode755 = FileMode644 | UnixFileMode.UserExecute | UnixFileMode.GroupExecute
            | UnixFileMode.OtherExecute;

        private static readonly StringComparison PathComparison =
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

        private static PaxTarEntry DirectoryEntry(string entryName, string path)
        {
            return new PaxTarEntry(TarEntryType.Directory, entryName)
            {
                Mode = DirectoryMode755,
                ModificationTime = Directory.GetLastWriteTimeUtc(path),
            };
        }

        /// <summary>
        /// A read-only view of a file that reports each read, so packing a large .tdf_bin shows progress and can be
        /// cancelled part-way.
        /// </summary>
        private sealed class ProgressStream : Stream
        {
            private readonly Stream _inner;
            private readonly Action<int> _onRead;

            public ProgressStream(Stream inner, Action<int> onRead)
            {
                _inner = inner;
                _onRead = onRead;
            }

            public override bool CanRead => true;
            public override bool CanSeek => _inner.CanSeek;
            public override bool CanWrite => false;
            public override long Length => _inner.Length;

            public override long Position
            {
                get { return _inner.Position; }
                set { _inner.Position = value; }
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                int read = _inner.Read(buffer, offset, count);
                _onRead(read);
                return read;
            }

            public override int Read(Span<byte> buffer)
            {
                int read = _inner.Read(buffer);
                _onRead(read);
                return read;
            }

            public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
            public override void Flush() { }
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }

        /// <summary>
        /// Path segments of a tar entry name; rejects absolute paths and "..".
        /// </summary>
        private static List<string> EntrySegments(string name)
        {
            if (name.StartsWith("/") || Path.IsPathRooted(name))
            {
                throw new InvalidDataException("Archive entry has an absolute path: " + name);
            }
            List<string> segments = name.Split('/', StringSplitOptions.RemoveEmptyEntries).Where(s => s != ".").ToList();
            if (segments.Contains(".."))
            {
                throw new InvalidDataException("Archive entry contains '..': " + name);
            }
            return segments;
        }

        private static bool IsGzip(FileStream file)
        {
            int b1 = file.ReadByte(), b2 = file.ReadByte();
            file.Position = 0;
            return b1 == 0x1f && b2 == 0x8b;
        }
    }
}
