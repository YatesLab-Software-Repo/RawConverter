using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using RawConverter.Common;
using Xunit;

namespace RawConverter.Tests
{
    public class ConversionRunnerTests : IDisposable
    {
        private const string Mgf =
            "BEGIN IONS\n" +
            "TITLE=test.1.1.2 File:\"test.raw\", NativeID:\"scan=1\"\n" +
            "RTINSECONDS=12.5\n" +
            "PEPMASS=500.25 1000\n" +
            "CHARGE=2+\n" +
            "SCANS=1\n" +
            "150.125 10.5\n" +
            "250.5 20.25\n" +
            "END IONS\n" +
            "BEGIN IONS\n" +
            "TITLE=test.2.2.3\n" +
            "RTINSECONDS=13.5\n" +
            "PEPMASS=600.75\n" +
            "CHARGE=3+\n" +
            "SCANS=2\n" +
            "350.5 30.5\n" +
            "END IONS\n";

        private readonly string _dir;

        public ConversionRunnerTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void ConvertsMgfToMs2()
        {
            string mgf = WriteInput("test.mgf", Mgf);

            FileConversionResult result = ConvertOne(mgf, OutputFormat.MS2);

            Assert.True(result.Succeeded, result.Error?.ToString());
            string[] lines = File.ReadAllLines(Path.Combine(_dir, "test.ms2"));
            Assert.Contains("H\tFirstScan\t1", lines);
            Assert.Contains("H\tLastScan\t2", lines);
            Assert.Equal(2, lines.Count(l => l.StartsWith("S\t")));
            Assert.Contains("150.125 10.5", lines);
            Assert.Contains("350.5 30.5", lines);
        }

        [Fact]
        public void OutputUsesPeriodDecimalSeparatorUnderAnyCulture()
        {
            string mgf = WriteInput("test.mgf", Mgf);
            CultureInfo previous = CultureInfo.CurrentCulture;
            CultureInfo previousDefault = CultureInfo.DefaultThreadCurrentCulture;
            try
            {
                CultureInfo german = new CultureInfo("de-DE");
                CultureInfo.CurrentCulture = german;
                CultureInfo.DefaultThreadCurrentCulture = german;

                FileConversionResult result = ConvertOne(mgf, OutputFormat.MS2);

                Assert.True(result.Succeeded, result.Error?.ToString());
            }
            finally
            {
                CultureInfo.CurrentCulture = previous;
                CultureInfo.DefaultThreadCurrentCulture = previousDefault;
            }
            string ms2 = File.ReadAllText(Path.Combine(_dir, "test.ms2"));
            Assert.Contains("150.125 10.5", ms2);
            Assert.DoesNotContain("150,125", ms2);
        }

        [Fact]
        public void ConvertsMs2BackToMgf()
        {
            string mgf = WriteInput("test.mgf", Mgf);
            Assert.True(ConvertOne(mgf, OutputFormat.MS2).Succeeded);
            string ms2 = Path.Combine(_dir, "test.ms2");

            FileConversionResult result = ConvertOne(ms2, OutputFormat.MGF, Path.Combine(_dir, "roundtrip"));

            Assert.True(result.Succeeded, result.Error?.ToString());
            string[] lines = File.ReadAllLines(Path.Combine(_dir, "roundtrip", "test.mgf"));
            Assert.Equal(2, lines.Count(l => l == "BEGIN IONS"));
            Assert.Contains(lines, l => l.StartsWith("PEPMASS=500.25"));
            Assert.Contains(lines, l => l.StartsWith("150.125"));
        }

        [Fact]
        public void WritesNextToInputWhenNoOutputFolderGiven()
        {
            string sub = Path.Combine(_dir, "sub");
            Directory.CreateDirectory(sub);
            string mgf = Path.Combine(sub, "test.mgf");
            File.WriteAllText(mgf, Mgf);

            Assert.True(ConvertOne(mgf, OutputFormat.MS2).Succeeded);

            Assert.True(File.Exists(Path.Combine(sub, "test.ms2")));
        }

        [Fact]
        public void ReportsMissingFile()
        {
            FileConversionResult result = ConvertOne(Path.Combine(_dir, "missing.raw"), OutputFormat.MS2);

            Assert.False(result.Succeeded);
            Assert.IsType<FileNotFoundException>(result.Error);
        }

        [Fact]
        public void ReportsUnsupportedInput()
        {
            string txt = WriteInput("test.txt", "hello");

            FileConversionResult result = ConvertOne(txt, OutputFormat.MS2);

            Assert.False(result.Succeeded);
            Assert.IsType<ArgumentException>(result.Error);
        }

        [Fact]
        public void ReportsMgfInputWithoutMs2Output()
        {
            string mgf = WriteInput("test.mgf", Mgf);

            FileConversionResult result = ConvertOne(mgf, OutputFormat.MS1);

            Assert.False(result.Succeeded);
            Assert.IsType<ArgumentException>(result.Error);
        }

        [Fact]
        public void ReportsInvalidRawFile()
        {
            string raw = WriteInput("bad.raw", "this is not a RAW file");

            FileConversionResult result = ConvertOne(raw, OutputFormat.MS2);

            Assert.False(result.Succeeded);
            Assert.NotNull(result.Error);
        }

        [Fact]
        public void CancelledBeforeStartConvertsNothing()
        {
            string mgf = WriteInput("test.mgf", Mgf);
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            IReadOnlyList<FileConversionResult> results = ConversionRunner.ConvertFiles(new[] { mgf },
                new ConversionOptions { OutputFormats = OutputFormat.MS2 }, null, cts.Token);

            Assert.True(results[0].Cancelled);
            Assert.False(File.Exists(Path.Combine(_dir, "test.ms2")));
        }

        [Fact]
        public void ConvertsSeveralFilesInParallelAndReportsProgress()
        {
            List<string> inputs = new List<string>();
            for (int i = 0; i < 4; i++)
            {
                inputs.Add(WriteInput("test" + i + ".mgf", Mgf));
            }
            List<ConversionProgress> reports = new List<ConversionProgress>();
            SyncProgress progress = new SyncProgress(p => { lock (reports) { reports.Add(p); } });

            IReadOnlyList<FileConversionResult> results = ConversionRunner.ConvertFiles(inputs,
                new ConversionOptions { OutputFormats = OutputFormat.MS2, MaxParallelFiles = 4 }, progress);

            Assert.All(results, r => Assert.True(r.Succeeded, r.Error?.ToString()));
            Assert.Equal(inputs, results.Select(r => r.InputFile));
            for (int i = 0; i < 4; i++)
            {
                Assert.True(File.Exists(Path.Combine(_dir, "test" + i + ".ms2")));
                Assert.Contains(reports, p => p.FileIndex == i && p.Percent == 100 && p.FileCount == 4);
            }
        }

        [Fact]
        public void ZlibDecompressReadsZlibStreams()
        {
            byte[] data = Enumerable.Range(0, 1000).Select(i => (byte)(i % 7)).ToArray();
            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream zlib = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            {
                zlib.Write(data, 0, data.Length);
            }

            Assert.Equal(data, Utils.ZlibDecompress(compressed.ToArray()));
        }

        private string WriteInput(string name, string content)
        {
            string path = Path.Combine(_dir, name);
            File.WriteAllText(path, content);
            return path;
        }

        private static FileConversionResult ConvertOne(string input, OutputFormat formats, string outFolder = null)
        {
            return ConversionRunner.ConvertFiles(new[] { input },
                new ConversionOptions { OutputFormats = formats, OutputFolder = outFolder })[0];
        }

        /// <summary>
        /// Unlike Progress&lt;T&gt;, reports synchronously so all reports have arrived when ConvertFiles returns.
        /// </summary>
        private class SyncProgress : IProgress<ConversionProgress>
        {
            private readonly Action<ConversionProgress> _report;

            public SyncProgress(Action<ConversionProgress> report)
            {
                _report = report;
            }

            public void Report(ConversionProgress value)
            {
                _report(value);
            }
        }
    }
}
