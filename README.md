# whisper-server-vulkan-windows

Prebuilt [whisper.cpp](https://github.com/ggml-org/whisper.cpp) HTTP transcription server for Windows with the **Vulkan backend** enabled, plus a PowerShell deploy script that unpacks it, downloads a model, and (optionally) registers it to auto-start at logon.

The point: get an **OpenAI-compatible `/v1/audio/transcriptions` endpoint** running on any Windows box with a Vulkan-capable GPU (AMD, NVIDIA, Intel — any vendor whose driver ships a Vulkan ICD) without installing a compiler, CUDA, ROCm, Python, or Docker.

## What's in this repo

| File | Size | What it is |
|---|---|---|
| `whisper-server-bundle.zip` | ~19 MB | `whisper-server.exe` + `whisper-cli.exe` + 5 native DLLs (whisper, ggml, ggml-base, ggml-cpu, ggml-vulkan) + VC++ 2015-2022 redist DLLs + a sample `jfk.wav`. |
| `deploy-whisper-server.ps1` | ~9 KB | Interactive model picker → extract → download model → smoke test → optional auto-start at logon. |

> ⚠️ **The Whisper model is _not_ included in this repository.** The model file
> (75 MB - 575 MB depending on size) is downloaded from
> [huggingface.co/ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp)
> the first time you run `deploy-whisper-server.ps1`. This keeps the git repo
> small enough to clone quickly and avoids re-distributing the model weights.
> A working internet connection is required on first run.

## Models

When you run the deploy script with no `-Model` flag, it shows an interactive
picker so you can choose based on your hardware and accuracy needs:

| # | Model | Size | WER (LS-clean) | Notes |
|---|---|---|---|---|
| 1 | `ggml-tiny.bin` | 75 MB | ~7-8% | Fastest, English-heavy, low-power boxes |
| 2 | `ggml-base.bin` | 142 MB | ~5% | Good speed/quality tradeoff |
| 3 | `ggml-small.bin` | 466 MB | ~3.4% | Balanced classic pick |
| 4 | `ggml-large-v3-turbo-q5_0.bin` | 547 MB | ~2% | Multilingual (99 langs), **recommended default** |

To skip the prompt and use the default, pass `-Yes`. To use a different model,
pass `-Model ggml-<name>.bin` (any file from
[the upstream HuggingFace repo](https://huggingface.co/ggerganov/whisper.cpp/tree/main)),
or `-ModelURL <full-url>` to fetch from a mirror.

The model lives at `<InstallDir>\models\<file>.bin` after download. Re-running
the script with the same `-InstallDir` and the same `-Model` reuses the cached
file — no second download.

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
# Interactive: pick model size from the menu, then unzip + download + smoke test
.\deploy-whisper-server.ps1

# Non-interactive: use the default model (large-v3-turbo-q5_0)
.\deploy-whisper-server.ps1 -Yes

# Explicit model, skip the picker
.\deploy-whisper-server.ps1 -Model ggml-small.bin

# Custom install location and port
.\deploy-whisper-server.ps1 -InstallDir D:\whisper -Port 9000 -Yes

# Production: auto-start at logon and leave running now
.\deploy-whisper-server.ps1 -InstallDir D:\whisper -AutoStart -StartNow -Yes

# Re-run later: keep the extracted bundle + cached model, just smoke-test + (re)register the task
.\deploy-whisper-server.ps1 -InstallDir D:\whisper -SkipExtract -SkipModelDownload -AutoStart
```

### All flags

| Flag | Default | Meaning |
|---|---|---|
| `-Zip <path>` | `.\whisper-server-bundle.zip` | Path to the bundle zip. |
| `-InstallDir <path>` | `$env:USERPROFILE\whisper-server` | Where the runtime is extracted. |
| `-Port <int>` | `8088` | Port the HTTP server listens on. |
| `-BindHost <ip>` | `0.0.0.0` | Interface to bind (`127.0.0.1` for local-only). |
| `-InferencePath <path>` | `/v1/audio/transcriptions` | URL path for the inference endpoint. |
| `-Model <file>` | _interactive picker_ | Which `.bin` model to use; skips the picker. |
| `-ModelURL <url>` | _HuggingFace pattern_ | Override the download URL (for mirrors). |
| `-Yes` | off | Skip the interactive picker, use the default model. |
| `-AutoStart` | off | Register a Scheduled Task that launches the server at user logon. |
| `-StartNow` | off | Leave the server running after the smoke test instead of stopping it. |
| `-SkipExtract` | off | Reuse an existing extracted `InstallDir` (no re-unzip). |
| `-SkipModelDownload` | off | Don't try to fetch the model; expects a pre-staged file. |

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
