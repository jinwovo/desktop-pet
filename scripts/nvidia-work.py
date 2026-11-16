#!/usr/bin/env python3
"""Local NVIDIA work helper. Model replies are text, never executable commands."""
from __future__ import annotations

import argparse
from dataclasses import dataclass, field
from email.utils import parsedate_to_datetime
import http.client
import json
import math
import os
from pathlib import Path
import re
import sys
import tempfile
import time
from urllib import error, request
from urllib.parse import urlsplit

ENV_NAMES = {"NVIDIA_BASE_URL", "NVIDIA_API_KEY", *(f"NVIDIA_NIM_MODEL_{i}" for i in range(1, 6))}
MAX_RESPONSE_BYTES = 16 * 1024 * 1024


@dataclass(frozen=True)
class Config:
    base_url: str
    api_key: str = field(repr=False)
    chat_models: tuple[str, ...]
    embedding_model: str | None


def read_env(path: Path) -> dict[str, str]:
    if path.stat().st_size > 1024 * 1024:
        raise ValueError("Environment file exceeds 1 MiB")
    values = {}
    for line in path.read_text(encoding="utf-8-sig").splitlines():
        match = re.match(r"^\s*(?:export\s+)?([A-Z0-9_]+)\s*=\s*(.*?)\s*$", line)
        if not match or match[1] not in ENV_NAMES:
            continue
        value = match[2]
        if value.startswith(('"', "'")):
            quote = value[0]
            end = value.find(quote, 1)
            if end < 0:
                raise ValueError("Invalid quoted NVIDIA environment value")
            tail = value[end + 1:].strip()
            if tail and not tail.startswith("#"):
                raise ValueError("Invalid quoted NVIDIA environment value")
            value = value[1:end]
        else:
            value = re.split(r"\s+#", value, maxsplit=1)[0].strip()
        values[match[1]] = value
    return values


def load_config(env_file: Path | None) -> Config:
    values = read_env(env_file) if env_file else {}
    values.update({name: os.environ[name] for name in ENV_NAMES if name in os.environ})
    missing = [name for name in ("NVIDIA_API_KEY", *(f"NVIDIA_NIM_MODEL_{i}" for i in range(1, 5))) if not values.get(name)]
    if missing:
        raise ValueError("Missing configuration: " + ", ".join(missing))
    key = values["NVIDIA_API_KEY"].strip()
    if not key or key == "<API_KEY>" or any(c in key for c in "\r\n"):
        raise ValueError("NVIDIA_API_KEY is a placeholder or invalid")
    base = values.get("NVIDIA_BASE_URL", "https://integrate.api.nvidia.com/v1").rstrip("/")
    parts = urlsplit(base)
    if parts.scheme != "https" or not parts.hostname or parts.username or parts.password or parts.query or parts.fragment:
        raise ValueError("NVIDIA_BASE_URL must be an HTTPS URL without credentials, query or fragment")
    models = tuple(values[f"NVIDIA_NIM_MODEL_{i}"].strip() for i in range(1, 5))
    if any(not model or "embed" in model.lower() or any(c in model for c in "\r\n") for model in models):
        raise ValueError("Models 1 through 4 must be chat models")
    return Config(base, key, models, values.get("NVIDIA_NIM_MODEL_5") or None)


class AttemptFailed(Exception):
    def __init__(self, kind: str, retry_after: float = 0, fatal: bool = False):
        super().__init__(kind)
        self.retry_after = retry_after
        self.fatal = fatal


class NoRedirect(request.HTTPRedirectHandler):
    def redirect_request(self, req, fp, code, msg, headers, newurl):
        # Never forward the API key to a redirect destination.
        return None


def retry_after_seconds(value: str | None) -> float:
    if not value:
        return 0
    try:
        seconds = float(value)
    except ValueError:
        try:
            seconds = parsedate_to_datetime(value).timestamp() - time.time()
        except (ValueError, TypeError, OverflowError):
            return 0
    return max(0, min(seconds, 300)) if seconds == seconds else 0


class Client:
    def __init__(self, config: Config, timeout: float = 60, opener=None):
        self.config = config
        self.timeout = timeout
        self.opener = opener or request.build_opener(NoRedirect())

    def post(self, endpoint: str, payload: dict) -> dict:
        req = request.Request(
            self.config.base_url + endpoint,
            data=json.dumps(payload, ensure_ascii=False).encode("utf-8"),
            headers={"Authorization": "Bearer " + self.config.api_key, "Content-Type": "application/json", "Accept": "application/json"},
            method="POST",
        )
        try:
            with self.opener.open(req, timeout=self.timeout) as response:
                if response.status != 200:
                    raise AttemptFailed(f"HTTP {response.status}")
                body = response.read(MAX_RESPONSE_BYTES + 1)
                if len(body) > MAX_RESPONSE_BYTES:
                    raise AttemptFailed("response too large")
                result = json.loads(body)
                if not isinstance(result, dict):
                    raise AttemptFailed("invalid response")
                return result
        except error.HTTPError as ex:
            # Error bodies can reflect request data. Log only status, never headers/key/body.
            ex.close()
            raise AttemptFailed(f"HTTP {ex.code}", retry_after_seconds(ex.headers.get("Retry-After") if ex.headers else None), fatal=ex.code == 401) from None
        except (error.URLError, TimeoutError, OSError, http.client.HTTPException):
            raise AttemptFailed("network or timeout") from None
        except (ValueError, UnicodeError):
            raise AttemptFailed("invalid JSON response") from None

    def chat(self, model: str, prompt: str, max_tokens: int) -> dict:
        payload = {"model": model, "messages": [{"role": "user", "content": prompt}], "temperature": 0.2, "max_tokens": max_tokens, "stream": False}
        if "nemotron-3.5-super" in model or "nemotron-3.5-lightning" in model:
            payload["chat_template_kwargs"] = {"enable_thinking": False}
        elif "nemotron-3-ultra" in model:
            payload["reasoning_effort"] = "none"
        result = self.post("/chat/completions", payload)
        choices = result.get("choices")
        if not isinstance(choices, list) or not choices or not isinstance(choices[0], dict):
            raise AttemptFailed("missing completion")
        choice = choices[0]
        message = choice.get("message")
        content = message.get("content") if isinstance(message, dict) else None
        if not isinstance(content, str) or not content.strip():
            raise AttemptFailed("empty completion")
        if choice.get("finish_reason") in ("length", "content_filter"):
            raise AttemptFailed("incomplete completion")
        return {"model": model, "content": content, "usage": result.get("usage", {})}


def chat_with_fallback(client: Client, prompt: str, max_tokens: int = 4096, rounds: int = 0,
                       cooldown: float = 10, sleep=time.sleep, log=lambda text: print(text, file=sys.stderr)) -> dict:
    if client.config.api_key in prompt:
        raise ValueError("Prompt contains the configured API key; request was not sent")
    cycle = 0
    while rounds == 0 or cycle < rounds:
        cycle += 1
        retry_after = 0.0
        for index, model in enumerate(client.config.chat_models, 1):
            log(f"Round {cycle}, model {index}: {model}")
            try:
                return client.chat(model, prompt, max_tokens)
            except AttemptFailed as ex:
                log(f"Model {index} failed: {ex}")
                if ex.fatal:
                    raise
                retry_after = max(retry_after, ex.retry_after)
        if rounds and cycle >= rounds:
            break
        delay = max(retry_after, min(cooldown * (2 ** min(cycle - 1, 5)), 120))
        log(f"All four failed; retry model 1 in {delay:g}s (Ctrl+C to stop)")
        sleep(delay)
    raise AttemptFailed("all configured chat attempts failed")


def validate_embedding(result: dict) -> None:
    data = result.get("data")
    if not isinstance(data, list) or len(data) != 1 or not isinstance(data[0], dict):
        raise AttemptFailed("invalid embedding response")
    vector = data[0].get("embedding")
    if not isinstance(vector, list) or not vector:
        raise AttemptFailed("invalid embedding response")
    try:
        valid = all(isinstance(value, (int, float)) and not isinstance(value, bool) and math.isfinite(value) for value in vector)
    except OverflowError:
        valid = False
    if not valid:
        raise AttemptFailed("invalid embedding values")


def save_output(path: Path, serialized: str) -> None:
    """Publish a complete output exclusively; never replace another task's result."""
    path.parent.mkdir(parents=True, exist_ok=True)
    temporary = None
    try:
        with tempfile.NamedTemporaryFile(mode="w", encoding="utf-8", dir=path.parent, prefix=".nvidia-work-", suffix=".tmp", delete=False) as output:
            temporary = Path(output.name)
            output.write(serialized + "\n")
            output.flush()
            os.fsync(output.fileno())
        # A hard link publishes the complete same-filesystem file atomically and fails if
        # the destination already exists, including concurrent writers or dangling links.
        os.link(temporary, path)
    finally:
        if temporary is not None:
            temporary.unlink(missing_ok=True)


def main(argv=None) -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--env-file", type=Path, help="Read only NVIDIA_* values; process environment takes precedence")
    parser.add_argument("--timeout", type=float, default=60)
    sub = parser.add_subparsers(dest="command", required=True)
    sub.add_parser("check", help="Validate configuration without printing the API key or making API requests")
    chat = sub.add_parser("chat", help="Try model 1,2,3,4, then cycle back to 1")
    chat.add_argument("--prompt-file", type=Path, required=True)
    chat.add_argument("--output", type=Path, required=True)
    chat.add_argument("--rounds", type=int, default=0, help="0 repeats until success or Ctrl+C")
    chat.add_argument("--max-tokens", type=int, default=4096)
    chat.add_argument("--cooldown", type=float, default=10)
    embed = sub.add_parser("embed", help="Use model 5 on /embeddings, separately from chat fallback")
    embed.add_argument("--input-file", type=Path, required=True)
    embed.add_argument("--output", type=Path, required=True)
    embed.add_argument("--input-type", choices=("query", "passage"), default="passage")
    args = parser.parse_args(argv)
    try:
        config = load_config(args.env_file)
        if not 1 <= args.timeout <= 600:
            raise ValueError("Timeout must be between 1 and 600 seconds")
        if args.command == "check":
            print(json.dumps({"base_url": config.base_url, "api_key_present": True, "chat_models": config.chat_models, "embedding_model": config.embedding_model}, ensure_ascii=True))
            return 0
        if args.output.exists():
            raise ValueError("Output already exists; choose a new file")
        client = Client(config, args.timeout)
        if args.command == "chat":
            if args.rounds < 0 or not 1 <= args.max_tokens <= 32768 or not 0 <= args.cooldown <= 300:
                raise ValueError("Invalid rounds, token limit or cooldown")
            prompt = args.prompt_file.read_text(encoding="utf-8-sig")
            result = chat_with_fallback(client, prompt, args.max_tokens, args.rounds, args.cooldown)
        else:
            if not config.embedding_model:
                raise ValueError("NVIDIA_NIM_MODEL_5 is not configured")
            content = args.input_file.read_text(encoding="utf-8-sig")
            if config.api_key in content:
                raise ValueError("Input contains the configured API key; request was not sent")
            result = client.post("/embeddings", {"model": config.embedding_model, "input": [content], "input_type": args.input_type, "encoding_format": "float"})
            validate_embedding(result)
        # Do not persist a credential if a malformed service response happens to reflect it.
        serialized = json.dumps(result, ensure_ascii=False, indent=2)
        if config.api_key in serialized:
            raise ValueError("Response contains the configured API key; output was not saved")
        save_output(args.output, serialized)
        print(json.dumps({"saved": str(args.output), "model": result.get("model"), "usage": result.get("usage", {})}, ensure_ascii=True))
        return 0
    except KeyboardInterrupt:
        print("Cancelled", file=sys.stderr)
        return 130
    except (ValueError, OSError, AttemptFailed) as ex:
        # Paths or raw transport errors can contain sensitive material: don't echo OSError text.
        print("NVIDIA work failed: " + ("local file error" if isinstance(ex, OSError) else str(ex)), file=sys.stderr)
        return 1


if __name__ == "__main__":
    raise SystemExit(main())
