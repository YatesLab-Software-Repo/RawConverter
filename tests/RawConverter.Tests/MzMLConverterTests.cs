using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Linq;
using RawConverter.Converter;
using Xunit;

namespace RawConverter.Tests
{
    public class MzMLConverterTests : IDisposable
    {
        private readonly string _dir;

        public MzMLConverterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "RawConverterTests_" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            Directory.Delete(_dir, true);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void DecodesBinaryData(bool is32Bit, bool zlib)
        {
            double[] values = { 100.5, 1234.25, 0.125 };

            double[] decoded = MzMLConverter.DecodeBinaryData(Encode(values, is32Bit, zlib), zlib, is32Bit);

            Assert.Equal(values, decoded);
        }

        [Fact]
        public void ConvertsMixedPrecisionZlibMzML()
        {
            // msconvert's default: 64-bit m/z, 32-bit intensities, zlib compressed;
            string mzml = Path.Combine(_dir, "mixed.mzML");
            File.WriteAllText(mzml, MzML(
                Spectrum(0, 1, new[] { 400.123456789, 500.25 }, new[] { 1000.5, 2000.25 }),
                Spectrum(1, 2, new[] { 150.0625, 250.125, 350.5 }, new[] { 10.5, 20.25, 30.5 }, precursorMz: 400.123456789, charge: 2)));

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { mzml },
                new ConversionOptions { OutputFormats = OutputFormat.MS1 | OutputFormat.MS2 })[0];

            Assert.True(result.Succeeded, result.Error?.ToString());
            string[] ms1 = File.ReadAllLines(Path.Combine(_dir, "mixed.ms1"));
            // peak lines end with the noise and baseline columns, which mzML does not provide;
            Assert.Equal(new[] { "400.1235 1000.5 0 0", "500.25 2000.2 0 0" }, ms1.Where(l => char.IsDigit(l[0])));
            string[] ms2 = File.ReadAllLines(Path.Combine(_dir, "mixed.ms2"));
            Assert.Equal(new[] { "150.0625 10.5 0 0", "250.125 20.2 0 0", "350.5 30.5 0 0" }, ms2.Where(l => char.IsDigit(l[0])));
        }

        [Fact]
        public void RejectsNumpress()
        {
            string mzml = Path.Combine(_dir, "numpress.mzML");
            File.WriteAllText(mzml, MzML(Spectrum(0, 1, new[] { 400.0 }, new[] { 1000.0 }))
                .Replace("name=\"zlib compression\"", "name=\"MS-Numpress linear prediction compression\""));

            FileConversionResult result = ConversionRunner.ConvertFiles(new[] { mzml },
                new ConversionOptions { OutputFormats = OutputFormat.MS1 })[0];

            Assert.False(result.Succeeded);
            Assert.IsType<NotSupportedException>(result.Error);
        }

        private static byte[] Encode(double[] values, bool is32Bit, bool zlib)
        {
            int width = is32Bit ? 4 : 8;
            byte[] bytes = new byte[values.Length * width];
            for (int i = 0; i < values.Length; i++)
            {
                if (is32Bit)
                {
                    BinaryPrimitives.WriteSingleLittleEndian(bytes.AsSpan(i * width), (float)values[i]);
                }
                else
                {
                    BinaryPrimitives.WriteDoubleLittleEndian(bytes.AsSpan(i * width), values[i]);
                }
            }
            if (!zlib)
            {
                return bytes;
            }
            using MemoryStream compressed = new MemoryStream();
            using (ZLibStream stream = new ZLibStream(compressed, CompressionLevel.Optimal, true))
            {
                stream.Write(bytes, 0, bytes.Length);
            }
            return compressed.ToArray();
        }

        private static string BinaryArray(double[] values, bool is32Bit, string arrayName)
        {
            string precision = is32Bit
                ? "<cvParam cvRef=\"MS\" accession=\"MS:1000521\" name=\"32-bit float\" value=\"\"/>"
                : "<cvParam cvRef=\"MS\" accession=\"MS:1000523\" name=\"64-bit float\" value=\"\"/>";
            string data = Convert.ToBase64String(Encode(values, is32Bit, true));
            return "<binaryDataArray encodedLength=\"" + data.Length + "\">" + precision
                + "<cvParam cvRef=\"MS\" accession=\"MS:1000574\" name=\"zlib compression\" value=\"\"/>"
                + "<cvParam cvRef=\"MS\" name=\"" + arrayName + "\" value=\"\"/>"
                + "<binary>" + data + "</binary></binaryDataArray>";
        }

        private static string Spectrum(int index, int msLevel, double[] mz, double[] intensity, double precursorMz = 0, int charge = 0)
        {
            string precursor = msLevel == 1 ? "" :
                "<precursorList count=\"1\"><precursor><selectedIonList count=\"1\"><selectedIon>"
                + "<cvParam cvRef=\"MS\" accession=\"MS:1000744\" name=\"selected ion m/z\" value=\"" + precursorMz + "\"/>"
                + "<cvParam cvRef=\"MS\" accession=\"MS:1000041\" name=\"charge state\" value=\"" + charge + "\"/>"
                + "</selectedIon></selectedIonList></precursor></precursorList>";
            return "<spectrum index=\"" + index + "\" id=\"scan=" + (index + 1) + "\" defaultArrayLength=\"" + mz.Length + "\">"
                + "<cvParam cvRef=\"MS\" accession=\"MS:1000511\" name=\"ms level\" value=\"" + msLevel + "\"/>"
                + "<scanList count=\"1\"><scan><cvParam cvRef=\"MS\" accession=\"MS:1000016\" name=\"scan start time\" value=\"" + (index + 1) + "\"/></scan></scanList>"
                + precursor
                + "<binaryDataArrayList count=\"2\">"
                + BinaryArray(mz, false, "m/z array") + BinaryArray(intensity, true, "intensity array")
                + "</binaryDataArrayList></spectrum>";
        }

        private static string MzML(params string[] spectra)
        {
            return "<?xml version=\"1.0\" encoding=\"utf-8\"?>\n"
                + "<mzML xmlns=\"http://psi.hupo.org/ms/mzml\" version=\"1.1.0\">"
                + "<run id=\"test\"><spectrumList count=\"" + spectra.Length + "\">" + string.Concat(spectra)
                + "</spectrumList></run></mzML>\n";
        }
    }
}
