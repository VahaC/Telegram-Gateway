#!/usr/bin/env python3
"""Verify Compose lifecycle without real credentials or outbound network access."""
import json
import os
from pathlib import Path
import subprocess
import time
import uuid

ROOT = Path(__file__).resolve().parent.parent


def main():
    project = "telegram-gateway-smoke-" + uuid.uuid4().hex[:8]
    environment = {**os.environ, "TELEGRAM_BOT_TOKEN": "123456:mock-token-not-real",
                   "TELEGRAM_CHAT_ID": "123456789", "API_KEY": "smoke-key-never-production-0123456789",
                   "GATEWAY_NETWORK": project, "GATEWAY_HOST": "gateway.example.invalid",
                   "TRUSTED_PROXY_IP": "", "ENABLE_MCP": "false"}
    compose = ["docker", "compose", "-p", project, "-f", "docker-compose.yml", "-f", "scripts/docker-compose.smoke.yml"]

    def run(arguments, input_text=None):
        result = subprocess.run(arguments, cwd=ROOT, env=environment, input=input_text,
                                capture_output=True, text=True, encoding="utf-8", timeout=120)
        if result.returncode:
            raise RuntimeError("Container smoke command failed: " + result.stderr)
        return result.stdout.strip()

    def request(path, body=None, authenticated=True):
        arguments = compose + ["exec", "-T", "gateway", "curl", "-sS", "-w", "\n%{http_code}"]
        if authenticated:
            arguments += ["-H", "X-Api-Key: " + environment["API_KEY"]]
        if body is not None:
            arguments += ["-H", "Content-Type: application/json", "--data-binary", "@-"]
        arguments += ["http://localhost:8080" + path]
        output = run(arguments, None if body is None else json.dumps(body, ensure_ascii=False))
        text, status = output.rsplit("\n", 1)
        return int(status), json.loads(text) if text else None

    try:
        run(compose + ["up", "-d", "--wait", "--wait-timeout", "60"])
        identifier = run(compose + ["ps", "-q", "gateway"])
        configuration = json.loads(run(["docker", "inspect", identifier]))[0]
        assert configuration["Config"]["User"] != "root"
        assert not configuration["HostConfig"]["PortBindings"]
        assert configuration["State"]["Health"]["Status"] == "healthy"
        assert request("/api/ready", authenticated=False)[0] == 200
        payload = {"text": "Контейнерний тест Україна", "idempotencyKey": "container-smoke"}
        accepted_status, accepted = request("/api/messages", payload)
        assert accepted_status == 202
        assert request("/api/deliveries/" + accepted["id"], authenticated=False)[0] == 401
        # Recreate the process/container on the same volume. No outbound sends are possible.
        run(compose + ["restart", "gateway"])
        for _ in range(30):
            try:
                if request("/api/ready", authenticated=False)[0] == 200:
                    break
            except RuntimeError:
                pass
            time.sleep(1)
        status, persisted = request("/api/deliveries/" + accepted["id"])
        assert status == 200 and persisted["id"] == accepted["id"]
        assert persisted["idempotencyKey"] == "container-smoke"
        assert request("/api/messages", {**payload, "text": "Changed payload"})[0] == 409
        print("Docker smoke passed: healthy non-root Compose service, no host ports, authentication, persistent ledger after container restart.")
        print("Telegram delivery was blocked by the isolated network; no real send was performed.")
    finally:
        run(compose + ["down", "--volumes", "--remove-orphans"])


if __name__ == "__main__":
    main()
