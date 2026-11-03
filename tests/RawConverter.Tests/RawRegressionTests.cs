using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using RawConverter.Cli;
using Xunit;

namespace RawConverter.Tests
{
    /// <summary>
    /// Converts real data files and compares the outputs with hashes recorded from a reference build
    /// (Regression/cases.json). Data files are looked up in $RAWCONVERTER_TEST_DATA, or test_data/ at the
    /// repository root; cases whose inputs are missing are skipped. See tests/README.md.
    /// </summary>
    public class RawRegressionTests
    {
        public static TheoryData<string> CaseNames()
        {
            return new TheoryData<string>(LoadCases().Select(c => c.Name));
        }

        [Theory]
        [MemberData(nameof(CaseNames))]
        public void OutputMatchesReference(string caseName)
        {
            RegressionCase rc = LoadCases().Single(c => c.Name == caseName);
            string dataDir = DataDirectory();
            List<string> inputs = rc.Inputs.Select(i => Path.GetFullPath(Path.Combine(dataDir ?? "", i))).ToList();
            if (dataDir == null || !inputs.All(i => File.Exists(i) || Directory.Exists(i)))
            {
                Assert.Skip("Test data not found: " + string.Join(", ", rc.Inputs)
                    + " (set RAWCONVERTER_TEST_DATA or populate test_data/)");
            }

            string outDir = Path.Combine(Path.GetTempPath(), "RawConverterRegression_" + caseName + "_" + Guid.NewGuid().ToString("N"));
            CommandLine cl = CommandLine.Parse(inputs.Concat(rc.Args).ToList());
            Assert.Null(cl.Error);
            cl.Options.OutputFolder = outDir;

            IReadOnlyList<FileConversionResult> results = ConversionRunner.ConvertFiles(cl.InputFiles, cl.Options);
            Assert.All(results, r => Assert.True(r.Succeeded, r.Error?.ToString()));

            Dictionary<string, string> actual = Directory.GetFiles(outDir)
                .ToDictionary(Path.GetFileName, f => NormalizedHash(f, dataDir));
            bool matches = actual.Count == rc.Outputs.Count
                && rc.Outputs.All(kv => actual.TryGetValue(kv.Key, out string hash) && hash == kv.Value);
            if (!matches)
            {
                // keep the outputs for diffing against the reference;
                Assert.Fail("Outputs differ from the reference. Kept in " + outDir + Environment.NewLine
                    + "Actual: " + JsonSerializer.Serialize(actual, new JsonSerializerOptions { WriteIndented = true }));
            }
            Directory.Delete(outDir, true);
        }

        /// <summary>
        /// SHA-256 of an output file with run-specific content removed: creation/conversion dates, the input
        /// folder path, and mzXML byte offsets and checksums (which shift with the path length).
        /// </summary>
        internal static string NormalizedHash(string file, string inputDir)
        {
            string inputDirFull = Path.GetFullPath(inputDir).TrimEnd(Path.DirectorySeparatorChar);
            using IncrementalHash sha = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
            foreach (string line in File.ReadLines(file))
            {
                if (line.StartsWith("H\tCreation Date") || line.StartsWith("H\tDate Converted") || line.Contains("<offset id=")
                    || line.Contains("<indexOffset>") || line.Contains("<sha1>"))
                {
                    continue;
                }
                sha.AppendData(Encoding.UTF8.GetBytes(line.Replace(inputDirFull, "{INPUT_DIR}") + "\n"));
            }
            return Convert.ToHexString(sha.GetHashAndReset()).ToLowerInvariant();
        }

        internal static string DataDirectory()
        {
            string dir = Environment.GetEnvironmentVariable("RAWCONVERTER_TEST_DATA");
            if (!string.IsNullOrEmpty(dir))
            {
                return dir;
            }
            for (DirectoryInfo d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            {
                if (File.Exists(Path.Combine(d.FullName, "RawConverter.sln")))
                {
                    string testData = Path.Combine(d.FullName, "test_data");
                    return Directory.Exists(testData) ? testData : null;
                }
            }
            return null;
        }

        internal static List<RegressionCase> LoadCases()
        {
            string json = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Regression", "cases.json"));
            return JsonSerializer.Deserialize<RegressionCaseFile>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true }).Cases;
        }

        internal class RegressionCaseFile
        {
            public List<RegressionCase> Cases { get; set; }
        }

        internal class RegressionCase
        {
            public string Name { get; set; }
            public List<string> Inputs { get; set; }
            public List<string> Args { get; set; }
            public Dictionary<string, string> Outputs { get; set; }
        }
    }
}
