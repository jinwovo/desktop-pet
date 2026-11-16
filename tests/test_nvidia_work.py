"""Offline protocol/routing checks: no real credentials or network requests."""
import importlib.util
import http.client
import io
import json
from pathlib import Path
import sys
import tempfile
import unittest
from unittest.mock import patch
from urllib.error import HTTPError, URLError

spec = importlib.util.spec_from_file_location("nvidia_work", Path(__file__).resolve().parents[1] / "scripts/nvidia-work.py")
nim = importlib.util.module_from_spec(spec)
sys.modules[spec.name] = nim
spec.loader.exec_module(nim)

KEY = "fixture-secret-not-a-real-key"
CONFIG = nim.Config("https://integrate.api.nvidia.com/v1", KEY, ("one", "two", "three", "four"), "embed-five")


class Response(io.BytesIO):
    status = 200


class Opener:
    def __init__(self, result=None, failure=None):
        self.result = result or {"choices": [{"message": {"content": "Reviewed"}, "finish_reason": "stop"}]}
        self.failure = failure
        self.requests = []

    def open(self, req, timeout):
        self.requests.append(req)
        if self.failure:
            raise self.failure
        return Response(json.dumps(self.result).encode())


class SequenceClient:
    config = CONFIG

    def __init__(self, outcomes):
        self.outcomes = iter(outcomes)
        self.calls = []

    def chat(self, model, prompt, max_tokens):
        self.calls.append(model)
        result = next(self.outcomes)
        if isinstance(result, Exception):
            raise result
        return {"model": model, "content": result}


class NvidiaWorkTests(unittest.TestCase):
    def test_fallback_wraps_four_to_one_and_stops_at_success(self):
        client = SequenceClient([nim.AttemptFailed("HTTP 503") for _ in range(4)] + ["ok"])
        waits, logs = [], []
        result = nim.chat_with_fallback(client, "public source", rounds=2, cooldown=2, sleep=waits.append, log=logs.append)
        self.assertEqual(client.calls, ["one", "two", "three", "four", "one"])
        self.assertEqual(result["model"], "one")
        self.assertEqual(waits, [2])
        self.assertNotIn(KEY, "\n".join(logs))

    def test_success_on_three_does_not_call_four_or_embeddings(self):
        client = SequenceClient([nim.AttemptFailed("timeout"), nim.AttemptFailed("HTTP 404"), "ok"])
        self.assertEqual(nim.chat_with_fallback(client, "code", rounds=1, log=lambda _: None)["model"], "three")
        self.assertEqual(client.calls, ["one", "two", "three"])

    def test_all_failures_are_bounded_when_rounds_requested(self):
        client = SequenceClient([nim.AttemptFailed("HTTP 429") for _ in range(8)])
        waits = []
        with self.assertRaises(nim.AttemptFailed):
            nim.chat_with_fallback(client, "code", rounds=2, cooldown=1, sleep=waits.append, log=lambda _: None)
        self.assertEqual(client.calls, list(CONFIG.chat_models) * 2)
        self.assertEqual(waits, [1])

    def test_unlimited_mode_can_be_cancelled_during_cooldown(self):
        client = SequenceClient([nim.AttemptFailed("HTTP 503") for _ in range(4)])
        with self.assertRaises(KeyboardInterrupt):
            nim.chat_with_fallback(client, "code", rounds=0, sleep=lambda _: (_ for _ in ()).throw(KeyboardInterrupt()), log=lambda _: None)
        self.assertEqual(len(client.calls), 4)

    def test_rate_limit_retry_after_is_respected_between_rounds(self):
        client = SequenceClient([nim.AttemptFailed("HTTP 429", retry_after=30) for _ in range(4)] + ["ok"])
        waits = []
        nim.chat_with_fallback(client, "code", rounds=2, cooldown=1, sleep=waits.append, log=lambda _: None)
        self.assertEqual(waits, [30])

    def test_invalid_shared_key_stops_without_repeating(self):
        client = SequenceClient([nim.AttemptFailed("HTTP 401", fatal=True)])
        with self.assertRaises(nim.AttemptFailed):
            nim.chat_with_fallback(client, "code", rounds=0, log=lambda _: None)
        self.assertEqual(client.calls, ["one"])

    def test_key_in_prompt_is_rejected_before_network(self):
        client = SequenceClient([])
        with self.assertRaises(ValueError):
            nim.chat_with_fallback(client, "accidental " + KEY, log=lambda _: None)
        self.assertEqual(client.calls, [])

    def test_env_parser_ignores_other_secrets_and_preserves_literal_values(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / ".env"
            path.write_text('\ufeffexport NVIDIA_API_KEY="literal#value" # comment\nUNRELATED_SECRET=do-not-read\nNVIDIA_NIM_MODEL_1=one # note\n', encoding="utf-8")
            self.assertEqual(nim.read_env(path), {"NVIDIA_API_KEY": "literal#value", "NVIDIA_NIM_MODEL_1": "one"})

    def test_process_environment_overrides_file_and_config_repr_hides_key(self):
        env = {"NVIDIA_API_KEY": KEY, **{f"NVIDIA_NIM_MODEL_{i}": name for i, name in enumerate(CONFIG.chat_models, 1)}}
        with patch.dict("os.environ", env, clear=True):
            config = nim.load_config(None)
        self.assertNotIn(KEY, repr(config))
        self.assertEqual(config.chat_models, CONFIG.chat_models)

    def test_insecure_or_credential_bearing_base_urls_rejected(self):
        env = {"NVIDIA_API_KEY": KEY, **{f"NVIDIA_NIM_MODEL_{i}": name for i, name in enumerate(CONFIG.chat_models, 1)}}
        for base in ("http://example.test/v1", "https://user:pass@example.test/v1", "https://example.test/v1?key=secret"):
            with self.subTest(base=base), patch.dict("os.environ", env | {"NVIDIA_BASE_URL": base}, clear=True), self.assertRaises(ValueError):
                nim.load_config(None)

    def test_empty_reasoning_only_or_truncated_completions_do_not_succeed(self):
        for message, finish in (({"content": ""}, "stop"), ({"reasoning_content": "private reasoning"}, "length"), ({"content": "partial answer"}, "length")):
            client = nim.Client(CONFIG, opener=Opener({"choices": [{"message": message, "finish_reason": finish}]}))
            with self.subTest(message=message), self.assertRaises(nim.AttemptFailed):
                client.chat("one", "code", 128)

    def test_chat_payload_uses_known_model_specific_reasoning_options(self):
        for model, expected in (("nvidia/nemotron-3.5-super-vl-120b-a12b", "chat_template_kwargs"), ("nvidia/nemotron-3-ultra-550b-a55b", "reasoning_effort")):
            opener = Opener()
            nim.Client(CONFIG, opener=opener).chat(model, "code", 256)
            payload = json.loads(opener.requests[0].data)
            self.assertIn(expected, payload)
            self.assertFalse(payload["stream"])
            self.assertNotIn(KEY, opener.requests[0].full_url)

    def test_error_body_and_transport_exception_are_not_logged(self):
        for failure in (HTTPError(CONFIG.base_url, 429, KEY, {"Retry-After": "12"}, io.BytesIO(KEY.encode())), URLError(KEY)):
            client = nim.Client(CONFIG, opener=Opener(failure=failure))
            with self.assertRaises(nim.AttemptFailed) as caught:
                client.chat("one", "code", 128)
            self.assertNotIn(KEY, str(caught.exception))

    def test_redirects_are_not_followed(self):
        self.assertIsNone(nim.NoRedirect().redirect_request(None, None, 302, "Found", {}, "https://other.test"))

    def test_broken_http_protocol_does_not_expose_raw_error(self):
        client = nim.Client(CONFIG, opener=Opener(failure=http.client.BadStatusLine(KEY)))
        with self.assertRaises(nim.AttemptFailed) as caught:
            client.chat("one", "code", 128)
        self.assertEqual(str(caught.exception), "network or timeout")

    def test_existing_output_is_preserved_without_any_api_call(self):
        env = {"NVIDIA_API_KEY": KEY, **{f"NVIDIA_NIM_MODEL_{i}": name for i, name in enumerate(CONFIG.chat_models, 1)}}
        with tempfile.TemporaryDirectory() as folder:
            output = Path(folder) / "review.json"
            output.write_text("keep", encoding="utf-8")
            with patch.dict("os.environ", env, clear=True), patch.object(nim.Client, "post") as post, patch("sys.stderr", new_callable=io.StringIO):
                self.assertEqual(nim.main(["chat", "--prompt-file", "unused", "--output", str(output)]), 1)
                post.assert_not_called()
            self.assertEqual(output.read_text(), "keep")

    def test_embedding_values_are_nonempty_finite_numbers(self):
        nim.validate_embedding({"data": [{"embedding": [0, -0.5, 1.2]}]})
        for vector in ([], [None], [True], ["1"], [float("nan")], [float("inf")], [10 ** 400]):
            with self.subTest(vector_type=str(type(vector[0])) if vector else "empty"), self.assertRaises(nim.AttemptFailed):
                nim.validate_embedding({"data": [{"embedding": vector}]})

    def test_output_publication_is_complete_and_does_not_overwrite(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "output.json"
            nim.save_output(path, '{"content":"한글"}')
            self.assertEqual(json.loads(path.read_text(encoding="utf-8")), {"content": "한글"})
            with self.assertRaises(FileExistsError):
                nim.save_output(path, "replace")
            self.assertEqual(json.loads(path.read_text(encoding="utf-8")), {"content": "한글"})
            self.assertEqual(list(Path(folder).glob(".nvidia-work-*.tmp")), [])

    def test_failed_output_publication_leaves_no_partial_result_or_temp_file(self):
        with tempfile.TemporaryDirectory() as folder:
            path = Path(folder) / "output.json"
            with patch.object(nim.os, "link", side_effect=OSError("fixture failure")), self.assertRaises(OSError):
                nim.save_output(path, "complete")
            self.assertFalse(path.exists())
            self.assertEqual(list(Path(folder).iterdir()), [])


if __name__ == "__main__":
    unittest.main()
