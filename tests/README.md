# Tests

`dotnet test RawConverter.sln` runs:

- **Unit tests** (command-line parsing, error handling, MGF/MS2 conversion of small synthetic files,
  locale independence, zlib decompression). These need no data files.
- **Regression tests** (`RawRegressionTests`): each case in `RawConverter.Tests/Regression/cases.json`
  converts real data files and compares SHA-256 hashes of the outputs with reference hashes. Cases whose
  input files are missing are skipped.

## Regression test data

Input files are read from `$RAWCONVERTER_TEST_DATA`, or from `test_data/` at the repository root (gitignored;
symbolic links work). The current cases use:

| File | Case | Location on the Yates lab server |
|---|---|---|
| `ExoExpt3s01.raw` | DDA (MS1/MS2/MGF/mzXML; monoisotopic precursor selection) | `/data/local/project_data/census3/2403Test/dimethyl_test/census/spectra/` |
| `3711_NCM.raw` | DIA precursor prediction | `/data/local/project_data/sage_dia/1205_run/` |
| `PFEC_000409_PhosphoILK_16.raw` | TMT MS3 | `/data/local/project_data/census/1709TMTNoise/` |
| `AprY_UV_EnzymeDigestion.mzML` | mzML input | |
| `200ngHeLaPASEF_1min.d` | timsTOF DDA-PASEF (MS1/MS2) | `/data/local/project_data/ip2/1804Test/` |
| `200ngHeLaPASEF_1min.d.tar` | timsTOF archive: extract and convert (`TimsTofArchiveTests`) | `/data/local/project_data/ip2/1804Test/` |
| `061925_CPP-SCP_E_5_6_5ul_dia_S2-E10_1_383.d` | timsTOF DIA is rejected (`TimsTofTests`) | `/data/local/project_data/sage/0207Test/` |

Before hashing, the outputs are normalised: `H	Creation Date` lines are dropped, the input folder path
is replaced with `{INPUT_DIR}`, and mzXML `<offset>`, `<indexOffset>` and `<sha1>` lines are dropped
(they change with the length of the path).

The reference hashes were recorded from the pre-restructure `RawConverterDotNetCore` build (commit
`2c3d1f6` plus its uncommitted mzML fixes). When a case fails, the test keeps its outputs in a temporary folder
and prints the actual hashes. If a change in output is intended, paste the new hashes into `cases.json`.

The `timstof_dda` hashes are of this port's output. It was checked against the TimsTOF_extractor script (with
its MS1 crash fixed): the MS2 file is identical apart from the `H` header lines, and every MS1 scan
contains the same lines. Only the order of peaks with equal m/z differs, because numpy's sort is not stable.
