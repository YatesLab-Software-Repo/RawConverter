# RawConverter

Converts Thermo RAW files (and mzXML, mzML, MS2 and MGF files) to MS1, MS2, MS3, MGF, mzXML and mzParquet,
and Bruker timsTOF DDA-PASEF data (`.d` folders) to MS2, mzXML and mzParquet.
It can also correct DDA precursor m/z values and predict DIA precursors. Runs on Linux and Windows (x64)
with .NET 10, using Thermo's RawFileReader library (no Wine or MSFileReader needed).

## Layout

| Path | Contents |
|---|---|
| `src/RawConverter.Core` | Conversion library shared by all front ends |
| `src/RawConverter.Cli` | Command-line tool (`RawConverter`) |
| `src/RawConverter.Gui` | Desktop GUI (`RawConverterGUI`, Avalonia), a copy of the legacy WinForms window |
| `tests/RawConverter.Tests` | Unit tests and data-driven regression tests (see `tests/README.md`) |
| `ThermoPkg` | Thermo RawFileReader NuGet packages (not on nuget.org; see `Licenses/Thermo`) |
| `Licenses` | License terms for the Thermo, Bruker and Parquet.Net components |
| `src/RawConverter.Core/native` | Native libraries: lpsolve, and Bruker's TDF SDK (`timsdata`) for timsTOF input |
| `packaging/singularity` | Container definition for hosts too old for .NET 10, such as CentOS 7 |
| `RawConverter` | Legacy .NET Framework 4 / WinForms version, kept as the reference for the GUI port |

## Build

Requires the .NET 10 SDK.

```
dotnet build RawConverter.sln
dotnet test RawConverter.sln
./build.sh        # build, test, and publish self-contained Linux and Windows builds into out/
```

`packaging/windows/build-installer.sh` builds the Windows installer, `out/RawConverter-<version>-win-x64-setup.exe`,
on Linux with Inno Setup in Docker (`amake/innosetup`). It installs the GUI and the CLI (optionally on PATH), the
license files, and the Microsoft Visual C++ runtime that Bruker's `timsdata.dll` needs, if it is missing.

## GUI

`RawConverterGUI` reproduces the legacy WinForms window and menus on Windows and Linux. Besides the legacy
inputs, `+` accepts timsTOF `.d.tar`/`.tar.gz` archives and `.d` adds timsTOF `.d` folders; files and folders
can also be dropped on the list. Output goes to the destination directory. Checking `Log` writes the log to a
`RawConverter_<date>.log` file there. The `mzParquet` output format accepts RAW, mzXML, mzML, MS2 and timsTOF
input.

On Linux the GUI needs an X11 or XWayland display, plus fontconfig and the basic X11 libraries (RHEL:
`fontconfig libICE libSM libX11 libXrandr libXi libXcursor`; Ubuntu: `libfontconfig1 libice6 libsm6 libx11-6
libxrandr2 libxi6 libxcursor1`), which desktop installations already have.

## Command line

```
RawConverter <input_file> [<input_file> ...] [options]

  --ms1 --ms2 --ms3 --mgf --mzxml --mzparquet
                                    output formats (at least one)
  --out_folder <dir>                output folder (default: next to each input file)
  --select_mono_prec                DDA: select the monoisotopic precursor m/z
  --predict_precursors              DIA: predict precursors
  --parallel <n>                    convert n files at the same time (default: 1)
  --quiet / --verbose / --version / --help
```

For example: `RawConverter sample.raw --ms1 --ms2 --select_mono_prec`

MS2 input converts only to MGF and mzParquet, and MGF input converts only to MS2. The exit code is 0 on success,
1 if any file failed, and 2 for invalid arguments.

**mzParquet:** `--mzparquet` writes `<name>.mzparquet`, the format of Chaparral Labs'
[mzParquet Converter](https://github.com/ChaparralLabs/mzparquet_converter) (ThermoParquet and dotD2parquet): a
Zstandard-compressed Apache Parquet table with one row per peak and the columns `scan`, `level`, `rt`, `mz`,
`intensity` (rounded to an integer), `ion_mobility`, `isolation_lower`, `isolation_upper`, `precursor_scan`,
`precursor_mz` and `precursor_charge`. It holds the same spectra and peaks as the MS1/MS2/MS3 output (centroided,
with the corrected or predicted precursors when those options are on); spectra without peaks have no rows. A
spectrum with several precursor candidates (unknown charge, or predicted DIA precursors) gets the first one, and a
charge of 0 is stored as null. As in that converter, `rt` is in minutes for Thermo, mzML and mzXML input and in
seconds for timsTOF input; MS2 input keeps its `RetTime` values (seconds in MS2 files from timsTOF data). For
timsTOF, MS1 frames are summed over ion mobility and centroided as in the mzXML output (`ion_mobility` is null),
and MS2 rows carry the precursor's 1/K0 and its isolation window. It can be read with any Parquet library, e.g.
`polars.read_parquet("sample.mzparquet")`.

**timsTOF:** pass the `.d` folder, e.g. `RawConverter sample.d --ms2`. This is a port of the
TimsTOF_extractor script (`extract_msn_nopd.py` 0.2.4) and writes the same MS2 output, using Bruker's TDF SDK
5.0.4 (see `Licenses/Bruker`): the file is named after the folder up to its first `.`, and by default is written
inside the `.d` folder. MS2 scans are written for precursors with a monoisotopic m/z and charge 2+ or higher.
MS1 output is disabled for now (`--ms1` is skipped with a warning), and DIA data is not supported yet.

`--mzxml` writes `<name>.mzXML` next to the MS2 file, with the same scan numbers: each MS1 frame, summed over ion
mobility and centroided by Bruker's SDK (`tims_extract_centroided_spectrum_for_frame_v2`), followed by the MS2 scans
of its precursors (the same peaks as the MS2 file, stored as 32-bit floats). mzXML has no ion-mobility field, so each
MS2 scan's `filterLine` carries the precursor's 1/K0, e.g. `TIMSTOF + MS2 1/K0=1.0234` (the `I	Ion Mobility` value of
the MS2 file); MS1 scans have none. Precursors whose MS2 spectrum has no peaks, which the MS2 file leaves out, are
written as empty scans.

A `.d` folder in a tar archive (`sample.d.tar`, `.tar.gz` or `.tgz`) can be passed directly. The `.d` folder is
extracted next to the archive (an existing one is reused), and the output files are written next to the `.d`
folder unless `--out_folder` is given. The archive must hold a single top-level `.d` folder.
