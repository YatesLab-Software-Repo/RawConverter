using System;
using System.IO;
using Xunit;

namespace RawConverter.Tests
{
    public class TimsTofTests : IDisposable
    {
        private const string DiaDataset = "061925_CPP-SCP_E_5_6_5ul_dia_S2-E10_1_383.d";

        private readonly string _dir;

        public TimsTofTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public void RecognisesTimsTofFolders()
        {
            string d = Path.Combine(_dir, "sample.d");
            Directory.CreateDirectory(d);
            Assert.False(ConversionRunner.IsTimsTofInput(d));

            File.WriteAllText(Path.Combine(d, "analysis.tdf"), "");
            File.WriteAllText(Path.Combine(d, "analysis.tdf_bin"), "");
            Assert.True(ConversionRunner.IsTimsTofInput(d));
            Assert.False(ConversionRunner.IsTimsTofInput(Path.Combine(d, "analysis.tdf")));
        }

        [Fact]
        public void RejectsFolderThatIsNotTimsTof()
        {
            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { _dir },
                new ConversionOptions { OutputFormats = OutputFormat.MS2 })[0];

            Assert.False(result.Succeeded);
            Assert.IsType<ArgumentException>(result.Error);
        }

        [Theory]
        [InlineData(OutputFormat.MGF)]
        [InlineData(OutputFormat.MS1)]
        public void RejectsTimsTofWithoutMs2Output(OutputFormat formats)
        {
            string d = Path.Combine(_dir, "sample.d");
            Directory.CreateDirectory(d);
            File.WriteAllText(Path.Combine(d, "analysis.tdf"), "");
            File.WriteAllText(Path.Combine(d, "analysis.tdf_bin"), "");

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { d },
                new ConversionOptions { OutputFormats = formats })[0];

            Assert.False(result.Succeeded);
            Assert.IsType<ArgumentException>(result.Error);
        }

        [Fact]
        public void RejectsDiaData()
        {
            string dataDir = RawRegressionTests.DataDirectory();
            string dia = dataDir == null ? null : Path.Combine(dataDir, DiaDataset);
            if (dia == null || !Directory.Exists(dia))
            {
                Assert.Skip("Test data not found: " + DiaDataset);
            }

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { dia },
                new ConversionOptions { OutputFormats = OutputFormat.MS2, OutputFolder = _dir })[0];

            Assert.False(result.Succeeded);
            Assert.IsType<NotSupportedException>(result.Error);
            Assert.Empty(Directory.GetFiles(_dir));
        }
    }
}
