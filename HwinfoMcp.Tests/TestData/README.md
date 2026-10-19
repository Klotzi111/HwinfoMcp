# Test data

## `hwinfo-sm2-snapshot.bin`

A raw dump of HWiNFO's shared memory section (`Global\HWiNFO_SENS_SM2`, 25 sensor groups, 470 readings)
recorded on an AMD Ryzen 9 7900X with an AMD Radeon RX 7800 XT and the CPU's integrated Radeon 610M.

Taken unchanged from [Hwinfo.SharedMemory.Net](https://github.com/Seraksab/Hwinfo.SharedMemory.Net)
(`Tests/TestData/hwinfo-sm2-snapshot.bin`, commit `1d09864597f4342e2466880399ad0872892f870a`),
which is licensed as follows:

```
MIT License

Copyright (c) 2023 Manfred Graf

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
```

The tests publish it under a session-local name and read it with the real `SharedMemoryReader`, see
`TestSnapshots.cs`.

## JSON dumps

Files written by `HwinfoMcp.exe --dump <file.json> [description]` (see `SensorDump`). The tests replay
them as snapshots.

- `amd-7800x3d-rtx4080-de.json`: AMD Ryzen 7 7800X3D with its Radeon iGPU and an NVIDIA GeForce RTX 4080,
  recorded with HWiNFO set to German, so every group and label also carries a translated user name. Its GPU
  groups lack the d/i prefix ("GPU [#0]: NVIDIA GeForce RTX 4080: ", "GPU [#1]: AMD Radeon: "). The drive's
  serial number was replaced with X.
