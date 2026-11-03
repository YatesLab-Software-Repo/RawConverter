using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Microsoft.Data.Sqlite;
using RawConverter.Bruker;
using RawConverter.Common;

namespace RawConverter.Converter
{
    /// <summary>
    /// Converts Bruker timsTOF DDA-PASEF data (a .d folder with analysis.tdf and analysis.tdf_bin) to MS1 and
    /// MS2 files. Port of the TimsTOF_extractor script (extract_msn_nopd.py, version 0.2.4); the output
    /// matches it, except that MS1 output works (the script opened an empty file name, and failed on MS1
    /// frames without peaks).
    /// </summary>
    internal class TimsTofConverter
    {
        private const string ExtractorVersion = "0.2.4";
        private const double ProtonMass = 1.007276466;
        private const int BatchSize = 1000;

        private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

        private readonly string _analysisDir;
        private readonly string _tdfFile;
        private readonly string _ms1File;
        private readonly string _ms2File;
        private int _progressStart;

        public static bool IsTimsTofDirectory(string path)
        {
            return Directory.Exists(path)
                && Directory.GetFiles(path, "*.tdf").Length == 1
                && Directory.GetFiles(path, "*.tdf_bin").Length == 1;
        }

        /// <param name="outFolder">null writes the output inside the .d folder, as the script did.</param>
        public TimsTofConverter(string analysisDir, string outFolder, bool writeMs1, bool writeMs2)
        {
            _analysisDir = Path.GetFullPath(analysisDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            _tdfFile = Directory.GetFiles(_analysisDir, "*.tdf")[0];

            // the script names outputs after the folder up to its first '.';
            string name = Path.GetFileName(_analysisDir).Split('.')[0];
            string folder = string.IsNullOrEmpty(outFolder) ? _analysisDir : outFolder;
            _ms1File = writeMs1 ? Path.Combine(folder, name + ".ms1") : null;
            _ms2File = writeMs2 ? Path.Combine(folder, name + ".ms2") : null;
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
            conn.Close();

            List<Frame> ms1Frames = allFrames.Where(f => f.MsMsType == 0).ToList();
            BuildScanNumbers(ms1Frames, precursors, out Dictionary<long, int> ms1ScanNums, out Dictionary<long, Dictionary<long, int>> ms2ScanNums);

            using TimsData td = new TimsData(_analysisDir);
            List<Precursor> written = precursors.Where(p => p.MonoisotopicMz.HasValue && p.Charge.HasValue && p.Charge > 1).ToList();
            int total = (_ms2File != null ? written.Count : 0) + (_ms1File != null ? ms1Frames.Count : 0);
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
        }

        private void WriteMs2(TimsData td, List<Precursor> precursors, List<Precursor> written, List<Frame> allFrames,
            Dictionary<long, Dictionary<long, int>> ms2ScanNums, TaskProgress progress, ref int done, int total)
        {
            using StreamWriter writer = CreateWriter(_ms2File);

            // FirstScan/LastScan come from the unfiltered precursor list, as in the script;
            Precursor first = precursors[0], last = precursors[precursors.Count - 1];
            string date = DateTime.Now.ToString("MMMM dd, yyyy HH:mm", CultureInfo.GetCultureInfo("en-US"));
            writer.Write("H\tExtractor\tTimsTOF_extractor\n"
                + "H\tExtractorVersion\t" + ExtractorVersion + "\n"
                + "H\tPublicationDate\t20-02-2020\n"
                + "H\tDate Converted\t" + date + "\n"
                + "H\tComments\tTimsTOF_extractor written by Yu Gao, 2018\n"
                + "H\tComments\tTimsTOF_extractor modified by Titus Jung, 2023\n"
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
            writer.Write("H\tExtractor\tTimsTOF_extractor\n"
                + "H\tExtractorVersion\t" + ExtractorVersion + "\n"
                + "H\tPublicationDate\t20-02-2020\n"
                + "H\tComments\tTimsTOF_extractor written by Yu Gao, 2018\n"
                + "H\tComments\tTimsTOF_extractor modified by Titus Jung, 2019\n"
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
            int id = r.GetOrdinal("Id"), time = r.GetOrdinal("Time"), type = r.GetOrdinal("MsMsType"), scans = r.GetOrdinal("NumScans");
            while (r.Read())
            {
                frames.Add(new Frame
                {
                    Id = r.GetInt64(id),
                    Time = r.GetDouble(time),
                    MsMsType = r.GetInt32(type),
                    NumScans = r.GetInt32(scans),
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
                scan = r.GetOrdinal("ScanNumber"), parent = r.GetOrdinal("Parent");
            while (r.Read())
            {
                precursors.Add(new Precursor
                {
                    Id = r.GetInt64(id),
                    MonoisotopicMz = r.IsDBNull(mono) ? (double?)null : r.GetDouble(mono),
                    Charge = r.IsDBNull(charge) ? (long?)null : r.GetInt64(charge),
                    ScanNumber = r.GetDouble(scan),
                    Parent = r.GetInt64(parent),
                });
            }
            return precursors;
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
        }

        private class Precursor
        {
            public long Id;
            public double? MonoisotopicMz;
            public long? Charge;
            public double ScanNumber;
            public long Parent;
        }
    }
}
