using System;
using System.Collections.Generic;

namespace RawConverter.Cli
{
    /// <summary>
    /// Prints per-file progress with the whole batch's progress and time left; overwrites one line on a terminal and
    /// prints every 10% when redirected.
    /// </summary>
    internal class ConsoleProgress : IProgress<ConversionProgress>
    {
        private readonly object _lock = new object();
        private readonly Dictionary<int, int> _lastPrinted = new Dictionary<int, int>();
        private int _lastLength;

        public ConsoleProgress(IReadOnlyList<string> inputFiles)
        {
            Batch = new BatchProgress(inputFiles);
        }

        public BatchProgress Batch { get; }

        public void Report(ConversionProgress value)
        {
            lock (_lock)
            {
                Batch.Report(value.FileIndex, value.Percent);
                int step = Console.IsOutputRedirected ? 10 : 1;
                _lastPrinted.TryGetValue(value.FileIndex, out int last);
                if (value.Percent < last + step && value.Percent != 100)
                {
                    return;
                }
                _lastPrinted[value.FileIndex] = value.Percent;

                string line = " Converting file " + (value.FileIndex + 1) + " / " + value.FileCount + ": " + value.Percent + "%";
                if (Batch.FilesDone < Batch.FileCount)
                {
                    line += "   (" + (Batch.FileCount > 1 ? "all files: " + Batch.Percent + "%, " : "")
                        + BatchProgress.FormatRemaining(Batch.Remaining) + ")";
                }
                if (Console.IsOutputRedirected)
                {
                    Console.WriteLine(line);
                }
                else
                {
                    // pad over the end of a longer previous line;
                    Console.Write("\r" + line.PadRight(_lastLength) + (value.Percent == 100 ? Environment.NewLine : ""));
                    _lastLength = value.Percent == 100 ? 0 : line.Length;
                }
            }
        }
    }
}
