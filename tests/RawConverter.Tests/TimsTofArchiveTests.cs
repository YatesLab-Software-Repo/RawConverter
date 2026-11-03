using System;
using System.Formats.Tar;
using System.IO;
using System.IO.Compression;
using System.Linq;
using Xunit;

namespace RawConverter.Tests
{
    public class TimsTofArchiveTests : IDisposable
    {
        private const string HeLaArchive = "200ngHeLaPASEF_1min.d.tar";

        private readonly string _dir;

        public TimsTofArchiveTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Theory]
        [InlineData("sample.d.tar", false)]
        [InlineData("sample.d.tar.gz", true)]
        [InlineData("sample.d.tgz", true)]
        public void ExtractsDFolderNextToArchive(string archiveName, bool gzip)
        {
            string archive = WriteArchive(archiveName, gzip, w =>
            {
                // some instrument archives store read-only directories;
                AddDirectory(w, "sample.d/", UnixFileMode.UserRead | UnixFileMode.UserExecute);
                AddDirectory(w, "sample.d/sample.m/", UnixFileMode.UserRead | UnixFileMode.UserExecute);
                AddFile(w, "sample.d/analysis.tdf", "tdf");
                AddFile(w, "sample.d/analysis.tdf_bin", "bin");
                AddFile(w, "sample.d/sample.m/method.txt", "method");
            });

            // the stand-in analysis.tdf is not a database, so conversion fails after extraction;
            FileConversionResult result = Convert(archive);

            Assert.False(result.Succeeded);
            string d = Path.Combine(_dir, "sample.d");
            Assert.Equal("tdf", File.ReadAllText(Path.Combine(d, "analysis.tdf")));
            Assert.Equal("method", File.ReadAllText(Path.Combine(d, "sample.m", "method.txt")));
            File.WriteAllText(Path.Combine(d, "sample.m", "writable.txt"), "");
            AssertNoTemporaryFolders();
        }

        [Theory]
        [InlineData("sample.d/../escape.txt")]
        [InlineData("/sample.d/analysis.tdf")]
        public void RejectsUnsafeEntries(string entryName)
        {
            string archive = WriteArchive("sample.d.tar", false, w =>
            {
                AddFile(w, "sample.d/analysis.tdf", "tdf");
                AddFile(w, entryName, "x");
            });

            FileConversionResult result = Convert(archive);

            Assert.False(result.Succeeded);
            Assert.IsType<InvalidDataException>(result.Error);
            Assert.False(File.Exists(Path.Combine(_dir, "escape.txt")));
            Assert.False(Directory.Exists(Path.Combine(_dir, "sample.d")));
            AssertNoTemporaryFolders();
        }

        [Fact]
        public void RejectsSeveralTopLevelEntries()
        {
            string archive = WriteArchive("two.tar", false, w =>
            {
                AddFile(w, "a.d/analysis.tdf", "tdf");
                AddFile(w, "b.d/analysis.tdf", "tdf");
            });

            FileConversionResult result = Convert(archive);

            Assert.IsType<InvalidDataException>(result.Error);
            Assert.False(Directory.Exists(Path.Combine(_dir, "a.d")));
            AssertNoTemporaryFolders();
        }

        [Fact]
        public void RejectsArchiveWithoutDFolder()
        {
            string archive = WriteArchive("other.tar", false, w => AddFile(w, "results/file.txt", "x"));

            Assert.IsType<InvalidDataException>(Convert(archive).Error);
            AssertNoTemporaryFolders();
        }

        [Fact]
        public void RejectsDFolderWithoutTdfFiles()
        {
            string archive = WriteArchive("sample.d.tar", false, w => AddFile(w, "sample.d/notes.txt", "x"));

            Assert.IsType<InvalidDataException>(Convert(archive).Error);
            Assert.False(Directory.Exists(Path.Combine(_dir, "sample.d")));
            AssertNoTemporaryFolders();
        }

        [Fact]
        public void RefusesIncompleteExistingDFolder()
        {
            string archive = WriteArchive("sample.d.tar", false, w => AddFile(w, "sample.d/analysis.tdf", "tdf"));
            Directory.CreateDirectory(Path.Combine(_dir, "sample.d"));

            Assert.IsType<IOException>(Convert(archive).Error);
        }

        [Fact]
        public void ConvertsRealArchiveNextToIt()
        {
            string dataDir = RawRegressionTests.DataDirectory();
            string source = dataDir == null ? null : Path.Combine(dataDir, HeLaArchive);
            if (source == null || !File.Exists(source))
            {
                Assert.Skip("Test data not found: " + HeLaArchive);
            }
            string archive = Path.Combine(_dir, HeLaArchive);
            File.Copy(source, archive);

            FileConversionResult result = Convert(archive);

            Assert.True(result.Succeeded, result.Error?.ToString());
            Assert.True(ConversionRunner.IsTimsTofInput(Path.Combine(_dir, "200ngHeLaPASEF_1min.d")));
            string ms2 = Path.Combine(_dir, "200ngHeLaPASEF_1min.ms2");
            string expected = RawRegressionTests.LoadCases().Single(c => c.Name == "timstof_dda").Outputs["200ngHeLaPASEF_1min.ms2"];
            Assert.Equal(expected, RawRegressionTests.NormalizedHash(ms2, _dir));

            // a second run uses the extracted folder;
            File.Delete(ms2);
            Assert.True(Convert(archive).Succeeded);
            Assert.True(File.Exists(ms2));
        }

        private FileConversionResult Convert(string archive)
        {
            return ConversionRunner.ConvertFiles(new[] { archive }, new ConversionOptions { OutputFormats = OutputFormat.MS2 })[0];
        }

        private void AssertNoTemporaryFolders()
        {
            Assert.DoesNotContain(Directory.GetDirectories(_dir), d => Path.GetFileName(d).Contains(".extracting-"));
        }

        private string WriteArchive(string name, bool gzip, Action<TarWriter> write)
        {
            string path = Path.Combine(_dir, name);
            using FileStream file = File.Create(path);
            using Stream stream = gzip ? new GZipStream(file, CompressionLevel.Fastest) : file;
            using (TarWriter writer = new TarWriter(stream, TarEntryFormat.Pax, leaveOpen: true))
            {
                write(writer);
            }
            return path;
        }

        private static void AddDirectory(TarWriter writer, string name, UnixFileMode mode)
        {
            writer.WriteEntry(new PaxTarEntry(TarEntryType.Directory, name) { Mode = mode });
        }

        private static void AddFile(TarWriter writer, string name, string content)
        {
            PaxTarEntry entry = new PaxTarEntry(TarEntryType.RegularFile, name)
            {
                Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite,
                DataStream = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(content)),
            };
            writer.WriteEntry(entry);
        }
    }
}
