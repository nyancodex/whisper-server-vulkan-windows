# whisper-server-vulkan-windows

Prebuilt [whisper.cpp](https://github.com/ggml-org/whisper.cpp) HTTP transcription server for Windows with the **Vulkan backend** enabled, plus a small GUI launcher that unpacks it, downloads a model, starts/stops the server, and lets you test transcription by dropping in an audio file.

The point: get an **OpenAI-compatible `/v1/audio/transcriptions` endpoint** running on any Windows box with a Vulkan-capable GPU (AMD, NVIDIA, Intel — any vendor whose driver ships a Vulkan ICD) without installing a compiler, CUDA, ROCm, Python, or Docker.

## Quick start

1. Download **`whisper-launcher.exe`** and **`whisper-server-bundle.zip`** from this repo (keep them in the same folder).
2. Double-click `whisper-launcher.exe`.
3. Pick a model from the dropdown, click **Start**. First start downloads the model (~75-575 MB) and may take a few minutes.
4. When the status dot turns green, drag a `.wav` / `.mp3` / `.m4a` onto the drop area to test, or point any OpenAI-style client at `http://localhost:8088/v1/audio/transcriptions`.

> ⚠️ **The Whisper model is _not_ included in this repository.** The model file
> (75 MB - 575 MB depending on size) is downloaded from
> [huggingface.co/ggerganov/whisper.cpp](https://huggingface.co/ggerganov/whisper.cpp)
> the first time you start the server. This keeps the repo small and avoids
> re-distributing the model weights. A working internet connection is required
> on first start.

## What's in this repo

| File | Size | What it is |
|---|---|---|
| `whisper-launcher.exe` | ~190 KB | GUI launcher (**framework-dependent** — needs the [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0)). |
| `whisper-server-bundle.zip` | ~19 MB | `whisper-server.exe` + `whisper-cli.exe` + 5 native DLLs (whisper, ggml, ggml-base, ggml-cpu, ggml-vulkan) + VC++ 2015-2022 redist DLLs + a sample `jfk.wav`. |
| `launcher/` | — | C# WinForms source for the launcher. |

**No .NET installed?** Grab the **self-contained** launcher (~69 MB, bundles the
.NET runtime, no prerequisites) from the
[Releases page](https://github.com/nyancodex/whisper-server-vulkan-windows/releases) instead.

## The GUI launcher

`whisper-launcher.exe` is a single window with:

- **Model dropdown** — pick by size / accuracy (see table below). Your choice is remembered between runs.
- **Install dir / Port / Bind host** — defaults are `%USERPROFILE%\whisper-server`, `8088`, `0.0.0.0`. Change them if you want.
- **Start / Stop** buttons and a **status dot** (grey = idle, green = running on `<endpoint>`).
- **Progress bar** for the model download.
- **Log pane** streaming whisper-server's stdout/stderr (handy for watching the Vulkan GPU bind on first start).
- **Drag-and-drop test area** — drop an audio file, see the transcript inline.

Settings persist to `%LOCALAPPDATA%\whisper-launcher\settings.json`.

### Models

| Model | Size | WER (LS-clean) | Notes |
|---|---|---|---|
| `ggml-tiny.bin` | 75 MB | ~7-8% | Fastest, English-heavy, low-power boxes |
| `ggml-base.bin` | 142 MB | ~5% | Good speed/quality tradeoff |
| `ggml-small.bin` | 466 MB | ~3.4% | Balanced classic pick |
| `ggml-large-v3-turbo-q5_0.bin` | 547 MB | ~2% | Multilingual (99 langs), **recommended default** |

The model lives at `<InstallDir>\models\<file>.bin` after download. Re-running
with the same install dir + model reuses the cached file — no second download.

## Prerequisites

- Windows 10 / 11 x64
- A working Vulkan GPU driver (provides `C:\Windows\System32\vulkan-1.dll`)
  - AMD: install **Adrenalin Software** — works on Radeon RX 400-series and newer
  - NVIDIA: any recent **GeForce Game Ready** or **Studio** driver
  - Intel: **Arc Graphics** drivers from intel.com
  - Verify with: `vulkaninfo --summary` (if installed) or check that the file exists
- [.NET 8 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/8.0) — **only** for the small launcher in this repo. The self-contained launcher from Releases needs nothing.
- ~600 MB free disk space (19 MB bundle + ~550 MB model)
- Internet access on first start (for the model download)

No compiler, no Vulkan SDK, no Python, no Docker.

## Headless / scripted use

The launcher has a `--selftest` mode that runs the full extract → start → transcribe
flow without the GUI (it expects the bundle zip next to the exe and downloads the
default model if not cached):

```powershell
.\whisper-launcher.exe --selftest
```

For fully unattended deploys you can also run `whisper-server.exe` from the bundle
directly:

```powershell
# after extracting whisper-server-bundle.zip
.\whisper-server.exe -m models\ggml-large-v3-turbo-q5_0.bin `
    --host 0.0.0.0 --port 8088 --inference-path /v1/audio/transcriptions
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

The GUI launcher is a .NET 8 WinForms app (source in [`launcher/`](launcher/)). To build it:

```powershell
cd launcher
dotnet build -c Debug                                            # dev build (needs .NET 8 SDK)
dotnet publish -c Release -r win-x64 --self-contained false `
    -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=false  # ~190 KB framework-dependent
dotnet publish -c Release -r win-x64 --self-contained true       # ~69 MB, no runtime needed
```

## Performance notes

On an AMD Radeon RX 6700 XT with Vulkan, `ggml-large-v3-turbo-q5_0.bin` transcribes an 11-second clip in ~3 seconds wall-clock from a cold start (model already in GPU VRAM). Hot inference is much faster — sub-second per chunk for short clips. CPU fallback (with `-ng`) is roughly an order of magnitude slower.

Larger / non-quantized models (`ggml-large-v3.bin` ~3.1 GB) give marginally better WER but use ~5× more VRAM and run ~2-3× slower.

## Known issues

- **First request after model load** is slower than steady-state (Vulkan shader compile + memory allocation).
- **Long clips** (> ~30 minutes) work but get held in a single HTTP request — set generous client timeouts.
- **MSYS / Git-Bash users**: when running `whisper-server.exe` directly from Git-Bash, the `--inference-path /v1/audio/transcriptions` argument gets mangled by Git-Bash's POSIX-path translation. Prefix the command with `MSYS_NO_PATHCONV=1` or run from native cmd / PowerShell. The launcher avoids this by launching the process directly via the .NET API.
- **SmartScreen** may warn the first time you run the launcher (unsigned exe). Click *More info → Run anyway*. The source is in this repo if you'd rather build it yourself.

## Licensing

The bundle redistributes whisper.cpp binaries and the Microsoft Visual C++ runtime DLLs.

- **whisper.cpp**: [MIT](https://github.com/ggml-org/whisper.cpp/blob/master/LICENSE) — Copyright (c) 2023-present Georgi Gerganov
- **ggml**: [MIT](https://github.com/ggml-org/ggml/blob/master/LICENSE) — Copyright (c) 2022-present Georgi Gerganov
- **Microsoft Visual C++ Redistributable** (`MSVCP140.dll`, `VCRUNTIME140.dll`, `VCRUNTIME140_1.dll`): redistributed under the [Microsoft Visual C++ Redistributable runtime files redistribution license](https://learn.microsoft.com/en-us/visualstudio/releases/2022/redistribution#visual-c-runtime-files)
- **Whisper models** (downloaded by the launcher, not in this repo): MIT — Copyright (c) 2022 OpenAI

The launcher source, README, and the rest of this repo are released under MIT — see [LICENSE](LICENSE).

## Related

- [ggml-org/whisper.cpp](https://github.com/ggml-org/whisper.cpp) — the upstream project
- [ggerganov/whisper.cpp on HuggingFace](https://huggingface.co/ggerganov/whisper.cpp) — model files
- [OpenAI Audio API reference](https://platform.openai.com/docs/api-reference/audio/createTranscription) — the API shape this server implements
