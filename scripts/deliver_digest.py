#!/usr/bin/env python3
"""External workflow: submit an existing digest, or invoke an external AI API then submit.

The gateway itself never researches news. No third-party Python dependencies are required.
Generated payloads are cached privately before submission so a retry sends identical content.
"""
import argparse
import datetime
import json
import os
from pathlib import Path
import sys
import time
import uuid
import urllib.error
import urllib.parse
import urllib.request
from zoneinfo import ZoneInfo


class WorkflowError(Exception):
    """An expected failure whose message is safe to print."""


class NoRedirect(urllib.request.HTTPRedirectHandler):
    def redirect_request(self, request, file_pointer, code, message, headers, new_url):
        return None  # Never forward an API credential to another origin.


def json_request(url, headers, body=None, timeout=60):
    request = urllib.request.Request(
        url, data=None if body is None else json.dumps(body, ensure_ascii=False).encode("utf-8"),
        headers={"Content-Type": "application/json", **headers}, method="GET" if body is None else "POST")
    try:
        with urllib.request.build_opener(NoRedirect).open(request, timeout=timeout) as response:
            return json.load(response)
    except urllib.error.HTTPError as error:
        # Remote error bodies can contain private prompts/content; never print them.
        raise WorkflowError(f"Remote request failed with HTTP {error.code}.") from None
    except (urllib.error.URLError, TimeoutError, json.JSONDecodeError):
        raise WorkflowError("Remote request failed or returned invalid JSON. Retry the same cached payload.") from None


def checked_gateway_url(value):
    parsed = urllib.parse.urlsplit(value)
    if parsed.username or parsed.password or parsed.query or parsed.fragment:
        raise WorkflowError("GATEWAY_URL must not contain credentials, a query, or a fragment.")
    if parsed.scheme != "https" and not (parsed.scheme == "http" and parsed.hostname in {"localhost", "127.0.0.1", "::1"}):
        raise WorkflowError("Use HTTPS, or loopback HTTP for local testing.")
    return value.rstrip("/")


def generate_payload(day, prompt, model, key, request=json_request):
    response = request("https://api.openai.com/v1/responses", {"Authorization": f"Bearer {key}"}, {
        "model": model, "store": False, "tools": [{"type": "web_search"}],
        "input": f"Today in Europe/Kyiv is {day}.\n{prompt}\nReturn the digest as Ukrainian Markdown, with clickable source URLs. Do not invent news or sources. Clearly label uncertainty."
    }, timeout=240)
    if response.get("status") != "completed":
        raise WorkflowError("AI research did not complete. No digest was submitted.")
    text = "\n".join(item["text"] for output in response.get("output", []) if output.get("type") == "message"
                     for item in output.get("content", []) if item.get("type") == "output_text")
    if not text.strip():
        raise WorkflowError("AI response had no digest content.")
    return {"title": "Технологічний дайджест", "date": day, "language": "uk", "content": text,
            "format": "markdown", "idempotencyKey": f"tech-digest-{day}"}


def cache_payload(path, payload):
    path.parent.mkdir(parents=True, exist_ok=True, mode=0o700)
    if os.name != "nt":
        path.parent.chmod(0o700)
    temporary = path.with_suffix(f".{uuid.uuid4().hex}.tmp")
    descriptor = os.open(temporary, os.O_WRONLY | os.O_CREAT | os.O_EXCL, 0o600)
    with os.fdopen(descriptor, "w", encoding="utf-8") as output:
        json.dump(payload, output, ensure_ascii=False)
    try:
        # Link creation is exclusive: a concurrent runner cannot replace the first cached payload.
        os.link(temporary, path)
    except FileExistsError:
        pass
    finally:
        temporary.unlink()
    return json.loads(path.read_text(encoding="utf-8"))


def deliver_payload(base_url, api_key, payload, request=json_request, pause=time.sleep, timeout=600):
    if not payload.get("idempotencyKey"):
        raise WorkflowError("The workflow requires an explicit idempotencyKey.")
    headers = {"X-Api-Key": api_key}
    access_id, access_secret = os.environ.get("CF_ACCESS_CLIENT_ID", ""), os.environ.get("CF_ACCESS_CLIENT_SECRET", "")
    if bool(access_id) != bool(access_secret):
        raise WorkflowError("Cloudflare Access requires both service-token environment variables.")
    if access_id:
        headers.update({"CF-Access-Client-Id": access_id, "CF-Access-Client-Secret": access_secret})
    delivery = request(base_url + "/api/digests", headers, payload)
    deadline = time.monotonic() + timeout
    while delivery.get("status") in {"Pending", "Sending"}:
        if time.monotonic() >= deadline:
            raise WorkflowError("Delivery is still pending. Check status before retrying the same payload.")
        pause(3)
        delivery = request(base_url + "/api/deliveries/" + str(delivery["id"]), headers)
    if delivery.get("status") != "Delivered":
        raise WorkflowError("Delivery did not complete. Inspect authenticated delivery metadata; uncertain outcomes require review.")
    return delivery


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    source = parser.add_mutually_exclusive_group(required=True)
    source.add_argument("--input", type=Path, help="Existing digest JSON file from any external assistant")
    source.add_argument("--generate", action="store_true", help="Use a separately configured OpenAI Responses API")
    parser.add_argument("--prompt", type=Path, help="Personal research instructions; required with --generate")
    parser.add_argument("--state-dir", type=Path, default=Path("Data/workflow"))
    parser.add_argument("--date", help="Digest date, default today in Europe/Kyiv")
    arguments = parser.parse_args()
    base_url = checked_gateway_url(os.environ.get("GATEWAY_URL", ""))
    api_key = os.environ.get("API_KEY", "")
    if not api_key:
        raise WorkflowError("Set API_KEY for the external workflow.")
    if arguments.input:
        payload = json.loads(arguments.input.read_text(encoding="utf-8"))
    else:
        day = arguments.date or datetime.datetime.now(ZoneInfo("Europe/Kyiv")).date().isoformat()
        datetime.date.fromisoformat(day)
        cache = arguments.state_dir / f"{day}.json"
        if cache.exists():
            payload = json.loads(cache.read_text(encoding="utf-8"))
        else:
            model, key = os.environ.get("OPENAI_MODEL", ""), os.environ.get("OPENAI_API_KEY", "")
            if not model or not key or arguments.prompt is None:
                raise WorkflowError("Generation requires OPENAI_MODEL, OPENAI_API_KEY, and --prompt. API billing is separate from ChatGPT.")
            payload = generate_payload(day, arguments.prompt.read_text(encoding="utf-8"), model, key)
            payload = cache_payload(cache, payload)
    result = deliver_payload(base_url, api_key, payload)
    print(f"Delivered {result['deliveredMessageCount']} parts. Delivery ID: {result['id']}")


if __name__ == "__main__":
    try:
        main()
    except WorkflowError as error:
        print(str(error), file=sys.stderr)
        sys.exit(1)
    except (OSError, ValueError, KeyError):
        # Never include arbitrary OSError/ValueError text: it can disclose paths, URLs or private data.
        print("Workflow failed. Check configuration, cached digest, and authenticated delivery status locally.", file=sys.stderr)
        sys.exit(1)
