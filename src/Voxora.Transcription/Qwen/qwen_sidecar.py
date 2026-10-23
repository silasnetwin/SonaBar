#!/usr/bin/env python3
"""Private Qwen3-ASR worker for SineFlow.

The host starts this process with a random named pipe and a per-launch HMAC secret.
Only length-prefixed JSON crosses the pipe. There is no network listener, no shell, and
the model is always loaded with local_files_only=True.
"""
from __future__ import annotations

import base64
import ctypes
import gc
import hashlib
import hmac
import json
import os
import queue
import secrets
import struct
import sys
import threading
import time
import traceback
from ctypes import wintypes
from dataclasses import dataclass

MAX_FRAME = 8 * 1024 * 1024
ERROR_FILE_NOT_FOUND = 2
ERROR_PIPE_BUSY = 231
GENERIC_READ = 0x80000000
GENERIC_WRITE = 0x40000000
OPEN_EXISTING = 3
INVALID_HANDLE_VALUE = ctypes.c_void_p(-1).value


def debug(message: str) -> None:
    path = os.environ.get("SINEFLOW_QWEN_LOG")
    if not path:
        return
    try:
        with open(path, "a", encoding="utf-8") as stream:
            stream.write(f"{time.time():.3f} {message}\n")
    except Exception:
        pass


LANGUAGES = {
    "ar": "Arabic", "yue": "Cantonese", "zh": "Chinese", "cs": "Czech",
    "da": "Danish", "nl": "Dutch", "en": "English", "fil": "Filipino",
    "fi": "Finnish", "fr": "French", "de": "German", "el": "Greek",
    "hi": "Hindi", "hu": "Hungarian", "id": "Indonesian", "it": "Italian",
    "ja": "Japanese", "ko": "Korean", "mk": "Macedonian", "ms": "Malay",
    "fa": "Persian", "pl": "Polish", "pt": "Portuguese", "ro": "Romanian",
    "ru": "Russian", "es": "Spanish", "sv": "Swedish", "th": "Thai",
    "tr": "Turkish", "vi": "Vietnamese",
}


class Cancelled(Exception):
    pass


class Pipe:
    def __init__(self, name: str):
        if not name or "\\" in name:
            raise ValueError("Invalid sidecar pipe name.")
        self.path = "\\\\.\\pipe\\" + name
        self.handle = None
        self.write_lock = threading.Lock()
        kernel32 = ctypes.WinDLL("kernel32", use_last_error=True)
        kernel32.CreateFileW.restype = wintypes.HANDLE
        kernel32.CreateFileW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD, wintypes.DWORD, wintypes.LPVOID, wintypes.DWORD, wintypes.DWORD, wintypes.HANDLE]
        kernel32.ReadFile.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID]
        kernel32.PeekNamedPipe.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(wintypes.DWORD), ctypes.POINTER(wintypes.DWORD)]
        kernel32.PeekNamedPipe.restype = wintypes.BOOL
        kernel32.WriteFile.argtypes = [wintypes.HANDLE, wintypes.LPVOID, wintypes.DWORD, ctypes.POINTER(wintypes.DWORD), wintypes.LPVOID]
        kernel32.WaitNamedPipeW.argtypes = [wintypes.LPCWSTR, wintypes.DWORD]
        kernel32.CloseHandle.argtypes = [wintypes.HANDLE]
        self.kernel32 = kernel32

    def connect(self, timeout: float = 30.0) -> None:
        deadline = time.monotonic() + timeout
        while True:
            handle = self.kernel32.CreateFileW(self.path, GENERIC_READ | GENERIC_WRITE, 0, None, OPEN_EXISTING, 0, None)
            if handle not in (None, INVALID_HANDLE_VALUE):
                self.handle = handle
                return
            error = ctypes.get_last_error()
            debug(f"connect error {error}")
            if error not in (ERROR_FILE_NOT_FOUND, ERROR_PIPE_BUSY):
                raise OSError(error, "Could not connect to the SineFlow named pipe.")
            if time.monotonic() >= deadline:
                raise TimeoutError("Timed out connecting to the SineFlow named pipe.")
            self.kernel32.WaitNamedPipeW(self.path, 500)

    def _available(self) -> int:
        available = wintypes.DWORD()
        if not self.kernel32.PeekNamedPipe(self.handle, None, 0, None, ctypes.byref(available), None):
            raise OSError(ctypes.get_last_error(), "Could not inspect the SineFlow named pipe.")
        return available.value

    def _read_exact(self, count: int) -> bytes:
        chunks: list[bytes] = []
        remaining = count
        while remaining:
            available = self._available()
            if available == 0:
                time.sleep(0.02)
                continue
            take = min(available, remaining)
            buffer = ctypes.create_string_buffer(take)
            read = wintypes.DWORD()
            if not self.kernel32.ReadFile(self.handle, buffer, take, ctypes.byref(read), None):
                raise OSError(ctypes.get_last_error(), "Could not read from the SineFlow named pipe.")
            if read.value == 0:
                raise EOFError("The SineFlow named pipe closed.")
            chunks.append(buffer.raw[:read.value])
            remaining -= read.value
        return b"".join(chunks)

    def read_frame(self) -> dict:
        header = self._read_exact(4)
        length = struct.unpack("<I", header)[0]
        if length == 0 or length > MAX_FRAME:
            raise ValueError("Invalid sidecar frame size.")
        return json.loads(self._read_exact(length).decode("utf-8"))

    def write_frame(self, message: dict) -> None:
        payload = json.dumps(message, ensure_ascii=False, separators=(",", ":")).encode("utf-8")
        if not payload or len(payload) > MAX_FRAME:
            raise ValueError("Invalid sidecar message size.")
        header = struct.pack("<I", len(payload))
        with self.write_lock:
            self._write_all(header)
            self._write_all(payload)

    def _write_all(self, data: bytes) -> None:
        view = memoryview(data)
        while view:
            chunk = bytes(view[:65536])
            buffer = ctypes.create_string_buffer(chunk)
            written = wintypes.DWORD()
            if not self.kernel32.WriteFile(self.handle, buffer, len(chunk), ctypes.byref(written), None):
                raise OSError(ctypes.get_last_error(), "Could not write to the SineFlow named pipe.")
            if written.value == 0:
                raise EOFError("The SineFlow named pipe closed.")
            view = view[written.value:]

    def close(self) -> None:
        if self.handle not in (None, INVALID_HANDLE_VALUE):
            try:
                self.kernel32.CloseHandle(self.handle)
            except Exception:
                pass
            self.handle = None


def proof(secret_base64: str, role: str, nonce: str) -> str:
    key = base64.b64decode(secret_base64, validate=True)
    message = f"sineflow-qwen-{role}:{nonce}".encode("utf-8")
    return base64.b64encode(hmac.new(key, message, hashlib.sha256).digest()).decode("ascii")


def verify_proof(secret_base64: str, role: str, nonce: str, value: object) -> bool:
    if not isinstance(value, str):
        return False
    try:
        expected = base64.b64decode(proof(secret_base64, role, nonce))
        actual = base64.b64decode(value, validate=True)
        return len(expected) == len(actual) and hmac.compare_digest(expected, actual)
    except Exception:
        return False


@dataclass
class Request:
    id: str
    op: str
    message: dict


class Worker:
    def __init__(self, pipe: Pipe, model_dir: str, secret: str):
        self.pipe = pipe
        self.model_dir = model_dir
        self.secret = secret
        self.requests: queue.Queue[Request | None] = queue.Queue()
        self.stop = threading.Event()
        self.state_lock = threading.Lock()
        self.current_id: str | None = None
        self.cancel_event = threading.Event()
        self.cancelled: set[str] = set()
        self.processor = None
        self.model = None
        self.torch = None
        self.dtype = None
        self.threads = max(1, min(32, int(os.environ.get("SINEFLOW_QWEN_THREADS", "4") or "4")))

    def send(self, message: dict) -> None:
        try:
            self.pipe.write_frame(message)
        except Exception:
            self.stop.set()

    def cancel(self, request_id: object) -> None:
        if not isinstance(request_id, str):
            return
        with self.state_lock:
            if self.current_id == request_id:
                self.cancel_event.set()
            else:
                self.cancelled.add(request_id)

    def submit(self, message: dict) -> None:
        request_id = message.get("id")
        op = message.get("op")
        debug(f"submit {op}")
        if not isinstance(request_id, str) or not request_id or op not in ("warm", "transcribe", "unload"):
            self.send({"op": "error", "id": request_id if isinstance(request_id, str) else "", "message": "Invalid sidecar request."})
            return
        self.requests.put(Request(request_id, op, message))

    def run(self) -> None:
        debug("worker run start")
        while not self.stop.is_set():
            debug("worker waiting")
            request = self.requests.get()
            debug(f"worker got {request.op if request else 'stop'}")
            if request is None:
                break
            with self.state_lock:
                self.current_id = request.id
                self.cancel_event.clear()
                was_cancelled = request.id in self.cancelled
                self.cancelled.discard(request.id)
            try:
                if was_cancelled:
                    raise Cancelled()
                if request.op == "warm":
                    self.load_model(request)
                    if self.cancel_event.is_set():
                        raise Cancelled()
                    self.send({"op": "warmed", "id": request.id})
                elif request.op == "transcribe":
                    self.transcribe(request)
                elif request.op == "unload":
                    self.unload_model()
                    self.send({"op": "unloaded", "id": request.id})
            except Cancelled:
                self.send({"op": "error", "id": request.id, "message": "Transcription canceled.", "cancelled": True})
            except Exception as error:
                debug("worker exception: " + traceback.format_exc())
                traceback.print_exc(file=sys.stderr)
                sys.stderr.flush()
                self.send({"op": "error", "id": request.id, "message": f"{type(error).__name__}: {error}"})
            finally:
                with self.state_lock:
                    self.current_id = None
                    self.cancel_event.clear()

    def load_model(self, request: Request) -> None:
        if self.model is not None:
            debug("model already loaded")
            return
        debug("loading model")
        self.send({"op": "progress", "id": request.id, "stage": "loading", "message": "Loading Qwen3-ASR on the CPU."})
        import torch
        debug("torch imported")
        from transformers import AutoModelForMultimodalLM, AutoProcessor
        debug("transformers imported")
        torch.set_num_threads(self.threads)
        torch.set_grad_enabled(False)
        dtype_name = (os.environ.get("SINEFLOW_QWEN_DTYPE") or "bfloat16").lower()
        dtype = {"bfloat16": torch.bfloat16, "float32": torch.float32, "float16": torch.float16}.get(dtype_name, torch.bfloat16)
        processor = AutoProcessor.from_pretrained(self.model_dir, local_files_only=True)
        debug("processor loaded")
        model = AutoModelForMultimodalLM.from_pretrained(self.model_dir, dtype=dtype, local_files_only=True)
        debug("model loaded")
        model.eval()
        self.torch = torch
        self.dtype = dtype
        self.processor = processor
        self.model = model
        self.send({"op": "progress", "id": request.id, "stage": "ready", "message": "Qwen3-ASR is ready."})

    def unload_model(self) -> None:
        self.model = None
        self.processor = None
        self.torch = None
        self.dtype = None
        gc.collect()

    def transcribe(self, request: Request) -> None:
        if self.cancel_event.is_set():
            raise Cancelled()
        self.load_model(request)
        if self.cancel_event.is_set():
            raise Cancelled()
        message = request.message
        wave = message.get("wave")
        if not isinstance(wave, str) or not os.path.isfile(wave):
            raise FileNotFoundError("The recorded WAV file was not found.")
        language = message.get("language")
        if not isinstance(language, str) or not language:
            language = "auto"
        language = language.lower()
        if language == "tl":
            language = "fil"
        if language != "auto" and language not in LANGUAGES:
            raise ValueError("Qwen3-ASR does not support this language.")
        prompt = message.get("prompt")
        if not isinstance(prompt, str):
            prompt = ""
        prompt = prompt.strip()[:4000]
        requested_max_tokens = message.get("maxNewTokens", 1024)
        try:
            requested_max_tokens = max(256, min(4096, int(requested_max_tokens)))
        except (TypeError, ValueError):
            requested_max_tokens = 1024
        audio, duration = read_wave(wave)
        max_new_tokens = max(256, min(requested_max_tokens, int(duration * 20)))
        if self.cancel_event.is_set():
            raise Cancelled()
        self.send({"op": "progress", "id": request.id, "stage": "transcribing", "message": "Transcribing locally."})
        inputs = self.processor.apply_transcription_request(
            audio=audio,
            language=None if language == "auto" else language,
            prompt=prompt or None,
        ).to("cpu", self.dtype)
        criteria = [CancellationCriteria(self.cancel_event)]
        with self.torch.inference_mode():
            output_ids = self.model.generate(**inputs, max_new_tokens=max_new_tokens, do_sample=False, stopping_criteria=criteria)
        if self.cancel_event.is_set():
            raise Cancelled()
        generated = output_ids[:, inputs["input_ids"].shape[1]:]
        parsed = self.processor.decode(generated, return_format="parsed")[0]
        if isinstance(parsed, dict):
            text = parsed.get("transcription") or ""
            language_name = parsed.get("language")
        else:
            text = str(parsed)
            language_name = None
        language_code = None
        if isinstance(language_name, str):
            for code, name in LANGUAGES.items():
                if name.lower() == language_name.lower():
                    language_code = "tl" if code == "fil" else code
                    break
        if not language_code:
            language_code = language if language != "auto" else "auto"
        self.send({
            "op": "result",
            "id": request.id,
            "text": text,
            "languageCode": language_code,
            "language": language_name,
            "durationSeconds": duration,
        })


class CancellationCriteria:
    def __init__(self, event: threading.Event):
        self.event = event

    def __call__(self, input_ids, scores, **kwargs) -> bool:
        return self.event.is_set()


def read_wave(path: str):
    import numpy as np
    import soundfile as sf
    audio, sample_rate = sf.read(path, dtype="float32", always_2d=True)
    if sample_rate != 16000:
        raise ValueError(f"Qwen3-ASR requires a 16 kHz WAV file, received {sample_rate} Hz.")
    if audio.shape[1] != 1:
        raise ValueError("Qwen3-ASR requires mono audio.")
    samples = np.ascontiguousarray(audio[:, 0], dtype=np.float32)
    return samples, samples.shape[0] / 16000.0


def main() -> int:
    debug("main start")
    pipe_name = os.environ.get("SINEFLOW_QWEN_PIPE")
    secret = os.environ.get("SINEFLOW_QWEN_SECRET")
    model_dir = os.environ.get("SINEFLOW_QWEN_MODEL")
    if not pipe_name or not secret or not model_dir:
        print("Missing SineFlow sidecar environment.", file=sys.stderr)
        return 2
    if not os.path.isdir(model_dir):
        print("The Qwen model directory was not found.", file=sys.stderr)
        return 2
    debug("environment ok")
    pipe = Pipe(pipe_name)
    try:
        debug("connecting")
        pipe.connect()
        debug("connected")
        nonce = base64.b64encode(secrets.token_bytes(32)).decode("ascii")
        pipe.write_frame({"op": "hello", "nonce": nonce})
        debug("hello sent")
        auth = pipe.read_frame()
        debug("auth received")
        if auth.get("op") != "auth" or not verify_proof(secret, "client", nonce, auth.get("proof")):
            raise PermissionError("The SineFlow host did not authenticate.")
        pipe.write_frame({"op": "ready", "proof": proof(secret, "server", nonce), "version": "1"})
        debug("ready sent")
        worker = Worker(pipe, model_dir, secret)
        thread = threading.Thread(target=worker.run, name="sineflow-qwen-worker", daemon=True)
        thread.start()
        debug("worker started")
        while not worker.stop.is_set():
            message = pipe.read_frame()
            op = message.get("op")
            debug(f"request {op}")
            if op == "shutdown":
                worker.stop.set()
                with worker.state_lock:
                    worker.cancel_event.set()
                worker.requests.put(None)
                break
            if op == "cancel":
                worker.cancel(message.get("id"))
            elif op in ("warm", "transcribe", "unload"):
                worker.submit(message)
            else:
                worker.send({"op": "error", "id": message.get("id") if isinstance(message.get("id"), str) else "", "message": "Unknown sidecar request."})
        debug("loop done")
        worker.stop.set()
        worker.requests.put(None)
        thread.join(timeout=2.0)
        return 0
    except Exception:
        debug("exception: " + traceback.format_exc())
        traceback.print_exc(file=sys.stderr)
        sys.stderr.flush()
        return 1
    finally:
        debug("closing")
        pipe.close()


if __name__ == "__main__":
    raise SystemExit(main())
