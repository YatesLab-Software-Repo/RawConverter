using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Xml;
using Xunit;

namespace RawConverter.Tests
{
    public class TimsTofTests : IDisposable
    {
        private const string DiaDataset = "061925_CPP-SCP_E_5_6_5ul_dia_S2-E10_1_383.d";
        private const string DdaDataset = "200ngHeLaPASEF_1min.d";

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
        public void RejectsTimsTofWithoutMs2OrMzXmlOutput(OutputFormat formats)
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
    
        [Fact]
        public void MzXmlMatchesMs2()
        {
            string dataDir = RawRegressionTests.DataDirectory();
            string dda = dataDir == null ? null : Path.Combine(dataDir, DdaDataset);
            if (dda == null || !Directory.Exists(dda))
            {
                Assert.Skip("Test data not found: " + DdaDataset);
            }

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { dda },
                new ConversionOptions { OutputFormats = OutputFormat.MS2 | OutputFormat.MzXML, OutputFolder = _dir })[0];
            Assert.True(result.Succeeded, result.Error?.ToString());

            Dictionary<int, Ms2Scan> ms2 = ReadMs2(Path.Combine(_dir, "200ngHeLaPASEF_1min.ms2"));
            List<MzXmlScan> scans = ReadMzXml(Path.Combine(_dir, "200ngHeLaPASEF_1min.mzXML"), out int scanCount);

            Assert.Equal(scanCount, scans.Count);
            Assert.Equal(scans.Select(s => s.Num).OrderBy(n => n), scans.Select(s => s.Num));
            Assert.All(scans.Where(s => s.MsLevel == 1), s => Assert.True(s.Mz.Length > 0 && s.Filter == "TIMSTOF + MS1"));

            // every MS2 scan of the MS2 file is in the mzXML file, after its MS1 scan; the others are empty;
            List<MzXmlScan> ms2Scans = scans.Where(s => s.MsLevel == 2).ToList();
            Assert.Equal(ms2.Count, ms2Scans.Count(s => s.Mz.Length > 0));
            int lastMs1 = 0;
            foreach (MzXmlScan scan in scans)
            {
                if (scan.MsLevel == 1)
                {
                    lastMs1 = scan.Num;
                    continue;
                }
                Assert.Equal(lastMs1, scan.PrecursorScanNum);
                if (!ms2.TryGetValue(scan.Num, out Ms2Scan expected))
                {
                    Assert.Empty(scan.Mz);
                    continue;
                }
                Assert.Equal(expected.PrecursorMz, scan.PrecursorMz, 4);
                Assert.Equal(expected.Charge, scan.Charge);
                Assert.Equal("TIMSTOF + MS2 1/K0=" + expected.IonMobility, scan.Filter);
                Assert.Equal(expected.Mz.Length, scan.Mz.Length);
                for (int i = 0; i < scan.Mz.Length; i++)
                {
                    // 32-bit floats in mzXML, 4 and 1 decimal places in the MS2 file;
                    Assert.Equal(expected.Mz[i], scan.Mz[i], 0.0003);
                    Assert.Equal(expected.Intensity[i], scan.Intensity[i], Math.Max(0.06, expected.Intensity[i] * 1e-6));
                }
            }
        }

        private class Ms2Scan
        {
            public double PrecursorMz;
            public int Charge;
            public string IonMobility;
            public double[] Mz;
            public double[] Intensity;
        }

        private class MzXmlScan
        {
            public int Num;
            public int MsLevel;
            public string Filter;
            public int PrecursorScanNum;
            public double PrecursorMz;
            public int Charge;
            public double[] Mz;
            public double[] Intensity;
        }

        private static Dictionary<int, Ms2Scan> ReadMs2(string file)
        {
            Dictionary<int, Ms2Scan> scans = new Dictionary<int, Ms2Scan>();
            Ms2Scan scan = null;
            List<double> mz = null, intensity = null;
            foreach (string line in File.ReadLines(file).Append("S"))
            {
                string[] f = line.Split('\t', ' ');
                if (f[0] == "S")
                {
                    if (scan != null)
                    {
                        scan.Mz = mz.ToArray();
                        scan.Intensity = intensity.ToArray();
                    }
                    if (f.Length > 1)
                    {
                        scan = new Ms2Scan { PrecursorMz = Parse(f[3]) };
                        scans[int.Parse(f[1], CultureInfo.InvariantCulture)] = scan;
                        mz = new List<double>();
                        intensity = new List<double>();
                    }
                }
                else if (line.StartsWith("I\tIon Mobility\t"))
                {
                    scan.IonMobility = line.Substring("I\tIon Mobility\t".Length);
                }
                else if (f[0] == "Z")
                {
                    scan.Charge = int.Parse(f[1], CultureInfo.InvariantCulture);
                }
                else if (scan != null && f[0] != "H" && f[0] != "I")
                {
                    mz.Add(Parse(f[0]));
                    intensity.Add(Parse(f[1]));
                }
            }
            return scans;
        }

        private static List<MzXmlScan> ReadMzXml(string file, out int scanCount)
        {
            List<MzXmlScan> scans = new List<MzXmlScan>();
            scanCount = 0;
            MzXmlScan scan = null;
            using XmlReader r = XmlReader.Create(file);
            while (r.Read())
            {
                if (r.NodeType != XmlNodeType.Element)
                {
                    continue;
                }
                switch (r.Name)
                {
                    case "msRun":
                        scanCount = int.Parse(r.GetAttribute("scanCount"), CultureInfo.InvariantCulture);
                        break;
                    case "scan":
                        scan = new MzXmlScan
                        {
                            Num = int.Parse(r.GetAttribute("num"), CultureInfo.InvariantCulture),
                            MsLevel = int.Parse(r.GetAttribute("msLevel"), CultureInfo.InvariantCulture),
                            Filter = r.GetAttribute("filterLine"),
                        };
                        scans.Add(scan);
                        break;
                    case "precursorMz":
                        scan.PrecursorScanNum = int.Parse(r.GetAttribute("precursorScanNum"), CultureInfo.InvariantCulture);
                        scan.Charge = int.Parse(r.GetAttribute("precursorCharge"), CultureInfo.InvariantCulture);
                        scan.PrecursorMz = Parse(r.ReadElementContentAsString());
                        break;
                    case "peaks":
                        byte[] bytes = Convert.FromBase64String(r.ReadElementContentAsString());
                        int n = bytes.Length / 8;
                        scan.Mz = new double[n];
                        scan.Intensity = new double[n];
                        for (int i = 0; i < n; i++)
                        {
                            scan.Mz[i] = ReadBigEndianFloat(bytes, 8 * i);
                            scan.Intensity[i] = ReadBigEndianFloat(bytes, 8 * i + 4);
                        }
                        break;
                }
            }
            return scans;
        }

        private static float ReadBigEndianFloat(byte[] bytes, int offset)
        {
            return BitConverter.Int32BitsToSingle(System.Buffers.Binary.BinaryPrimitives.ReadInt32BigEndian(bytes.AsSpan(offset, 4)));
        }

        private static double Parse(string s)
        {
            return double.Parse(s, CultureInfo.InvariantCulture);
        }
    }
}
