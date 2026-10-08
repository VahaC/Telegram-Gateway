import tempfile
import unittest
from pathlib import Path
from deliver_digest import WorkflowError, cache_payload, checked_gateway_url, deliver_payload, generate_payload


class WorkflowTests(unittest.TestCase):
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
