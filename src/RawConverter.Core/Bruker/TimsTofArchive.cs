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
    /// top-level "name.d/" folder, into the folder that holds the archive.
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
