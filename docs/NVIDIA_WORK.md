# NVIDIA-assisted development

`scripts/nvidia-work.py` is an optional development helper for sending an explicitly prepared prompt to NVIDIA NIM. It is separate from the desktop app and its Claude/Codex completion hooks. The desktop app does not require an NVIDIA account or API key. This helper does not switch the model running an existing Codex session or change that session's token allowance.

Python 3.10 or newer is required. No pip dependencies are needed.

## Configuration

Keep credentials in a local `.env` outside the published source, or use process environment variables. The helper reads only these names and does not execute or interpolate `.env` contents:

```dotenv
NVIDIA_BASE_URL=https://integrate.api.nvidia.com/v1
NVIDIA_API_KEY=<your actual key>
NVIDIA_NIM_MODEL_1=nvidia/nemotron-3.5-super-vl-120b-a12b
NVIDIA_NIM_MODEL_2=nvidia/nemotron-3-ultra-550b-a55b
NVIDIA_NIM_MODEL_3=nvidia/nemotron-3.5-lightning-30b-a3b
NVIDIA_NIM_MODEL_4=nvidia/nemotron-3-nano-omni-30b-a3b-reasoning
NVIDIA_NIM_MODEL_5=nvidia/nemotron-3-embed-1b
```

Pass `--env-file` explicitly. Process environment variables take precedence over that file. `check` prints the endpoint and model names, plus whether the key is configured; it never prints the key or contacts the service.

```powershell
python scripts/nvidia-work.py --env-file ../.env check
```

## Chat fallback

Write a task and selected source excerpts to `prompt.txt`, then run:

```powershell
python scripts/nvidia-work.py --env-file ../.env chat --prompt-file prompt.txt --output .local/nvidia/review.json
```

The sequence is **1 → 2 → 3 → 4 → 1**, stopping on the first complete, nonempty answer. A new task starts at model 1. Model 5 is excluded from chat. `--rounds 0` is the default and repeats until success or Ctrl+C; `--rounds 2` bounds the task to two passes. After all four fail, the helper waits with increasing cooldown and honors a bounded server `Retry-After`. HTTP 401 stops immediately because every model uses the same invalid key. Other API/network failures advance to the next chat model. Empty, reasoning-only and token-truncated replies count as failed attempts.

Use `--timeout 40` before `chat` to limit each HTTP attempt. `--max-tokens 4096` and `--cooldown 10` are chat options. The total duration can exceed one request timeout when multiple models are tried. Known Super/Lightning models disable thinking for this text-work helper; Ultra requests `reasoning_effort: none`. See NVIDIA's [Super example](https://build.nvidia.com/nvidia/nemotron-3.5-super-vl-120b-a12b), [Lightning example](https://build.nvidia.com/nvidia/nemotron-3.5-lightning-30b-a3b) and [Ultra API](https://docs.api.nvidia.com/nim/reference/nvidia-nemotron-3-ultra-550b-a55b-infer). Other model-specific behavior follows the service defaults.

Successful output is JSON with the selected model, answer and reported token usage. A complete temporary file is published atomically without replacing existing output; this requires same-filesystem hard-link support, such as NTFS. Choose a fresh name for each task. Requests go to the configured HTTPS service; selected prompt contents leave the machine. The helper sends neither the `.env` file nor the rest of the repository automatically. It rejects a prompt containing the configured key, does not follow HTTP redirects, and does not print API error bodies or transport exception contents. API replies are saved as text and never executed, applied as patches or used as shell commands. Review and test suggested changes before applying them.

## Embeddings

Model 5 uses a separate operation. NVIDIA documents it as an [embedding endpoint](https://docs.api.nvidia.com/nim/reference/nvidia-nemotron-3-embed-1b-infer), with `input_type` set to `passage` for indexing or `query` for searching:

```powershell
python scripts/nvidia-work.py --env-file ../.env embed --input-file passage.txt --output .local/nvidia/embedding.json --input-type passage
```

Embedding vectors are not code-review or chat answers and do not participate in the four-model fallback.

## Verification

The routing and credential-handling checks are offline and use fixture credentials:

```powershell
python -m unittest discover -s tests -p test_nvidia_work.py -v
```

On 2026-11-16, the configured Super endpoint returned a real review of selected public DesktopPet source (1,315 prompt tokens + 622 completion tokens). Model 5 returned a real embedding (17 reported prompt tokens). Models 2–4 were not individually probed during that check; the 1→2→3→4→1 transition, rate-limit cooldown, cancellation and error paths were exercised with offline simulated failures. Availability and quota depend on the configured NVIDIA account and service at request time.
