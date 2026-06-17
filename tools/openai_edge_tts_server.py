#!/usr/bin/env python
"""Local OpenAI-compatible TTS server backed by edge-tts.

Install:
  pip install flask edge-tts miniaudio

Run:
  python tools/openai_edge_tts_server.py --host 127.0.0.1 --port 5050

Useful env vars:
  LOCAL_TTS_PROVIDER=edge|minimax
  LOCAL_TTS_TIMEOUT_SECONDS=30
  LOCAL_TTS_MAX_CONCURRENT=2
  LOCAL_TTS_CACHE_ENTRIES=64
  LOCAL_TTS_WARMUP=1
  LOCAL_TTS_WARMUP_VOICES=zh-CN-YunxiNeural,en-US-AriaNeural
  MINIMAX_API_KEY=<YOUR_MINIMAX_API_KEY>
  MINIMAX_BASE_URL=https://api.example.com
  MINIMAX_MODEL=speech-2.8-hd

Endpoints:
  POST /v1/speech
  POST /v1/audio/speech
"""

from __future__ import annotations

import argparse
import asyncio
import io
import json
import os
import threading
import urllib.request
import wave
from collections import OrderedDict
from typing import Any

from flask import Flask, Response, jsonify, request

try:
    import edge_tts
except ImportError as exc:  # pragma: no cover
    raise SystemExit("Missing dependency. Run: pip install flask edge-tts") from exc

try:
    import miniaudio
except ImportError:  # pragma: no cover
    miniaudio = None


DEFAULT_VOICE = os.environ.get("LOCAL_TTS_VOICE", "zh-CN-YunxiNeural")
DEFAULT_PROVIDER = os.environ.get("LOCAL_TTS_PROVIDER", "edge").strip().lower()
OPTIONAL_API_KEY = os.environ.get("LOCAL_TTS_API_KEY", "")
SYNTH_TIMEOUT_SECONDS = float(os.environ.get("LOCAL_TTS_TIMEOUT_SECONDS", "30"))
MAX_CONCURRENT_SYNTH = max(1, int(os.environ.get("LOCAL_TTS_MAX_CONCURRENT", "2")))
MAX_CACHE_ENTRIES = max(0, int(os.environ.get("LOCAL_TTS_CACHE_ENTRIES", "64")))
MINIMAX_API_KEY = os.environ.get("MINIMAX_API_KEY", "")
MINIMAX_BASE_URL = os.environ.get("MINIMAX_BASE_URL", "https://api.minimaxi.com").rstrip("/")
MINIMAX_MODEL = os.environ.get("MINIMAX_MODEL", "speech-2.8-hd")
WARMUP_ENABLED = os.environ.get("LOCAL_TTS_WARMUP", "").strip() in ("1", "true", "yes", "on")
WARMUP_TEXT = os.environ.get("LOCAL_TTS_WARMUP_TEXT", "warm up.")
WARMUP_VOICES = [
    v.strip() for v in os.environ.get("LOCAL_TTS_WARMUP_VOICES", DEFAULT_VOICE).split(",") if v.strip()
]

app = Flask(__name__)
_synth_gate = threading.BoundedSemaphore(MAX_CONCURRENT_SYNTH)
_cache_lock = threading.Lock()
_audio_cache: OrderedDict[tuple[str, str, str, str, str, str], bytes] = OrderedDict()


def require_auth() -> Response | None:
    if not OPTIONAL_API_KEY:
        return None
    auth = request.headers.get("Authorization", "")
    if auth != f"Bearer {OPTIONAL_API_KEY}":
        return jsonify({"error": "Unauthorized"}), 401
    return None


def edge_rate_from_speed(speed: Any) -> str:
    try:
        value = float(speed)
    except (TypeError, ValueError):
        value = 1.0
    value = max(0.25, min(4.0, value))
    percent = round((value - 1.0) * 100)
    sign = "+" if percent >= 0 else ""
    return f"{sign}{percent}%"


def clamp_float(value: Any, lo: float, hi: float, fallback: float) -> float:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return fallback
    return max(lo, min(hi, number))


async def synthesize_mp3(text: str, voice: str, speed: Any) -> bytes:
    communicate = edge_tts.Communicate(text, voice=voice, rate=edge_rate_from_speed(speed))
    chunks: list[bytes] = []
    async for chunk in communicate.stream():
        if chunk.get("type") == "audio" and chunk.get("data"):
            chunks.append(chunk["data"])
    return b"".join(chunks)


def get_cached_audio(key: tuple[str, str, str, str, str, str]) -> bytes | None:
    if MAX_CACHE_ENTRIES <= 0:
        return None
    with _cache_lock:
        audio = _audio_cache.get(key)
        if audio is None:
            return None
        _audio_cache.move_to_end(key)
        return audio


def put_cached_audio(key: tuple[str, str, str, str, str, str], audio: bytes) -> None:
    if MAX_CACHE_ENTRIES <= 0:
        return
    with _cache_lock:
        _audio_cache[key] = audio
        _audio_cache.move_to_end(key)
        while len(_audio_cache) > MAX_CACHE_ENTRIES:
            _audio_cache.popitem(last=False)


def run_synthesis(text: str, voice: str, speed: Any) -> bytes:
    async def _run() -> bytes:
        return await asyncio.wait_for(
            synthesize_mp3(text, voice, speed),
            timeout=SYNTH_TIMEOUT_SECONDS,
        )

    return asyncio.run(_run())


def synthesize_minimax(text: str, voice: str, speed: Any, response_format: str, model: str | None) -> bytes:
    if not MINIMAX_API_KEY:
        raise RuntimeError("MINIMAX_API_KEY is required for provider=minimax")

    url = f"{MINIMAX_BASE_URL}/v1/t2a_v2"
    fmt = "wav" if response_format == "wav" else "mp3"
    audio_setting: dict[str, Any] = {
        "sample_rate": 22050,
        "format": fmt,
        "channel": 1,
    }
    if fmt == "mp3":
        audio_setting["bitrate"] = 128000

    payload = {
        "model": model or MINIMAX_MODEL,
        "text": text,
        "stream": False,
        "voice_setting": {
            "voice_id": voice,
            "speed": clamp_float(speed, 0.5, 2.0, 1.0),
            "vol": 1,
            "pitch": 0,
        },
        "audio_setting": audio_setting,
        "output_format": "hex",
        "subtitle_enable": False,
        "aigc_watermark": False,
    }
    body = json.dumps(payload, ensure_ascii=False).encode("utf-8")
    req = urllib.request.Request(
        url,
        data=body,
        headers={
            "Authorization": f"Bearer {MINIMAX_API_KEY}",
            "Content-Type": "application/json; charset=utf-8",
        },
        method="POST",
    )
    with urllib.request.urlopen(req, timeout=SYNTH_TIMEOUT_SECONDS) as resp:
        raw = resp.read()
    data = json.loads(raw.decode("utf-8"))
    base_resp = data.get("base_resp") or {}
    if base_resp.get("status_code") not in (None, 0):
        raise RuntimeError(base_resp.get("status_msg") or f"MiniMax error {base_resp.get('status_code')}")
    audio_hex = (data.get("data") or {}).get("audio")
    if not audio_hex:
        raise RuntimeError("MiniMax returned empty audio")
    return bytes.fromhex(audio_hex)


def mp3_to_sapi_wav(mp3: bytes) -> bytes:
    if miniaudio is None:
        raise RuntimeError("Missing dependency for wav output. Run: pip install miniaudio")
    decoded = miniaudio.decode(
        mp3,
        output_format=miniaudio.SampleFormat.SIGNED16,
        nchannels=1,
        sample_rate=22050,
    )
    pcm = decoded.samples.tobytes()
    out = io.BytesIO()
    with wave.open(out, "wb") as wav:
        wav.setnchannels(decoded.nchannels)
        wav.setsampwidth(2)
        wav.setframerate(decoded.sample_rate)
        wav.writeframes(pcm)
    return out.getvalue()


def warmup() -> None:
    if not WARMUP_ENABLED:
        return
    for voice in WARMUP_VOICES:
        try:
            provider = DEFAULT_PROVIDER if DEFAULT_PROVIDER in ("edge", "minimax") else "edge"
            if provider == "minimax":
                audio = synthesize_minimax(WARMUP_TEXT, voice, 1.0, "wav", MINIMAX_MODEL)
                put_cached_audio((provider, MINIMAX_MODEL, voice, "1.0", "wav", WARMUP_TEXT), audio)
            else:
                mp3 = run_synthesis(WARMUP_TEXT, voice, 1.0)
                put_cached_audio((provider, "", voice, "1.0", "mp3", WARMUP_TEXT), mp3)
                if miniaudio is not None:
                    put_cached_audio((provider, "", voice, "1.0", "wav", WARMUP_TEXT), mp3_to_sapi_wav(mp3))
        except Exception as exc:
            app.logger.warning("warmup failed for %s: %s", voice, exc)


@app.get("/health")
def health() -> Response:
    return jsonify({
        "ok": True,
        "engine": "universal-local-tts",
        "default_provider": DEFAULT_PROVIDER,
        "default_voice": DEFAULT_VOICE,
        "minimax_configured": bool(MINIMAX_API_KEY),
        "timeout_seconds": SYNTH_TIMEOUT_SECONDS,
        "max_concurrent": MAX_CONCURRENT_SYNTH,
        "cache_entries": len(_audio_cache),
    })


@app.get("/v1/voices")
def voices() -> Response:
    auth_error = require_auth()
    if auth_error:
        return auth_error
    try:
        data = asyncio.run(edge_tts.list_voices())
        return jsonify({"data": data})
    except Exception as exc:  # pragma: no cover
        return jsonify({"error": str(exc)}), 500


@app.post("/v1/speech")
@app.post("/v1/audio/speech")
def speech() -> Response:
    auth_error = require_auth()
    if auth_error:
        return auth_error

    data = request.get_json(force=True, silent=True) or {}
    text = data.get("input") or data.get("text") or ""
    voice = data.get("voice") or DEFAULT_VOICE
    provider = (data.get("provider") or DEFAULT_PROVIDER or "edge").strip().lower()
    model = data.get("model")
    response_format = (data.get("response_format") or "mp3").lower()
    if not text:
        return jsonify({"error": "input is required"}), 400
    if response_format not in ("mp3", "wav"):
        return jsonify({"error": f"unsupported format {response_format}"}), 400
    if provider not in ("edge", "minimax"):
        return jsonify({"error": f"unsupported provider {provider}"}), 400

    speed = data.get("speed", 1.0)
    cache_key = (provider, str(model or ""), voice, str(speed), response_format, text)
    cached = get_cached_audio(cache_key)
    if cached is not None:
        mimetype = "audio/wav" if response_format == "wav" else "audio/mpeg"
        return Response(cached, mimetype=mimetype)

    try:
        if not _synth_gate.acquire(timeout=1.0):
            return jsonify({"error": "tts server is busy"}), 503
        try:
            if provider == "minimax":
                audio = synthesize_minimax(text, voice, speed, response_format, model)
            else:
                audio = run_synthesis(text, voice, speed)
        finally:
            _synth_gate.release()
        if provider == "edge" and response_format == "wav":
            audio = mp3_to_sapi_wav(audio)
    except TimeoutError:
        return jsonify({"error": f"tts timeout after {SYNTH_TIMEOUT_SECONDS:g}s"}), 504
    except Exception as exc:
        return jsonify({"error": str(exc)}), 500
    if not audio:
        return jsonify({"error": "empty audio"}), 500
    put_cached_audio(cache_key, audio)
    mimetype = "audio/wav" if response_format == "wav" else "audio/mpeg"
    return Response(audio, mimetype=mimetype)


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--host", default="127.0.0.1")
    parser.add_argument("--port", type=int, default=5050)
    parser.add_argument("--debug", action="store_true")
    args = parser.parse_args()
    if WARMUP_ENABLED:
        threading.Thread(target=warmup, daemon=True).start()
    app.run(host=args.host, port=args.port, debug=args.debug, threaded=True, use_reloader=False)


if __name__ == "__main__":
    main()
