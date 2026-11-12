using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using RawConverter.Bruker;
using RawConverter.Common;
using RawConverter.MassSpec;

namespace RawConverter.Converter
{
    /// <summary>
    /// Converts Bruker timsTOF DDA-PASEF data (a .d folder with analysis.tdf and analysis.tdf_bin) to MS1, MS2,
    /// mzXML and mzParquet files. MS1 and MS2 are a port of the TimsTOF_extractor script (extract_msn_nopd.py,
    /// version 0.2.4); the output matches it, except that MS1 output works (the script opened an empty file name,
    /// and failed on MS1 frames without peaks). mzXML and mzParquet have the same scan numbers. mzXML MS1 frames
    /// are summed over ion mobility and centroided by the SDK, and the precursor's 1/K0 is in each MS2 scan's
    /// filterLine. mzParquet MS1 rows are the frames' raw peaks, each with its 1/K0, MS2 rows carry the
    /// precursor's 1/K0, and the rt column is in seconds, as dotD2parquet writes it.
    /// </summary>
    internal class TimsTofConverter
    {
        private const double ProtonMass = 1.007276466;
        private const int BatchSize = 1000;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly string _analysisDir;
        private readonly string _tdfFile;
        private readonly string _ms1File;
        private readonly string _ms2File;
        private readonly string _mzXmlFile;
        private readonly string _mzParquetFile;
        private int _progressStart;

        public const string TdfFileName = "analysis.tdf";
        public const string TdfBinFileName = "analysis.tdf_bin";

        /// <summary>
        /// Whether the folder is a timsTOF .d folder: it holds analysis.tdf and analysis.tdf_bin (names compared
        /// case-insensitively).
        /// </summary>
        public static bool IsTimsTofDirectory(string path)
        {
            return Directory.Exists(path) && FindFile(path, TdfFileName) != null && FindFile(path, TdfBinFileName) != null;
        }

        private static string FindFile(string folder, string name)
        {
            return Directory.EnumerateFiles(folder)
                .FirstOrDefault(f => string.Equals(Path.GetFileName(f), name, StringComparison.OrdinalIgnoreCase));
        }

        /// <param name="outFolder">null writes the output inside the .d folder, as the script did.</param>
        public TimsTofConverter(string analysisDir, string outFolder, bool writeMs1, bool writeMs2, bool writeMzXml = false,
            bool writeMzParquet = false)
        {
            _analysisDir = Path.GetFullPath(analysisDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _tdfFile = FindFile(_analysisDir, TdfFileName)
                ?? throw new FileNotFoundException("No " + TdfFileName + " in " + _analysisDir);

            // the script names outputs after the folder up to its first '.';
            string name = Path.GetFileName(_analysisDir).Split('.')[0];
            string folder = string.IsNullOrEmpty(outFolder) ? _analysisDir : outFolder;
            _ms1File = writeMs1 ? Path.Combine(folder, name + ".ms1") : null;
            _ms2File = writeMs2 ? Path.Combine(folder, name + ".ms2") : null;
            _mzXmlFile = writeMzXml ? Path.Combine(folder, name + ".mzXML") : null;
            _mzParquetFile = writeMzParquet ? Path.Combine(folder, name + ".mzparquet") : null;
        }

        /// <param name="progressStart">progress percentage to start from (when extraction came first)</param>
        public void Convert(TaskProgress progress, int progressStart = 0)
        {
            _progressStart = progressStart;
            using SqliteConnection conn = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = _tdfFile,
                Mode = SqliteOpenMode.ReadOnly,
            }.ToString());
            conn.Open();

            if (!HasRows(conn, "Precursors"))
            {
                throw new NotSupportedException(HasRows(conn, "DiaFrameMsMsInfo")
                    ? "timsTOF DIA data is not supported yet: " + _analysisDir
                    : "No PASEF precursors found in " + _analysisDir);
            }

            List<Frame> allFrames = ReadFrames(conn);
            List<Precursor> precursors = ReadPrecursors(conn);
            ReadIsolationWindows(conn, precursors);
            Dictionary<string, string> metadata = ReadGlobalMetadata(conn);
            conn.Close();

            List<Frame> ms1Frames = allFrames.Where(f => f.MsMsType == 0).ToList();
            BuildScanNumbers(ms1Frames, precursors, out Dictionary<long, int> ms1ScanNums, out Dictionary<long, Dictionary<long, int>> ms2ScanNums);

            using TimsData td = new TimsData(_analysisDir);
            List<Precursor> written = precursors.Where(p => p.MonoisotopicMz.HasValue && p.Charge.HasValue && p.Charge > 1).ToList();
            int total = (_ms2File != null ? written.Count : 0) + (_ms1File != null ? ms1Frames.Count : 0)
                + (_mzXmlFile != null || _mzParquetFile != null ? ms1Frames.Count : 0);
            int done = 0;

            if (_ms2File != null)
            {
                Log.Info(" Output file: " + _ms2File);
                WriteMs2(td, precursors, written, allFrames, ms2ScanNums, progress, ref done, total);
            }
            if (_ms1File != null && !progress.Aborted)
            {
                Log.Info(" Output file: " + _ms1File);
                WriteMs1(td, ms1Frames, ms1ScanNums, progress, ref done, total);
            }
            if ((_mzXmlFile != null || _mzParquetFile != null) && !progress.Aborted)
            {
                if (_mzXmlFile != null)
                {
                    Log.Info(" Output file: " + _mzXmlFile);
                }
                if (_mzParquetFile != null)
                {
                    Log.Info(" Output file: " + _mzParquetFile);
                }
                WriteSpectra(td, ms1Frames, written, ms1ScanNums, ms2ScanNums, metadata, progress, ref done, total);
            }
        }

        private void WriteMs2(TimsData td, List<Precursor> precursors, List<Precursor> written, List<Frame> allFrames,
            Dictionary<long, Dictionary<long, int>> ms2ScanNums, TaskProgress progress, ref int done, int total)
        {
            using StreamWriter writer = CreateWriter(_ms2File);

            // FirstScan/LastScan come from the unfiltered precursor list, as in the script;
            Precursor first = precursors[0], last = precursors[precursors.Count - 1];
            string date = DateTime.Now.ToString("MMMM dd, yyyy HH:mm", CultureInfo.GetCultureInfo("en-US"));
            writer.Write("H\tExtractor\tRawConverter\n"
                + "H\tExtractorVersion\t" + ConversionRunner.Version + "\n"
                + "H\tDate Converted\t" + date + "\n"
                + "H\tComments\tTimsTOF_extractor written by Yu Gao, 2018\n"
                + "H\tComments\tTimsTOF_extractor modified by Titus Jung, 2023\n"
                + "H\tComments\tTimsTOF_extractor ported to RawConverter by Titus Jung, 2026\n"
                + "H\tExtractorOptions\tMSn\n"
                + "H\tAcquisitionMethod\tData-Dependent\n"
                + "H\tInstrumentType\tTIMSTOF\n"
                + "H\tDataType\tCentroid\n"
                + "H\tScanType\tMS2\n"
                + "H\tResolution\n"
                + "H\tIsolationWindow\n"
                + "H\tFirstScan\t" + ms2ScanNums[first.Parent][first.Id] + "\n"
                + "H\tLastScan\t" + ms2ScanNums[last.Parent][last.Id] + "\n"
                + "H\tMonoIsotopic PrecMz\tTrue\n");

            StringBuilder sb = new StringBuilder();
            for (int batchStart = 0; batchStart < written.Count; batchStart += BatchSize)
            {
                List<Precursor> batch = written.GetRange(batchStart, Math.Min(BatchSize, written.Count - batchStart));
                Dictionary<long, (double[] Mz, float[] Area)> spectra = td.ReadPasefMsMs(batch.Select(p => p.Id).ToArray());
                foreach (Precursor p in batch)
                {
                    if (spectra.TryGetValue(p.Id, out var spectrum) && spectrum.Mz.Length > 0)
                    {
                        double precMz = p.MonoisotopicMz.Value;
                        long cs = p.Charge.Value;
                        double precMass = precMz * cs - (cs - 1) * ProtonMass;
                        double retTime = allFrames[(int)p.Parent - 1].Time;
                        double k0 = td.ScanNumToOneOverK0(p.Parent, new[] { p.ScanNumber })[0];
                        int scanId = ms2ScanNums[p.Parent][p.Id];

                        sb.Clear();
                        sb.Append("S\t").Append(scanId.ToString("D6", Inv)).Append('\t').Append(scanId.ToString("D6", Inv))
                            .Append('\t').Append(precMz.ToString("F4", Inv)).Append('\n');
                        sb.Append("I\tTIMSTOF_Frame_ID\t").Append(p.Parent).Append('\n');
                        sb.Append("I\tTIMSTOF_ParentTable_ID\t").Append(p.Id).Append('\n');
                        sb.Append("I\tIon Mobility\t").Append(k0.ToString("F4", Inv)).Append('\n');
                        sb.Append("I\tRetTime\t").Append(retTime.ToString("F4", Inv)).Append('\n');
                        sb.Append("Z\t").Append(cs).Append('\t').Append(precMass.ToString("F4", Inv)).Append('\n');
                        for (int j = 0; j < spectrum.Mz.Length; j++)
                        {
                            sb.Append(spectrum.Mz[j].ToString("F4", Inv)).Append(' ')
                                .Append(((double)spectrum.Area[j]).ToString("F1", Inv)).Append(" \n");
                        }
                        writer.Write(sb);
                    }
                    ReportProgress(progress, ++done, total);
                }
                if (progress.Aborted)
                {
                    return;
                }
            }
        }

        private void WriteMs1(TimsData td, List<Frame> ms1Frames, Dictionary<long, int> ms1ScanNums,
            TaskProgress progress, ref int done, int total)
        {
            using StreamWriter writer = CreateWriter(_ms1File);
            writer.Write("H\tExtractor\tRawConverter\n"
                + "H\tExtractorVersion\t" + ConversionRunner.Version + "\n"
                + "H\tComments\tTimsTOF_extractor written by Yu Gao, 2018\n"
                + "H\tComments\tTimsTOF_extractor modified by Titus Jung, 2019\n"
                + "H\tComments\tTimsTOF_extractor ported to RawConverter by Titus Jung, 2026\n"
                + "H\tExtractorOptions\tMSn\n"
                + "H\tAcquisitionMethod\tData-Dependent\n"
                + "H\tInstrumentType\tTIMSTOF\n"
                + "H\tScanType\tMS1\n");

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < ms1Frames.Count; i++)
            {
                Frame frame = ms1Frames[i];
                List<(uint[] Indices, uint[] Intensities)> scans = td.ReadScans(frame.Id, 0, (uint)frame.NumScans);
                int numPeaks = scans.Sum(s => s.Indices.Length);
                if (numPeaks > 0)
                {
                    double[] indices = new double[numPeaks];
                    double[] mobilityScans = new double[numPeaks];
                    double[] intensities = new double[numPeaks];
                    int k = 0;
                    for (int scan = 0; scan < scans.Count; scan++)
                    {
                        for (int j = 0; j < scans[scan].Indices.Length; j++, k++)
                        {
                            indices[k] = scans[scan].Indices[j];
                            intensities[k] = scans[scan].Intensities[j];
                            mobilityScans[k] = scan;
                        }
                    }
                    double[] mz = td.IndexToMz(frame.Id, indices);
                    double[] oneOverK0 = td.ScanNumToOneOverK0(frame.Id, mobilityScans);

                    // the script rounds to 4 decimals with numpy (rint(x * 1e4) / 1e4) before formatting, and sorts
                    // by the rounded m/z; a stable sort keeps peaks with equal m/z in mobility-scan order;
                    double[] mzRounded = mz.Select(RoundNumpy4).ToArray();
                    int[] order = Enumerable.Range(0, numPeaks).OrderBy(idx => mzRounded[idx]).ToArray();

                    // the script writes the previous MS1 frame's time (0 for the first frame);
                    double retTime = i == 0 ? 0 : ms1Frames[i - 1].Time;
                    int scanNum = ms1ScanNums[frame.Id];
                    sb.Clear();
                    sb.Append("S\t").Append(scanNum.ToString("D6", Inv)).Append('\t').Append(scanNum.ToString("D6", Inv)).Append('\n');
                    sb.Append("I\tTIMSTOF_Frame_id\t").Append(frame.Id).Append('\n');
                    sb.Append("I\tRetTime\t").Append(retTime.ToString("F2", Inv)).Append('\n');
                    foreach (int idx in order)
                    {
                        sb.Append(mzRounded[idx].ToString("F4", Inv)).Append(' ')
                            .Append(RoundNumpy4(intensities[idx]).ToString("F1", Inv)).Append(' ')
                            .Append(RoundNumpy4(oneOverK0[idx]).ToString("F4", Inv)).Append(" \n");
                    }
                    writer.Write(sb);
                }
                ReportProgress(progress, ++done, total);
                if (progress.Aborted)
                {
                    return;
                }
            }
        }

        /// <summary>
        /// One pass over the MS1 frames, each followed by the MS2 scans of its precursors, in scan-number order,
        /// into the mzXML and/or mzParquet file. Precursors without peaks are written as empty mzXML scans, so
        /// that scanCount is known up front.
        /// </summary>
        private void WriteSpectra(TimsData td, List<Frame> ms1Frames, List<Precursor> written, Dictionary<long, int> ms1ScanNums,
            Dictionary<long, Dictionary<long, int>> ms2ScanNums, Dictionary<string, string> metadata, TaskProgress progress,
            ref int done, int total)
        {
            Dictionary<long, List<Precursor>> byParent = written.GroupBy(p => p.Parent).ToDictionary(g => g.Key, g => g.ToList());
            string Meta(string key) => metadata.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value) ? value : "unknown";
            string model = Meta("InstrumentName");

            MzXMLFileWriter writer = null;
            MzParquetWriter parquet = null;
            try
            {
                if (_mzXmlFile != null)
                {
                    writer = new MzXMLFileWriter(_mzXmlFile);
                    // the parent file is analysis.tdf, which identifies the run, rather than the much larger .tdf_bin;
                    writer.WriteHeader(ms1Frames.Count + written.Count, ms1Frames.Count > 0 ? ms1Frames[0].Time : 0,
                        ms1Frames.Count > 0 ? ms1Frames[ms1Frames.Count - 1].Time : 0, _tdfFile, "Bruker Daltonics", model,
                        "nanoelectrospray", model, model, "acquisition", Meta("AcquisitionSoftware"), Meta("AcquisitionSoftwareVersion"), ConversionRunner.Version);
                }
                if (_mzParquetFile != null)
                {
                    parquet = new MzParquetWriter(_mzParquetFile, 60);
                }

                foreach (Frame frame in ms1Frames)
                {
                    int ms1Scan = ms1ScanNums[frame.Id];
                    if (writer != null)
                    {
                        (double[] mz, float[] area) = td.ExtractCentroidedSpectrum(frame.Id, 0, (uint)frame.NumScans);
                        writer.WriteScan(CreateSpectrum(ms1Scan, 1, frame.Time, mz, area, "TIMSTOF " + frame.Polarity + " MS1"));
                    }
                    if (parquet != null)
                    {
                        WriteRawMs1(td, frame, ms1Scan, parquet);
                    }

                    if (byParent.TryGetValue(frame.Id, out List<Precursor> children))
                    {
                        Dictionary<long, (double[] Mz, float[] Area)> spectra = td.ReadPasefMsMs(children.Select(p => p.Id).ToArray());
                        foreach (Precursor p in children)
                        {
                            if (!spectra.TryGetValue(p.Id, out var spectrum))
                            {
                                spectrum = (Array.Empty<double>(), Array.Empty<float>());
                            }
                            double k0 = td.ScanNumToOneOverK0(p.Parent, new[] { p.ScanNumber })[0];
                            // the retention time is the parent frame's, as in the MS2 file;
                            MassSpectrum spec = CreateSpectrum(ms2ScanNums[p.Parent][p.Id], 2, frame.Time, spectrum.Mz, spectrum.Area,
                                "TIMSTOF " + frame.Polarity + " MS2 1/K0=" + k0.ToString("F4", Inv));
                            spec.PrecursorScanNumber = ms1Scan;
                            spec.PrecursorIntensity = p.Intensity ?? 0;
                            spec.Precursors = new List<Tuple<double, int>> { Tuple.Create(p.MonoisotopicMz.Value, (int)p.Charge.Value) };
                            spec.IonMobility = k0;
                            if (p.IsolationMz.HasValue)
                            {
                                spec.IsolationLowerMz = p.IsolationMz - p.IsolationWidth / 2;
                                spec.IsolationUpperMz = p.IsolationMz + p.IsolationWidth / 2;
                            }
                            writer?.WriteScan(spec);
                            parquet?.WriteScan(spec);
                        }
                    }

                    ReportProgress(progress, ++done, total);
                    if (progress.Aborted)
                    {
                        return;
                    }
                }
                writer?.WriteIndex();
                writer?.WriteEnd();
            }
            finally
            {
                writer?.Close();
                parquet?.Close();
            }
        }

        /// <summary>
        /// The frame's peaks as measured, in mobility-scan order, each with its scan's 1/K0.
        /// </summary>
        private static void WriteRawMs1(TimsData td, Frame frame, int scanNumber, MzParquetWriter parquet)
        {
            List<(uint[] Indices, uint[] Intensities)> scans = td.ReadScans(frame.Id, 0, (uint)frame.NumScans);
            int numPeaks = scans.Sum(s => s.Indices.Length);
            if (numPeaks == 0)
            {
                return;
            }
            double[] scanK0 = td.ScanNumToOneOverK0(frame.Id, Enumerable.Range(0, scans.Count).Select(i => (double)i).ToArray());
            double[] indices = new double[numPeaks];
            uint[] intensities = new uint[numPeaks];
            double[] oneOverK0 = new double[numPeaks];
            int k = 0;
            for (int scan = 0; scan < scans.Count; scan++)
            {
                for (int j = 0; j < scans[scan].Indices.Length; j++, k++)
                {
                    indices[k] = scans[scan].Indices[j];
                    intensities[k] = scans[scan].Intensities[j];
                    oneOverK0[k] = scanK0[scan];
                }
            }
            parquet.WriteMs1Peaks(scanNumber, frame.Time / 60, td.IndexToMz(frame.Id, indices), intensities, oneOverK0);
        }

        private static MassSpectrum CreateSpectrum(int scanNum, int msLevel, double timeInSeconds, double[] mz, float[] area, string filter)
        {
            List<Ion> peaks = new List<Ion>(mz.Length);
            Ion basePeak = null;
            double tic = 0;
            for (int i = 0; i < mz.Length; i++)
            {
                Ion ion = new Ion(mz[i], area[i]);
                peaks.Add(ion);
                tic += ion.Intensity;
                if (basePeak == null || ion.Intensity > basePeak.Intensity)
                {
                    basePeak = ion;
                }
            }
            return new MassSpectrum(scanNum, scanNum.ToString(Inv), timeInSeconds / 60, peaks, 0, InstrumentType.ELSE, filter, 0, msLevel > 1)
            {
                MsLevel = msLevel,
                ActivationMethod = Activation.CID,
                BasePeakMz = basePeak?.MZ ?? 0,
                BasePeakIntensity = basePeak?.Intensity ?? 0,
                TotIonCurrent = tic,
            };
        }

        /// <summary>
        /// numpy.around(x, 4): scale, round half to even, unscale.
        /// </summary>
        private static double RoundNumpy4(double x)
        {
            return Math.Round(x * 10000.0) / 10000.0;
        }

        /// <summary>
        /// Scan numbers as the script assigns them: each MS1 frame takes the next number, followed by one number
        /// for each of its precursors (including precursors that are not written).
        /// </summary>
        private static void BuildScanNumbers(List<Frame> ms1Frames, List<Precursor> precursors,
            out Dictionary<long, int> ms1ScanNums, out Dictionary<long, Dictionary<long, int>> ms2ScanNums)
        {
            Dictionary<long, List<Precursor>> byParent = new Dictionary<long, List<Precursor>>();
            foreach (Precursor p in precursors)
            {
                if (!byParent.TryGetValue(p.Parent, out List<Precursor> list))
                {
                    byParent[p.Parent] = list = new List<Precursor>();
                }
                list.Add(p);
            }

            ms1ScanNums = new Dictionary<long, int>();
            ms2ScanNums = new Dictionary<long, Dictionary<long, int>>();
            int prevScan = 0;
            foreach (Frame frame in ms1Frames)
            {
                prevScan++;
                ms1ScanNums[frame.Id] = prevScan;
                if (byParent.TryGetValue(frame.Id, out List<Precursor> children))
                {
                    Dictionary<long, int> map = new Dictionary<long, int>();
                    for (int i = 0; i < children.Count; i++)
                    {
                        map[children[i].Id] = prevScan + 1 + i;
                    }
                    ms2ScanNums[frame.Id] = map;
                    prevScan += children.Count;
                }
            }
        }

        private static bool HasRows(SqliteConnection conn, string table)
        {
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT count(*) FROM sqlite_master WHERE type IN ('table', 'view') AND name = $name";
            cmd.Parameters.AddWithValue("$name", table);
            if ((long)cmd.ExecuteScalar() == 0)
            {
                return false;
            }
            cmd.CommandText = "SELECT EXISTS (SELECT 1 FROM " + table + ")";
            return (long)cmd.ExecuteScalar() != 0;
        }

        // "SELECT *" keeps the row order the script saw;
        private static List<Frame> ReadFrames(SqliteConnection conn)
        {
            List<Frame> frames = new List<Frame>();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Frames";
            using SqliteDataReader r = cmd.ExecuteReader();
            int id = r.GetOrdinal("Id"), time = r.GetOrdinal("Time"), type = r.GetOrdinal("MsMsType"), scans = r.GetOrdinal("NumScans"),
                polarity = r.GetOrdinal("Polarity");
            while (r.Read())
            {
                frames.Add(new Frame
                {
                    Id = r.GetInt64(id),
                    Time = r.GetDouble(time),
                    MsMsType = r.GetInt32(type),
                    NumScans = r.GetInt32(scans),
                    Polarity = r.GetString(polarity),
                });
            }
            return frames;
        }

        private static List<Precursor> ReadPrecursors(SqliteConnection conn)
        {
            List<Precursor> precursors = new List<Precursor>();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT * FROM Precursors";
            using SqliteDataReader r = cmd.ExecuteReader();
            int id = r.GetOrdinal("Id"), mono = r.GetOrdinal("MonoisotopicMz"), charge = r.GetOrdinal("Charge"),
                scan = r.GetOrdinal("ScanNumber"), intensity = r.GetOrdinal("Intensity"), parent = r.GetOrdinal("Parent");
            while (r.Read())
            {
                precursors.Add(new Precursor
                {
                    Id = r.GetInt64(id),
                    MonoisotopicMz = r.IsDBNull(mono) ? (double?)null : r.GetDouble(mono),
                    Charge = r.IsDBNull(charge) ? (long?)null : r.GetInt64(charge),
                    ScanNumber = r.GetDouble(scan),
                    Intensity = r.IsDBNull(intensity) ? (double?)null : r.GetDouble(intensity),
                    Parent = r.GetInt64(parent),
                });
            }
            return precursors;
        }

        /// <summary>
        /// The isolation window of each precursor; its PASEF frames all use the same one.
        /// </summary>
        private static void ReadIsolationWindows(SqliteConnection conn, List<Precursor> precursors)
        {
            Dictionary<long, Precursor> byId = precursors.ToDictionary(p => p.Id);
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Precursor, IsolationMz, IsolationWidth FROM PasefFrameMsMsInfo GROUP BY Precursor";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                if (!r.IsDBNull(0) && byId.TryGetValue(r.GetInt64(0), out Precursor p))
                {
                    p.IsolationMz = r.GetDouble(1);
                    p.IsolationWidth = r.GetDouble(2);
                }
            }
        }

        private static Dictionary<string, string> ReadGlobalMetadata(SqliteConnection conn)
        {
            Dictionary<string, string> metadata = new Dictionary<string, string>();
            using SqliteCommand cmd = conn.CreateCommand();
            cmd.CommandText = "SELECT Key, Value FROM GlobalMetadata";
            using SqliteDataReader r = cmd.ExecuteReader();
            while (r.Read())
            {
                metadata[r.GetString(0)] = r.IsDBNull(1) ? null : r.GetString(1);
            }
            return metadata;
        }

        private static StreamWriter CreateWriter(string path)
        {
            // UTF-8 without a byte-order mark and '\n' line endings on every platform, as the script wrote;
            return new StreamWriter(path, false, new UTF8Encoding(false), 1 << 20);
        }

        private void ReportProgress(TaskProgress progress, int done, int total)
        {
            progress.CurrentProgress = total == 0 ? 100 : _progressStart + (int)((long)done * (100 - _progressStart) / total);
        }

        private class Frame
        {
            public long Id;
            public double Time;
            public int MsMsType;
            public int NumScans;
            public string Polarity;
        }

        private class Precursor
        {
            public long Id;
            public double? MonoisotopicMz;
            public long? Charge;
            public double ScanNumber;
            public double? Intensity;
            public long Parent;
            public double? IsolationMz;
            public double IsolationWidth;
        }
    }
}
