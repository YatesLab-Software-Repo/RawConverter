using System;
using System.IO;
using Xunit;

namespace RawConverter.Tests
{
    public class BatchProgressTests
    {
        private TimeSpan _now;

        private BatchProgress Make(params long[] sizes)
        {
            return new BatchProgress(sizes, () => _now);
        }

        [Fact]
        public void WeighsFilesBySize()
        {
            BatchProgress batch = Make(300, 100);

            batch.Report(1, 100);   // the small file done: a quarter of the work
            Assert.Equal(25, batch.Percent);

            batch.Report(0, 50);
            Assert.Equal(62, batch.Percent);
            Assert.Equal(1, batch.FilesDone);
        }

        [Fact]
        public void UnknownSizesCountAsAverage()
        {
            BatchProgress batch = Make(200, 0, 0);   // 0 = unknown: counts as 200

            batch.Report(1, 100);
            Assert.Equal(33, batch.Percent);
        }

        [Fact]
        public void ProgressNeverGoesBack()
        {
            BatchProgress batch = Make(1);
            batch.Report(0, 60);
            batch.Report(0, 40);
            batch.Report(0, 250);

            Assert.Equal(100, batch.Percent);
        }

        [Fact]
        public void EstimatesTimeLeftFromElapsed()
        {
            BatchProgress batch = Make(100, 100);

            _now = TimeSpan.FromSeconds(3);
            batch.Report(0, 50);
            Assert.Null(batch.Remaining);    // too early

            _now = TimeSpan.FromMinutes(2);
            Assert.Equal(TimeSpan.FromMinutes(6), batch.Remaining);   // a quarter done in 2 min

            batch.Report(0, 100);
            batch.Report(1, 100);
            Assert.Equal(TimeSpan.Zero, batch.Remaining);
        }

        [Fact]
        public void NoEstimateBeforeTwoPercent()
        {
            BatchProgress batch = Make(1);
            _now = TimeSpan.FromMinutes(10);
            batch.Report(0, 1);

            Assert.Null(batch.Remaining);
        }

        [Theory]
        [InlineData(-1, "estimating time left...")]
        [InlineData(40, "less than a minute left")]
        [InlineData(61, "about 2 min left")]
        [InlineData(9 * 60 + 10, "about 10 min left")]
        [InlineData(23 * 60, "about 25 min left")]
        [InlineData(60 * 60, "about 1 h left")]
        [InlineData(87 * 60, "about 1 h 25 min left")]
        public void FormatsTimeLeft(int seconds, string text)
        {
            Assert.Equal(text, BatchProgress.FormatRemaining(seconds < 0 ? null : TimeSpan.FromSeconds(seconds)));
        }

        [Theory]
        [InlineData(0.4, "1 s")]
        [InlineData(42, "42 s")]
        [InlineData(252, "4 min 12 s")]
        [InlineData(3900, "1 h 5 min")]
        public void FormatsDuration(double seconds, string text)
        {
            Assert.Equal(text, BatchProgress.FormatDuration(TimeSpan.FromSeconds(seconds)));
        }

        [Fact]
        public void MeasuresFilesAndFolders()
        {
            string dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            try
            {
                string d = Path.Combine(dir, "sample.d");
                Directory.CreateDirectory(Path.Combine(d, "sub"));
                File.WriteAllBytes(Path.Combine(d, "analysis.tdf"), new byte[100]);
                File.WriteAllBytes(Path.Combine(d, "sub", "x"), new byte[50]);
                string raw = Path.Combine(dir, "a.raw");
                File.WriteAllBytes(raw, new byte[30]);

                string link = Path.Combine(dir, "link.raw");
                File.CreateSymbolicLink(link, raw);

                Assert.Equal(150, BatchProgress.InputSize(d));
                Assert.Equal(30, BatchProgress.InputSize(raw));
                Assert.Equal(30, BatchProgress.InputSize(link));   // the linked file's size, not the link's
                Assert.Equal(0, BatchProgress.InputSize(Path.Combine(dir, "missing.raw")));
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }
    }
}
