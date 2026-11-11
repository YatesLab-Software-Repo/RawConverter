using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Parquet;
using Parquet.Schema;
using Xunit;

namespace RawConverter.Tests
{
    public class MzParquetTests : IDisposable
    {
        private static readonly string[] Columns = { "scan", "level", "rt", "mz", "intensity", "ion_mobility", "isolation_lower",
            "isolation_upper", "precursor_scan", "precursor_mz", "precursor_charge" };

        private readonly string _dir;

        public MzParquetTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Fact]
        public async Task ConvertsMs2Input()
        {
            string ms2 = Path.Combine(_dir, "test.ms2");
            File.WriteAllText(ms2, "H\tExtractor\tRawConverter\n"
                + "S\t000002\t000002\t500.2500\n"
                + "I\tIon Mobility\t0.9876\n"
                + "I\tRetTime\t12.50\n"
                + "I\tPrecursorScan\t1\n"
                + "Z\t2\t999.4927\n"
                + "Z\t3\t1498.7354\n"
                + "250.5000 20.6\n"
                + "150.1250 10.4\n"
                + "S\t000003\t000003\t600.7500\n"
                + "I\tRetTime\t13.50\n"
                + "Z\t3\t1800.2354\n"
                + "350.5000 30.5\n");

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { ms2 },
                new ConversionOptions { OutputFormats = OutputFormat.MzParquet, OutputFolder = _dir })[0];
            Assert.True(result.Succeeded, result.Error?.ToString());

            (List<Row> rows, ParquetSchema schema) = await Read(Path.Combine(_dir, "test.mzparquet"));

            Assert.Equal(Columns, schema.DataFields.Select(f => f.Name));
            Assert.Equal(3, rows.Count);
            // peaks are sorted by m/z, intensities rounded; the first charge state is the precursor;
            Row first = rows[0];
            Assert.Equal((2u, 2u, 12.5f, 150.125f, 10u), (first.Scan, first.Level, first.Rt, first.Mz, first.Intensity));
            Assert.Equal(0.9876f, first.IonMobility);
            Assert.Equal(1u, first.PrecursorScan);
            Assert.Equal(500.25, first.PrecursorMz.Value, 3);
            Assert.Equal(2u, first.PrecursorCharge);
            Assert.Null(first.IsolationLower);
            Assert.Equal(21u, rows[1].Intensity);
            Assert.Equal((3u, 13.5f, 3u), (rows[2].Scan, rows[2].Rt, rows[2].PrecursorCharge.Value));
            Assert.Null(rows[2].IonMobility);
            Assert.Null(rows[2].PrecursorScan);
        }

        [Fact]
        public async Task TimsTofMatchesMs2()
        {
            string dataDir = RawRegressionTests.DataDirectory();
            string dda = dataDir == null ? null : Path.Combine(dataDir, "200ngHeLaPASEF_1min.d");
            if (dda == null || !Directory.Exists(dda))
            {
                Assert.Skip("Test data not found: 200ngHeLaPASEF_1min.d");
            }

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { dda },
                new ConversionOptions { OutputFormats = OutputFormat.MS2 | OutputFormat.MzParquet, OutputFolder = _dir })[0];
            Assert.True(result.Succeeded, result.Error?.ToString());

            // the MS1 rows (22.9 million raw peaks) are summarised rather than kept;
            Ms1Summary ms1 = new Ms1Summary();
            List<Row> rows = (await Read(Path.Combine(_dir, "200ngHeLaPASEF_1min.mzparquet"), ms1)).Rows;
            Dictionary<int, Ms2Spectrum> ms2 = ReadMs2(Path.Combine(_dir, "200ngHeLaPASEF_1min.ms2"));

            // MS1 frames are centroided, without mobility; each MS2 scan follows its MS1 scan;
            // MS1 rows are the frames' raw peaks, each with the 1/K0 of its mobility scan, which falls with the scan number;
            Assert.Equal(MS1PeakCounts(dda), ms1.PeakCounts.Values);
            Assert.Equal(0, ms1.MissingMobility);
            Assert.Equal(0, ms1.MobilityIncreases);
            Assert.Equal(0, ms1.WithPrecursor);
            Assert.InRange(ms1.MinMobility, 0.4f, ms1.MaxMobility);
            Assert.InRange(ms1.MaxMobility, ms1.MinMobility, 2.0f);
            HashSet<uint> ms1Scans = ms1.PeakCounts.Keys.ToHashSet();

            CompareMs2(rows, ms2, rtScale: 1);
            foreach (IGrouping<uint, Row> scan in rows.Where(r => r.Level == 2).GroupBy(r => r.Scan))
            {
                Row row = scan.First();
                Assert.Equal(double.Parse(ms2[(int)scan.Key].IonMobility, CultureInfo.InvariantCulture), row.IonMobility.Value, 4);
                Assert.Contains(row.PrecursorScan.Value, ms1Scans);
                Assert.True(row.PrecursorScan < row.Scan);
                Assert.True(row.IsolationLower < row.PrecursorMz + 1 && row.IsolationUpper > row.PrecursorMz);
            }
        }

        [Fact]
        public async Task RawMatchesMs2()
        {
            string dataDir = RawRegressionTests.DataDirectory();
            string raw = dataDir == null ? null : Path.Combine(dataDir, "ExoExpt3s01.raw");
            if (raw == null || !File.Exists(raw))
            {
                Assert.Skip("Test data not found: ExoExpt3s01.raw");
            }

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { raw },
                new ConversionOptions { OutputFormats = OutputFormat.MS2 | OutputFormat.MzParquet, OutputFolder = _dir })[0];
            Assert.True(result.Succeeded, result.Error?.ToString());

            List<Row> rows = (await Read(Path.Combine(_dir, "ExoExpt3s01.mzparquet"))).Rows;
            Assert.Contains(rows, r => r.Level == 1);
            Assert.All(rows, r => Assert.Null(r.IonMobility));
            CompareMs2(rows, ReadMs2(Path.Combine(_dir, "ExoExpt3s01.ms2")), rtScale: 1);
            Assert.All(rows.Where(r => r.Level == 2), r => Assert.True(r.IsolationLower < r.IsolationUpper));
        }

        /// <summary>
        /// The MS2 rows hold the same scans, peaks and precursors as the .ms2 file (which rounds to 4 decimals,
        /// and retention times to 2).
        /// </summary>
        private static void CompareMs2(List<Row> rows, Dictionary<int, Ms2Spectrum> ms2, double rtScale)
        {
            List<IGrouping<uint, Row>> scans = rows.Where(r => r.Level == 2).GroupBy(r => r.Scan).ToList();
            Assert.Equal(ms2.Where(kv => kv.Value.Mz.Count > 0).Select(kv => (uint)kv.Key).OrderBy(s => s), scans.Select(g => g.Key).OrderBy(s => s));
            foreach (IGrouping<uint, Row> scan in scans)
            {
                Ms2Spectrum expected = ms2[(int)scan.Key];
                List<Row> peaks = scan.ToList();
                Assert.Equal(expected.Mz.Count, peaks.Count);
                for (int i = 0; i < peaks.Count; i++)
                {
                    Assert.True(Math.Abs(peaks[i].Mz - expected.Mz[i]) < 3e-4, "m/z of scan " + scan.Key);
                    Assert.True(Math.Abs(peaks[i].Intensity - expected.Intensity[i]) <= 0.5 + 1e-6 * expected.Intensity[i],
                        "intensity of scan " + scan.Key);
                }
                Assert.Equal(expected.RetTime * rtScale, peaks[0].Rt, 0.006);
                Assert.Equal(expected.PrecursorMz, peaks[0].PrecursorMz.Value, 0.001);
                Assert.Equal((uint)expected.Charge, peaks[0].PrecursorCharge.Value);
            }
        }

        private class Ms2Spectrum
        {
            public double RetTime;
            public double PrecursorMz;
            public int Charge;
            public string IonMobility;
            public List<double> Mz = new List<double>();
            public List<double> Intensity = new List<double>();
        }

        private static Dictionary<int, Ms2Spectrum> ReadMs2(string file)
        {
            Dictionary<int, Ms2Spectrum> scans = new Dictionary<int, Ms2Spectrum>();
            Ms2Spectrum scan = null;
            foreach (string line in File.ReadLines(file))
            {
                string[] f = line.Split('\t', ' ');
                if (f[0] == "S")
                {
                    scan = new Ms2Spectrum();
                    scans[int.Parse(f[1], CultureInfo.InvariantCulture)] = scan;
                }
                else if (line.StartsWith("I\tRetTime\t"))
                {
                    scan.RetTime = Parse(f[2]);
                }
                else if (line.StartsWith("I\tIon Mobility\t"))
                {
                    scan.IonMobility = line.Substring("I\tIon Mobility\t".Length);
                }
                else if (f[0] == "Z" && scan.Charge == 0)
                {
                    scan.Charge = int.Parse(f[1], CultureInfo.InvariantCulture);
                    scan.PrecursorMz = (Parse(f[2]) + (scan.Charge - 1) * 1.007276466) / scan.Charge;
                }
                else if (scan != null && line.Length > 0 && char.IsDigit(line[0]))
                {
                    scan.Mz.Add(Parse(f[0]));
                    scan.Intensity.Add(Parse(f[1]));
                }
            }
            return scans;
        }

        private static double Parse(string s) => double.Parse(s, CultureInfo.InvariantCulture);

        private class Row
        {
            public uint Scan;
            public uint Level;
            public float Rt;
            public float Mz;
            public uint Intensity;
            public float? IonMobility;
            public float? IsolationLower;
            public float? IsolationUpper;
            public uint? PrecursorScan;
            public float? PrecursorMz;
            public uint? PrecursorCharge;
        }

        private static List<long> MS1PeakCounts(string analysisDir)
        {
            using SqliteConnection conn = new SqliteConnection("Data Source=" + Path.Combine(analysisDir, "analysis.tdf") + ";Mode=ReadOnly");
            conn.Open();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT NumPeaks FROM Frames WHERE MsMsType = 0 AND NumPeaks > 0 ORDER BY Id";
            using SqliteDataReader r = cmd.ExecuteReader();
            List<long> counts = new List<long>();
            while (r.Read())
            {
                counts.Add(r.GetInt64(0));
            }
            return counts;
        }

        private class Ms1Summary
        {
            public Dictionary<uint, long> PeakCounts = new Dictionary<uint, long>();
            public long MissingMobility;
            public long MobilityIncreases;
            public long WithPrecursor;
            public float MinMobility = float.MaxValue;
            public float MaxMobility = float.MinValue;
            public uint LastScan;
            public float LastMobility;
        }

        /// <param name="ms1">when given, MS1 rows are summarised in it instead of returned</param>
        private static async Task<(List<Row> Rows, ParquetSchema Schema)> Read(string file, Ms1Summary ms1 = null)
        {
            List<Row> rows = new List<Row>();
            await using ParquetReader reader = await ParquetReader.CreateAsync(file);
            DataField[] fields = reader.Schema.DataFields;
            for (int g = 0; g < reader.RowGroupCount; g++)
            {
                using ParquetRowGroupReader rg = reader.OpenRowGroupReader(g);
                int n = (int)rg.RowCount;
                uint[] scan = await Column<uint>(rg, fields[0], n), level = await Column<uint>(rg, fields[1], n);
                float[] rt = await Column<float>(rg, fields[2], n), mz = await Column<float>(rg, fields[3], n);
                uint[] intensity = await Column<uint>(rg, fields[4], n);
                float?[] ionMobility = await NullableColumn<float>(rg, fields[5], n), isolationLower = await NullableColumn<float>(rg, fields[6], n),
                    isolationUpper = await NullableColumn<float>(rg, fields[7], n);
                uint?[] precursorScan = await NullableColumn<uint>(rg, fields[8], n);
                float?[] precursorMz = await NullableColumn<float>(rg, fields[9], n);
                uint?[] precursorCharge = await NullableColumn<uint>(rg, fields[10], n);
                for (int i = 0; i < n; i++)
                {
                    if (ms1 != null && level[i] == 1)
                    {
                        ms1.PeakCounts[scan[i]] = ms1.PeakCounts.GetValueOrDefault(scan[i]) + 1;
                        if (ionMobility[i] is float k0)
                        {
                            ms1.MinMobility = Math.Min(ms1.MinMobility, k0);
                            ms1.MaxMobility = Math.Max(ms1.MaxMobility, k0);
                            if (scan[i] == ms1.LastScan && k0 > ms1.LastMobility)
                            {
                                ms1.MobilityIncreases++;
                            }
                            ms1.LastMobility = k0;
                        }
                        else
                        {
                            ms1.MissingMobility++;
                        }
                        ms1.LastScan = scan[i];
                        if (precursorScan[i] != null || precursorMz[i] != null || isolationLower[i] != null)
                        {
                            ms1.WithPrecursor++;
                        }
                        continue;
                    }
                    rows.Add(new Row
                    {
                        Scan = scan[i], Level = level[i], Rt = rt[i], Mz = mz[i], Intensity = intensity[i], IonMobility = ionMobility[i],
                        IsolationLower = isolationLower[i], IsolationUpper = isolationUpper[i], PrecursorScan = precursorScan[i],
                        PrecursorMz = precursorMz[i], PrecursorCharge = precursorCharge[i],
                    });
                }
            }
            return (rows, reader.Schema);
        }

        private static async Task<T[]> Column<T>(ParquetRowGroupReader rg, DataField field, int n) where T : struct
        {
            T[] values = new T[n];
            await rg.ReadAsync<T>(field, values.AsMemory(), null, TestContext.Current.CancellationToken);
            return values;
        }

        private static async Task<T?[]> NullableColumn<T>(ParquetRowGroupReader rg, DataField field, int n) where T : struct
        {
            T?[] values = new T?[n];
            await rg.ReadAsync<T>(field, values.AsMemory(), null, TestContext.Current.CancellationToken);
            return values;
        }
    }
}
