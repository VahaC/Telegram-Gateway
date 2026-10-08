import os
import tempfile
import unittest
from pathlib import Path
from unittest import mock
from deliver_digest import WorkflowError, cache_payload, checked_gateway_url, deliver_payload, generate_payload, load_client_config


class WorkflowTests(unittest.TestCase):
    def test_private_config_selects_gateway_credentials(self):
        with tempfile.TemporaryDirectory() as directory, mock.patch.dict(os.environ,
                {"CF_ACCESS_CLIENT_ID": "stale-id", "CF_ACCESS_CLIENT_SECRET": "stale-secret"}, clear=True):
            path = Path(directory) / "client.env"
            path.write_text("GATEWAY_URL=https://gateway.example\nAPI_KEY=" + "a" * 64 + "\n", encoding="utf-8")
            load_client_config(path)
            self.assertEqual("https://gateway.example", os.environ["GATEWAY_URL"])
            self.assertEqual("a" * 64, os.environ["API_KEY"])
            self.assertNotIn("CF_ACCESS_CLIENT_ID", os.environ)
            self.assertNotIn("CF_ACCESS_CLIENT_SECRET", os.environ)

    def test_invalid_private_config_does_not_expose_secrets(self):
        for content in [
            "GATEWAY_URL=http://public.example\nAPI_KEY=" + "b" * 64,
            "GATEWAY_URL=https://gateway.example\nAPI_KEY=private-short-secret",
            "GATEWAY_URL=https://gateway.example\nAPI_KEY=" + "b" * 64 + "\nTELEGRAM_BOT_TOKEN=private-bot-token",
            "GATEWAY_URL=https://gateway.example\nAPI_KEY=" + "b" * 64 + "\nAPI_KEY=duplicate-secret",
            "GATEWAY_URL=https://gateway.example\nAPI_KEY=" + "b" * 64 + "\nCF_ACCESS_CLIENT_ID=private-unpaired-id",
        ]:
            with self.subTest(entry=content.splitlines()[-1].split("=")[0]), tempfile.TemporaryDirectory() as directory:
                path = Path(directory) / "client.env"
                path.write_text(content, encoding="utf-8")
                with self.assertRaises(WorkflowError) as raised:
                    load_client_config(path)
                self.assertNotIn("private-", str(raised.exception))

    def test_submission_polls_until_delivery_without_reposting(self):
        requests = []

        def request(url, headers, body=None):
            requests.append((url, headers, body))
            return {"id": "abc", "status": "Pending" if body else "Delivered", "deliveredMessageCount": 3}

        result = deliver_payload("https://gateway.example", "test-key", {"idempotencyKey": "daily"}, request, lambda _: None)
        self.assertEqual("Delivered", result["status"])
        self.assertEqual(2, len(requests))
        self.assertIsNone(requests[1][2])

    def test_partial_failure_is_not_reported_as_delivery(self):
        with self.assertRaises(WorkflowError):
            deliver_payload("https://gateway.example", "test-key", {"idempotencyKey": "daily"},
                            lambda *args: {"status": "PartiallyDelivered"})

    def test_generation_uses_external_research_and_extracts_content(self):
        calls = []

        def request(url, headers, body, timeout):
            calls.append(body)
            return {"status": "completed", "output": [{"type": "message", "content": [{"type": "output_text", "text": "Український дайджест"}]}]}

        payload = generate_payload("2026-10-08", "test prompt", "test-model", "test-api-key", request)
        self.assertEqual("Український дайджест", payload["content"])
        self.assertEqual("tech-digest-2026-10-08", payload["idempotencyKey"])
        self.assertEqual([{"type": "web_search"}], calls[0]["tools"])
        self.assertFalse(calls[0]["store"])

    def test_incomplete_generation_is_not_submitted(self):
        with self.assertRaises(WorkflowError):
            generate_payload("2026-10-08", "prompt", "model", "key", lambda *args, **kwargs: {"status": "incomplete"})

    def test_payload_cache_preserves_unicode(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "daily.json"
            cache_payload(path, {"content": "Україна 🚀"})
            self.assertIn("Україна", path.read_text(encoding="utf-8"))

    def test_payload_cache_never_replaces_original_submission(self):
        with tempfile.TemporaryDirectory() as directory:
            path = Path(directory) / "daily.json"
            cache_payload(path, {"content": "Original"})
            actual = cache_payload(path, {"content": "Changed"})
            self.assertEqual({"content": "Original"}, actual)

    def test_insecure_or_credential_bearing_gateway_urls_are_rejected(self):
        for url in ["http://public.example", "https://user:secret@example.com", "https://example.com?key=secret", ""]:
            with self.assertRaises(WorkflowError):
                checked_gateway_url(url)
        self.assertEqual("http://127.0.0.1:5080", checked_gateway_url("http://127.0.0.1:5080/"))


if __name__ == "__main__":
    unittest.main()
