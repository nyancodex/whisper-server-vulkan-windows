# whisper-server-vulkan-windows

Prebuilt [whisper.cpp](https://github.com/ggml-org/whisper.cpp) HTTP transcription server for Windows with the **Vulkan backend** enabled, plus a PowerShell deploy script that unpacks it, downloads a model, and (optionally) registers it to auto-start at logon.

The point: get an **OpenAI-compatible `/v1/audio/transcriptions` endpoint** running on any Windows box with a Vulkan-capable GPU (AMD, NVIDIA, Intel — any vendor whose driver ships a Vulkan ICD) without installing a compiler, CUDA, ROCm, Python, or Docker.

## What's in this repo

| File | Size | What it is |
|---|---|---|
| `whisper-server-bundle.zip` | ~19 MB | `whisper-server.exe` + `whisper-cli.exe` + 5 native DLLs (whisper, ggml, ggml-base, ggml-cpu, ggml-vulkan) + VC++ 2015-2022 redist DLLs + a sample `jfk.wav`. **No model inside** — the deploy script fetches one from HuggingFace. |
| `deploy-whisper-server.ps1` | ~8 KB | Extract → download model → smoke test → optional auto-start at logon. |

The model (default: `ggml-large-v3-turbo-q5_0.bin`, ~547 MB) is downloaded from `huggingface.co/ggerganov/whisper.cpp` on first run. Override with `-Model <other.bin>` or `-ModelURL <url>` to use a different one (whisper-small, distil-large-v3.5, etc.).

## Prerequisites

- Windows 10 / 11 x64
- A working Vulkan GPU driver (provides `C:\Windows\System32\vulkan-1.dll`)
  - AMD: install **Adrenalin Software** — works on Radeon RX 400-series and newer
  - NVIDIA: any recent **GeForce Game Ready** or **Studio** driver
  - Intel: **Arc Graphics** drivers from intel.com
  - Verify with: `vulkaninfo --summary` (if installed) or check that the file exists
- ~600 MB free disk space (19 MB bundle + ~550 MB model)
- Internet access on first run (for the model download)

No compiler, no Vulkan SDK, no Python, no Docker.

## Usage

```powershell
# Default: extract to %USERPROFILE%\whisper-server, port 8088, download large-v3-turbo
.\deploy-whisper-server.ps1

# Custom install location and port
.\deploy-whisper-server.ps1 -InstallDir D:\whisper -Port 9000

# Smaller model
.\deploy-whisper-server.ps1 -Model ggml-small.bin

# Production: auto-start at logon and leave running now
.\deploy-whisper-server.ps1 -InstallDir D:\whisper -AutoStart -StartNow

# Re-run later, skip extract and re-download (just smoke-test + register the task)
.\deploy-whisper-server.ps1 -InstallDir D:\whisper -SkipExtract -SkipModelDownload -AutoStart
```

If PowerShell blocks the script with an execution-policy error, run it once with the bypass:

```powershell
PowerShell -ExecutionPolicy Bypass -File .\deploy-whisper-server.ps1
```

## Calling the server

After the script reports `Smoke test PASSED`, the endpoint follows OpenAI's `/v1/audio/transcriptions` shape:

```powershell
# verbose_json gives per-segment avg_logprob, no_speech_prob, per-word probability/timestamps
curl -X POST http://localhost:8088/v1/audio/transcriptions `
     -F "file=@your.wav;type=audio/wav" `
     -F "response_format=verbose_json" `
     -F "temperature=0"
```

```bash
# from another machine on the LAN
curl -X POST http://<gpu-box-ip>:8088/v1/audio/transcriptions \
     -F "file=@your.wav;type=audio/wav" \
     -F "response_format=json"
```

Supported `response_format` values: `json` (just `{text}`), `verbose_json` (full segments + word timestamps), `text`, `srt`, `vtt`.

## Build provenance

The bundle was built from:

- **whisper.cpp**: latest `master` (≥ commit `27101c0`, May 2026)
- **Backend**: `-DGGML_VULKAN=ON`, no CPU-specific or vendor-specific flags
- **Toolchain**: Clang 22.1.6 (`x86_64-pc-windows-msvc`) + Ninja, Release mode
- **Vulkan SDK**: 1.4.350.0
- **Target**: Windows 10/11 x64

To rebuild from source yourself:

```powershell
git clone https://github.com/ggml-org/whisper.cpp
cd whisper.cpp
cmake -B build -G Ninja `
    -DCMAKE_C_COMPILER=clang `
    -DCMAKE_CXX_COMPILER=clang++ `
    -DCMAKE_BUILD_TYPE=Release `
    -DGGML_VULKAN=ON `
    -DWHISPER_BUILD_SERVER=ON
cmake --build build --config Release -j
# Output: build\bin\whisper-server.exe + DLLs
```

## Performance notes

On an AMD Radeon RX 6700 XT with Vulkan, `ggml-large-v3-turbo-q5_0.bin` transcribes an 11-second clip in ~3 seconds wall-clock from a cold start (model already in GPU VRAM). Hot inference is much faster — sub-second per chunk for short clips. CPU fallback (with `-ng`) is roughly an order of magnitude slower.

Larger / non-quantized models (`ggml-large-v3.bin` ~3.1 GB) give marginally better WER but use ~5× more VRAM and run ~2-3× slower.

## Known issues

- **First request after model load** is slower than steady-state (Vulkan shader compile + memory allocation).
- **Long clips** (> ~30 minutes) work but get held in a single HTTP request — set generous client timeouts.
- **MSYS / Git-Bash users**: when running `whisper-server.exe` directly from Git-Bash, the `--inference-path /v1/audio/transcriptions` argument gets mangled by Git-Bash's POSIX-path translation. Prefix the command with `MSYS_NO_PATHCONV=1` or run from native cmd / PowerShell. The deploy script avoids this by launching via PowerShell.

## Licensing

The bundle redistributes whisper.cpp binaries and the Microsoft Visual C++ runtime DLLs.

- **whisper.cpp**: [MIT](https://github.com/ggml-org/whisper.cpp/blob/master/LICENSE) — Copyright (c) 2023-present Georgi Gerganov
- **ggml**: [MIT](https://github.com/ggml-org/ggml/blob/master/LICENSE) — Copyright (c) 2022-present Georgi Gerganov
- **Microsoft Visual C++ Redistributable** (`MSVCP140.dll`, `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`): redistributed under the [Microsoft Visual C++ Redistributable runtime files redistribution license](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution#visual-c-runtime-files)
- **Whisper models** (downloaded by the script, not in this repo): MIT — Copyright (c) 2022 OpenAI

The deploy script and README in this repo are released under MIT — see [LICENSE](LICENSE).

## Related

- [ggml-org/whisper.cpp](https://github.com/ggml-org/whisper.cpp) — the upstream project
- [ggerganov/whisper.cpp on HuggingFace](https://huggingface.co/ggerganov/whisper.cpp) — model files
- [OpenAI Audio API reference](https://platform.openai.com/docs/api-reference/audio/createTranscription) — the API shape this server implements
