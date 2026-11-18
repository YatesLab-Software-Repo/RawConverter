using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace RawConverter
{
    /// <summary>
    /// Progress of a whole batch of conversions and an estimate of the time left. Each file's progress counts by the
    /// size of its input (a .d folder by the size of its files), so a large file weighs more than a small one; the
    /// estimate is the elapsed time scaled by the work left.
    /// </summary>
    public sealed class BatchProgress
    {
        /// <summary>No estimate before this share of the work is done, or before MinElapsed: early ones jump around.</summary>
        public const double MinFraction = 0.02;

        public static readonly TimeSpan MinElapsed = TimeSpan.FromSeconds(5);

        private readonly double[] _weights;
        private readonly int[] _percents;
        private readonly Func<TimeSpan> _elapsed;

        /// <param name="elapsed">for tests; null measures from now</param>
        public BatchProgress(IReadOnlyList<string> inputs, Func<TimeSpan> elapsed = null)
            : this(inputs.Select(InputSize).ToList(), elapsed)
        {
        }

        /// <param name="sizes">the inputs' sizes in bytes; 0 for unknown</param>
        public BatchProgress(IReadOnlyList<long> sizes, Func<TimeSpan> elapsed = null)
        {
            // unknown sizes count as the average of the known ones (all equal if none is known);
            double known = sizes.Where(s => s > 0).Select(s => (double)s).DefaultIfEmpty(1).Average();
            double[] w = sizes.Select(s => s > 0 ? s : known).ToArray();
            double total = w.Sum();
            _weights = w.Select(x => total > 0 ? x / total : 1.0 / Math.Max(1, w.Length)).ToArray();
            _percents = new int[sizes.Count];
            if (elapsed == null)
            {
                Stopwatch watch = Stopwatch.StartNew();
                elapsed = () => watch.Elapsed;
            }
            _elapsed = elapsed;
        }

        public int FileCount => _percents.Length;

        /// <summary>Files finished (100%).</summary>
        public int FilesDone => _percents.Count(p => p >= 100);

        /// <summary>Share of the whole batch done, 0 to 1.</summary>
        public double Fraction => _percents.Select((p, i) => _weights[i] * p / 100.0).Sum();

        public int Percent => (int)Math.Floor(Fraction * 100);

        public TimeSpan Elapsed => _elapsed();

        public void Report(int fileIndex, int percent)
        {
            if (fileIndex >= 0 && fileIndex < _percents.Length)
            {
                _percents[fileIndex] = Math.Max(_percents[fileIndex], Math.Clamp(percent, 0, 100));
            }
        }

        /// <summary>Estimated time left for the whole batch, or null while too little is done to tell.</summary>
        public TimeSpan? Remaining
        {
            get
            {
                double f = Fraction;
                TimeSpan elapsed = Elapsed;
                if (f >= 1)
                {
                    return TimeSpan.Zero;
                }
                if (f < MinFraction || elapsed < MinElapsed)
                {
                    return null;
                }
                return TimeSpan.FromSeconds(elapsed.TotalSeconds * (1 - f) / f);
            }
        }

        /// <summary>"estimating...", "less than a minute left", "about 7 min left", "about 1 h 25 min left".</summary>
        public static string FormatRemaining(TimeSpan? remaining)
        {
            if (remaining == null)
            {
                return "estimating time left...";
            }
            double minutes = remaining.Value.TotalMinutes;
            if (minutes < 1)
            {
                return "less than a minute left";
            }
            if (minutes < 10)
            {
                return "about " + Math.Ceiling(minutes) + " min left";
            }
            // longer estimates are rounded to 5 minutes, so they do not change with every update;
            int rounded = (int)(Math.Round(minutes / 5) * 5);
            return rounded < 60 ? "about " + rounded + " min left"
                : "about " + rounded / 60 + " h" + (rounded % 60 == 0 ? "" : " " + rounded % 60 + " min") + " left";
        }

        /// <summary>"42 s", "4 min 12 s", "1 h 5 min".</summary>
        public static string FormatDuration(TimeSpan t)
        {
            if (t.TotalMinutes < 1)
            {
                return Math.Max(1, (int)Math.Round(t.TotalSeconds)) + " s";
            }
            if (t.TotalHours < 1)
            {
                return (int)t.TotalMinutes + " min " + t.Seconds + " s";
            }
            return (int)t.TotalHours + " h " + t.Minutes + " min";
        }

        /// <summary>
        /// The size of an input in bytes: a file's length, or the total of a folder's files (a timsTOF .d); 0 if it
        /// cannot be read.
        /// </summary>
        public static long InputSize(string path)
        {
            try
            {
                if (File.Exists(path))
                {
                    return Length(new FileInfo(path));
                }
                if (Directory.Exists(path))
                {
                    return new DirectoryInfo(path).EnumerateFiles("*", SearchOption.AllDirectories).Sum(Length);
                }
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException)
            {
            }
            return 0;
        }

        /// <summary>
        /// The size of a file, or of the file a symbolic link points to (FileInfo.Length of a link is the link's own).
        /// </summary>
        private static long Length(FileInfo file)
        {
            if (file.LinkTarget != null && file.ResolveLinkTarget(true) is FileInfo target && target.Exists)
            {
                return target.Length;
            }
            return file.Length;
        }
    }
}
