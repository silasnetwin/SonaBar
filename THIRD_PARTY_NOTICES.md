# SonaBar third-party notices

SonaBar is an independent Windows application by SilasNet and is not affiliated with any third-party dictation product. The SonaBar name, icon and interface are original assets. No third-party source code, application package or asset files are redistributed; the interface follows the compact dictation-bar category.

The `licenses` directory in the distribution contains upstream license texts. Native libraries remain separate and replaceable. SonaBar imposes no restriction on reverse engineering or replacing LGPL components for debugging modifications to those components.

| Component | Purpose | License / source |
|---|---|---|
| .NET 10, Windows Desktop runtime, Microsoft.Extensions.*, Microsoft.Data.Sqlite | Self-contained application runtime and infrastructure | MIT; https://github.com/dotnet/runtime, https://github.com/dotnet/wpf, https://github.com/dotnet/efcore |
| NAudio 2.2.1 | WASAPI capture, resampling | MIT, Mark Heath and contributors; https://github.com/naudio/NAudio |
| whisper.cpp 1.9.4 / ggml | Direct C ABI local speech inference; pinned as `third_party/whisper.cpp` | MIT, Georgi Gerganov and contributors; https://github.com/ggml-org/whisper.cpp |
| NeMo-Speech.cpp 0.1.0 | Direct C ABI Parakeet TDT local speech inference; pinned as `third_party/NeMo-Speech.cpp` | Apache-2.0, NVIDIA Corporation & affiliates; https://github.com/NVIDIA/NeMo-Speech.cpp |
| SentencePiece | Tokenizer linked into the Parakeet runtime | Apache-2.0, Google Inc.; https://github.com/google/sentencepiece |
| Abseil | Runtime support library linked into the Parakeet runtime | Apache-2.0, The Abseil Authors; https://github.com/abseil/abseil-cpp |
| Protocol Buffers | Serialization runtime linked into the Parakeet runtime | BSD-3-Clause, Google Inc.; https://github.com/protocolbuffers/protobuf |
| NVIDIA Parakeet TDT 0.6B v3 GGUF | Optional offline multilingual speech model | CC-BY-4.0, NVIDIA; https://huggingface.co/nvidia/parakeet-tdt-0.6b-v3 |
| OpenAI Whisper models | Downloadable multilingual speech models | MIT, OpenAI; https://github.com/openai/whisper; GGML conversions: https://huggingface.co/ggerganov/whisper.cpp |
| LLamaSharp 0.27.0 | Local writing bindings | MIT, SciSharp contributors; https://github.com/SciSharp/LLamaSharp |
| llama.cpp / ggml | CPU / Vulkan writing inference | MIT, Georgi Gerganov and contributors; https://github.com/ggml-org/llama.cpp |
| Qwen2.5-1.5B-Instruct GGUF | Downloadable local writing model | Apache-2.0, Alibaba Cloud / Qwen; https://huggingface.co/Qwen/Qwen2.5-1.5B-Instruct-GGUF |
| Qwen3-4B-Instruct-2507 GGUF | Recommended local writing model | Apache-2.0, Qwen; quantization by Unsloth; https://huggingface.co/unsloth/Qwen3-4B-Instruct-2507-GGUF |
| Qwen3-ASR-1.7B-hf | Optional offline multilingual speech model, downloaded separately | Apache-2.0, Alibaba Cloud / Qwen; pinned revision `bcd2b5b7f32b480ab5790554cfa8347f246a14f3`; https://huggingface.co/Qwen/Qwen3-ASR-1.7B-hf |
| CPython 3.12.15 embedded runtime | Private Python process for the Qwen sidecar | PSF-2.0, Python Software Foundation; runtime build from https://github.com/astral-sh/python-build-standalone |
| PyTorch 2.14.1 (CPU) | Qwen tensor inference | BSD-3-Clause, The PyTorch Foundation and contributors; https://github.com/pytorch/pytorch |
| Transformers 5.20.2 | Qwen model loading and generation | Apache-2.0, Hugging Face; https://github.com/huggingface/transformers |
| Pinned Qwen Python dependencies | Tokenizers, NumPy, SoundFile, Hugging Face Hub, Safetensors, and support libraries | Apache-2.0, MIT, BSD-3-Clause, PSF-2.0, and other upstream licenses; exact versions in `scripts/qwen-requirements.txt`; texts in `licenses/qwen` |
| sherpa-onnx 1.13.8 | Local speaker embedding inference | Apache-2.0, Xiaomi Corporation and contributors; https://github.com/k2-fsa/sherpa-onnx |
| ONNX Runtime | Native inference used by sherpa-onnx | MIT, Microsoft; https://github.com/microsoft/onnxruntime |
| WeSpeaker VoxCeleb ResNet34 ONNX | Downloadable speaker embeddings | Apache-2.0, WeSpeaker team; https://github.com/wenet-e2e/wespeaker; ONNX conversion distributed by sherpa-onnx |
| SQLite, SQLitePCLRaw 3.0.5 | Local database | SQLite public domain; SQLitePCLRaw Apache-2.0, SourceGear / Eric Sink; https://sqlite.org/copyright.html, https://github.com/ericsink/SQLitePCL.raw |
| PDFsharp WPF 6.2.2 | Meeting PDF export | MIT, empira Software GmbH; https://github.com/empira/PDFsharp |
| FFmpeg LGPL shared build | Audio/video decoding | LGPL-3.0-or-later, FFmpeg developers; build n8.1.3-14-g330caae0c1-20261001 from https://github.com/BtbN/FFmpeg-Builds/releases/tag/autobuild-2026-10-01-13-06 |
| Microsoft Visual C++ release runtime | App-local native dependency | Microsoft Visual Studio redistributable license; https://learn.microsoft.com/visualstudio/productinfo/2022-redistribution-vs |

## FFmpeg source and replacement

SonaBar invokes FFmpeg as a separate process. The `native` directory contains the LGPL shared build, not a GPL/nonfree build. Its DLLs can be replaced by compatible builds. The exact binary hash is pinned in `scripts/dependencies.json`. The full upstream build recipe and dependency source locations are available at https://github.com/BtbN/FFmpeg-Builds and https://github.com/BtbN/FFmpeg-Builds/tree/master/scripts.d. FFmpeg source revision: https://github.com/FFmpeg/FFmpeg/tree/330caae0c1. FFmpeg licensing documentation: https://ffmpeg.org/legal.html.

Before public distribution, retain the corresponding source and build recipes for this exact FFmpeg build alongside the release download, including enabled external library sources. This repository's development artifacts are unsigned; no public hosting or release publication is performed by the build script.

## Models

Models are not embedded in the standard installer. Downloads use HTTPS and must match the byte size and SHA-256 digest in `src/SonaBar.Models/catalog.json`. The model manager retains no Hugging Face account credentials. Model license texts are included even when model weights are downloaded later. The Qwen3-ASR bundle includes its embedded Python runtime, but its model weights are downloaded separately and are never committed to this repository.

## Build-only dependencies

xUnit.net (Apache-2.0), Microsoft.NET.Test.Sdk (MIT), and the .NET SDK are used for building/testing and are not requirements for end users. Exact restored dependencies can be audited with `dotnet list SonaBar.slnx package --include-transitive`.
