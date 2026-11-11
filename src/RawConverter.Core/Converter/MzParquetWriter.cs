using System;
using System.IO;
using System.IO.Compression;
using Parquet;
using Parquet.Schema;
using RawConverter.MassSpec;

namespace RawConverter.Converter
{
    /// <summary>
    /// Writes .mzparquet files (https://github.com/ChaparralLabs/mzparquet_converter): a Zstandard-compressed
    /// Parquet table with one row per peak, in the column order and types of ThermoParquet and dotD2parquet.
    /// Spectra without peaks have no rows.
    /// </summary>
    internal sealed class MzParquetWriter : IDisposable
    {
        private const int RowGroupSize = 1 << 20;

        private static readonly DataField ScanField = new DataField<uint>("scan");
        private static readonly DataField LevelField = new DataField<uint>("level");
        private static readonly DataField RtField = new DataField<float>("rt");
        private static readonly DataField MzField = new DataField<float>("mz");
        private static readonly DataField IntensityField = new DataField<uint>("intensity");
        private static readonly DataField IonMobilityField = new DataField<float?>("ion_mobility");
        private static readonly DataField IsolationLowerField = new DataField<float?>("isolation_lower");
        private static readonly DataField IsolationUpperField = new DataField<float?>("isolation_upper");
        private static readonly DataField PrecursorScanField = new DataField<uint?>("precursor_scan");
        private static readonly DataField PrecursorMzField = new DataField<float?>("precursor_mz");
        private static readonly DataField PrecursorChargeField = new DataField<uint?>("precursor_charge");

        private static readonly ParquetSchema Schema = new ParquetSchema(ScanField, LevelField, RtField, MzField, IntensityField,
            IonMobilityField, IsolationLowerField, IsolationUpperField, PrecursorScanField, PrecursorMzField, PrecursorChargeField);

        private readonly double _rtScale;
        private readonly FileStream _stream;
        private readonly ParquetWriter _writer;

        private readonly uint[] _scan = new uint[RowGroupSize];
        private readonly uint[] _level = new uint[RowGroupSize];
        private readonly float[] _rt = new float[RowGroupSize];
        private readonly float[] _mz = new float[RowGroupSize];
        private readonly uint[] _intensity = new uint[RowGroupSize];
        private readonly float?[] _ionMobility = new float?[RowGroupSize];
        private readonly float?[] _isolationLower = new float?[RowGroupSize];
        private readonly float?[] _isolationUpper = new float?[RowGroupSize];
        private readonly uint?[] _precursorScan = new uint?[RowGroupSize];
        private readonly float?[] _precursorMz = new float?[RowGroupSize];
        private readonly uint?[] _precursorCharge = new uint?[RowGroupSize];
        private int _rows;
        private bool _disposed;

        /// <param name="rtScale">factor from MassSpectrum.RetentionTime to the rt column (60 writes seconds
        /// from minutes, as dotD2parquet does for Bruker data)</param>
        public MzParquetWriter(string path, double rtScale = 1)
        {
            _rtScale = rtScale;
            _stream = new FileStream(path, FileMode.Create, FileAccess.ReadWrite);
            try
            {
                // the default (smallest size) is the slowest Zstandard level;
                ParquetOptions options = new ParquetOptions { CompressionMethod = CompressionMethod.Zstd, CompressionLevel = CompressionLevel.Optimal };
                _writer = ParquetWriter.CreateAsync(Schema, _stream, options).GetAwaiter().GetResult();
            }
            catch
            {
                _stream.Dispose();
                throw;
            }
        }

        public void WriteScan(MassSpectrum spec)
        {
            if (spec.Peaks == null || spec.Peaks.Count == 0)
            {
                return;
            }

            bool isMsn = spec.MsLevel > 1;
            float rt = (float)(spec.RetentionTime * _rtScale);
            float? ionMobility = (float?)spec.IonMobility;
            float? isolationLower = isMsn ? (float?)spec.IsolationLowerMz : null;
            float? isolationUpper = isMsn ? (float?)spec.IsolationUpperMz : null;
            uint? precursorScan = isMsn && spec.PrecursorScanNumber > 0 ? (uint)spec.PrecursorScanNumber : (uint?)null;
            float? precursorMz = null;
            uint? precursorCharge = null;
            if (isMsn && spec.Precursors != null && spec.Precursors.Count > 0)
            {
                // one precursor per row; spectra with several candidates (unknown charge, DIA) take the first;
                precursorMz = spec.Precursors[0].Item1 > 0 ? (float)spec.Precursors[0].Item1 : (float?)null;
                precursorCharge = spec.Precursors[0].Item2 > 0 ? (uint)spec.Precursors[0].Item2 : (uint?)null;
            }

            foreach (Ion peak in spec.Peaks)
            {
                _scan[_rows] = (uint)spec.ScanNumber;
                _level[_rows] = (uint)spec.MsLevel;
                _rt[_rows] = rt;
                _mz[_rows] = (float)peak.MZ;
                _intensity[_rows] = ToUInt32(peak.Intensity);
                _ionMobility[_rows] = ionMobility;
                _isolationLower[_rows] = isolationLower;
                _isolationUpper[_rows] = isolationUpper;
                _precursorScan[_rows] = precursorScan;
                _precursorMz[_rows] = precursorMz;
                _precursorCharge[_rows] = precursorCharge;
                if (++_rows == RowGroupSize)
                {
                    Flush();
                }
            }
        }

        /// <summary>
        /// Uncentroided MS1 peaks, each with its own ion mobility (timsTOF frames).
        /// </summary>
        public void WriteMs1Peaks(int scanNumber, double retentionTime, double[] mz, uint[] intensity, double[] ionMobility)
        {
            float rt = (float)(retentionTime * _rtScale);
            for (int i = 0; i < mz.Length; i++)
            {
                _scan[_rows] = (uint)scanNumber;
                _level[_rows] = 1;
                _rt[_rows] = rt;
                _mz[_rows] = (float)mz[i];
                _intensity[_rows] = intensity[i];
                _ionMobility[_rows] = (float)ionMobility[i];
                _isolationLower[_rows] = null;
                _isolationUpper[_rows] = null;
                _precursorScan[_rows] = null;
                _precursorMz[_rows] = null;
                _precursorCharge[_rows] = null;
                if (++_rows == RowGroupSize)
                {
                    Flush();
                }
            }
        }

        private static uint ToUInt32(double intensity)
        {
            double rounded = Math.Round(intensity);
            return rounded <= 0 ? 0 : rounded >= uint.MaxValue ? uint.MaxValue : (uint)rounded;
        }

        private void Flush()
        {
            if (_rows == 0)
            {
                return;
            }
            using (ParquetRowGroupWriter rg = _writer.CreateRowGroup())
            {
                rg.WriteAsync<uint>(ScanField, _scan.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<uint>(LevelField, _level.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<float>(RtField, _rt.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<float>(MzField, _mz.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<uint>(IntensityField, _intensity.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<float>(IonMobilityField, _ionMobility.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<float>(IsolationLowerField, _isolationLower.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<float>(IsolationUpperField, _isolationUpper.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<uint>(PrecursorScanField, _precursorScan.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<float>(PrecursorMzField, _precursorMz.AsMemory(0, _rows)).GetAwaiter().GetResult();
                rg.WriteAsync<uint>(PrecursorChargeField, _precursorCharge.AsMemory(0, _rows)).GetAwaiter().GetResult();
            }
            _rows = 0;
        }

        /// <summary>
        /// Write the remaining rows and the file footer.
        /// </summary>
        public void Close()
        {
            try
            {
                Flush();
            }
            finally
            {
                Dispose();
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            try
            {
                // writes the footer;
                _writer.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                _stream.Dispose();
            }
        }
    }
}
