using System;
using System.Collections.Generic;
using System.Formats.Tar;
using System.IO;
using System.Linq;
using System.Threading;
using Xunit;

namespace RawConverter.Tests
{
    /// <summary>
    /// Packing timsTOF .d folders into .d.tar (OutputFormat.DTar, --tar).
    /// </summary>
    public class DTarTests : IDisposable
    {
        private readonly string _dir;
        private readonly string _d;

        public DTarTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            _d = Path.Combine(_dir, "sample.d");
            Directory.CreateDirectory(Path.Combine(_d, "sample.m"));
            File.WriteAllText(Path.Combine(_d, "analysis.tdf"), "tdf");
            File.WriteAllText(Path.Combine(_d, "analysis.tdf_bin"), new string('x', 300_000));
            File.WriteAllText(Path.Combine(_d, "sample.m", "method.txt"), "method");
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void PacksNextToFolder()
        {
            FileConversionResult result = Pack(_d, null);

            Assert.True(result.Succeeded, result.Error?.ToString());
            Dictionary<string, string> entries = ReadArchive(Path.Combine(_dir, "sample.d.tar"));
            Assert.Equal(new[] { "sample.d/", "sample.d/analysis.tdf", "sample.d/analysis.tdf_bin", "sample.d/sample.m/",
                "sample.d/sample.m/method.txt" }, entries.Keys);
            Assert.Equal("tdf", entries["sample.d/analysis.tdf"]);
            Assert.Equal(300_000, entries["sample.d/analysis.tdf_bin"].Length);
            Assert.Equal("method", entries["sample.d/sample.m/method.txt"]);
            AssertNoTemporaryFiles(_dir);
        }

        [Fact]
        public void PacksIntoOutputFolderAndReplacesExistingArchive()
        {
            string outFolder = Path.Combine(_dir, "out");
            Directory.CreateDirectory(outFolder);
            File.WriteAllText(Path.Combine(outFolder, "sample.d.tar"), "old");

            Assert.True(Pack(_d + Path.DirectorySeparatorChar, outFolder).Succeeded);

            Assert.Contains("sample.d/analysis.tdf", ReadArchive(Path.Combine(outFolder, "sample.d.tar")).Keys);
            Assert.False(File.Exists(Path.Combine(_dir, "sample.d.tar")));
            AssertNoTemporaryFiles(outFolder);
        }

        [Fact]
        public void PackedArchiveExtractsToTheSameFolder()
        {
            string outFolder = Path.Combine(_dir, "copy");
            Assert.True(Pack(_d, outFolder).Succeeded);

            // extraction works; the stand-in analysis.tdf is not a database, so the conversion itself fails;
            ConversionRunner.ConvertFiles(new[] { Path.Combine(outFolder, "sample.d.tar") },
                new ConversionOptions { OutputFormats = OutputFormat.MS2 });

            string extracted = Path.Combine(outFolder, "sample.d");
            foreach (string file in Directory.GetFiles(_d, "*", SearchOption.AllDirectories))
            {
                Assert.Equal(File.ReadAllText(file), File.ReadAllText(Path.Combine(extracted, Path.GetRelativePath(_d, file))));
            }
        }

        [Fact]
        public void RefusesToWriteInsideTheFolder()
        {
            FileConversionResult result = Pack(_d, Path.Combine(_d, "archive"));

            Assert.IsType<ArgumentException>(result.Error);
            Assert.False(Directory.Exists(Path.Combine(_d, "archive")));
        }

        [Fact]
        public void RejectsFolderThatIsNotTimsTof()
        {
            string notD = Path.Combine(_dir, "other");
            Directory.CreateDirectory(notD);

            Assert.IsType<ArgumentException>(Pack(notD, null).Error);
            Assert.False(File.Exists(Path.Combine(_dir, "other.tar")));
        }

        [Theory]
        [InlineData("analysis.tdf", "other.tdf")]
        [InlineData("analysis.tdf_bin", "other.tdf_bin")]
        [InlineData("analysis.tdf_bin", null)]
        public void RequiresAnalysisTdfAndTdfBin(string file, string renameTo)
        {
            string path = Path.Combine(_d, file);
            if (renameTo == null)
            {
                File.Delete(path);
            }
            else
            {
                File.Move(path, Path.Combine(_d, renameTo));
            }

            Assert.False(ConversionRunner.IsTimsTofInput(_d));
            foreach (OutputFormat format in new[] { OutputFormat.DTar, OutputFormat.MS2 })
            {
                FileConversionResult result = ConversionRunner.ConvertFiles(new[] { _d },
                    new ConversionOptions { OutputFormats = format })[0];
                Assert.IsType<ArgumentException>(result.Error);
                Assert.Contains("analysis.tdf and analysis.tdf_bin", result.Error.Message);
            }
            Assert.False(File.Exists(Path.Combine(_dir, "sample.d.tar")));
        }

        [Fact]
        public void AnalysisFileNamesAreCaseInsensitive()
        {
            File.Move(Path.Combine(_d, "analysis.tdf"), Path.Combine(_d, "Analysis.TDF"));

            Assert.True(ConversionRunner.IsTimsTofInput(_d));
        }

        [Fact]
        public void CancellingLeavesNoArchive()
        {
            using CancellationTokenSource cts = new CancellationTokenSource();
            cts.Cancel();

            FileConversionResult result = ConversionRunner.ConvertFile(_d, 0, 1,
                new ConversionOptions { OutputFormats = OutputFormat.DTar }, null, cts.Token);

            Assert.True(result.Cancelled);
            Assert.False(File.Exists(Path.Combine(_dir, "sample.d.tar")));
            AssertNoTemporaryFiles(_dir);
        }

        [Fact]
        public void OnlyDTarForOtherInputIsAnError()
        {
            string raw = Path.Combine(_dir, "a.mgf");
            File.WriteAllText(raw, "");

            Assert.IsType<ArgumentException>(Pack(raw, null).Error);
        }

        private static FileConversionResult Pack(string input, string outFolder)
        {
            return ConversionRunner.ConvertFiles(new[] { input },
                new ConversionOptions { OutputFormats = OutputFormat.DTar, OutputFolder = outFolder })[0];
        }

        private static Dictionary<string, string> ReadArchive(string path)
        {
            Dictionary<string, string> entries = new Dictionary<string, string>();
            using FileStream file = File.OpenRead(path);
            using TarReader reader = new TarReader(file);
            TarEntry entry;
            while ((entry = reader.GetNextEntry()) != null)
            {
                entries[entry.Name] = entry.DataStream == null ? null : new StreamReader(entry.DataStream).ReadToEnd();
            }
            return entries;
        }

        private static void AssertNoTemporaryFiles(string folder)
        {
            Assert.DoesNotContain(Directory.GetFiles(folder), f => Path.GetFileName(f).Contains(".writing-"));
        }
    }
}
