# Bruker TDF SDK

`src/RawConverter.Core/native/libtimsdata.so` and `timsdata.dll` are from Bruker's TDF SDK 5.0.4 (2026-07-21),
used for timsTOF input. They are distributed under Bruker's "Software License Agreement (with the right to link
to other software)", which comes with the SDK (`EULA TDF-SDK.pdf`), on these conditions:

- only the files listed in the SDK's `redist.txt` are distributed (`linux64/libtimsdata.so`, `win64/timsdata.dll`),
  always together with `THIRD-PARTY-LICENSE-README.txt` (this folder; it is copied next to the libraries in builds);
- the About box (and `RawConverter --version`) shows "Included software components: Copyright © 2022 by Bruker
  Daltonics GmbH & Co. KG. All rights reserved.";
- the libraries are not modified, and are not combined with GPL-licensed software.

The SDK itself, including the agreement, is downloaded from
https://www.bruker.com/service/support-upgrades/software-downloads/mass-spectrometry.html. Anyone using the
libraries must accept that agreement.

On Windows the libraries need the Microsoft Visual C++ 2015-2022 Redistributable (x64). On Linux they need
glibc 2.17 or later.
