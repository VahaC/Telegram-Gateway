#!/usr/bin/env python3
"""Verify Compose lifecycle without real credentials or outbound network access."""
import json
import os
from pathlib import Path
import subprocess
import time
import uuid
import re
import html
from urllib.parse import urlencode, urlparse, parse_qs
import hashlib
import base64

ROOT = Path(__file__).resolve().parent.parent


def main():
    project = "telegram-gateway-smoke-" + uuid.uuid4().hex[:8]
    environment = {**os.environ, "TELEGRAM_BOT_TOKEN": "123456:mock-token-not-real",
                   "TELEGRAM_CHAT_ID": "123456789", "API_KEY": "smoke-key-never-production-0123456789",
                   "GATEWAY_NETWORK": project, "GATEWAY_HOST": "gateway.example.invalid",
                   "TRUSTED_PROXY_IP": "127.0.0.1", "ENABLE_MCP": "true",
                   "ENABLE_MCP_OAUTH": "true", "MCP_PUBLIC_URL": "https://gateway.example.invalid",
                   "MCP_OAUTH_CLIENT_ID": "telegram-gateway", "MCP_OAUTH_REDIRECT_URI": "https://client.example/callback",
                   "OAUTH_CLIENT_SECRET": "smoke-oauth-client-secret-never-production-0123456789"}
    compose = ["docker", "compose", "-p", project, "-f", "docker-compose.yml", "-f", "scripts/docker-compose.smoke.yml"]

    def run(arguments, input_text=None):
        result = subprocess.run(arguments, cwd=ROOT, env=environment, input=input_text,
                                capture_output=True, text=True, encoding="utf-8", timeout=120)
        if result.returncode:
            raise RuntimeError("Container smoke command failed: " + result.stderr)
        return result.stdout.strip()

    def request(path, body=None, authenticated=True):
        arguments = compose + ["exec", "-T", "gateway", "curl", "-sS", "-H", "X-Forwarded-Proto: https", "-w", "\n%{http_code}"]
        if authenticated:
            arguments += ["-H", "X-Api-Key: " + environment["API_KEY"]]
        if body is not None:
            arguments += ["-H", "Content-Type: application/json", "--data-binary", "@-"]
        arguments += ["http://localhost:8080" + path]
        output = run(arguments, None if body is None else json.dumps(body, ensure_ascii=False))
        text, status = output.rsplit("\n", 1)
        return int(status), json.loads(text) if text else None

    def oauth_request(path, form=None, cookies=False):
        arguments = compose + ["exec", "-T", "gateway", "curl", "-sS", "-i", "-H", "X-Forwarded-Proto: https"]
        if cookies:
            arguments += ["-b", "/tmp/oauth-cookies", "-c", "/tmp/oauth-cookies"]
        if form is not None:
            arguments += ["-H", "Content-Type: application/x-www-form-urlencoded", "--data-binary", "@-"]
        arguments += ["http://localhost:8080" + path]
        raw = run(arguments, None if form is None else urlencode(form)).replace("\r\n", "\n")
        head, _, body = raw.partition("\n\n")
        status = int(head.splitlines()[0].split()[1])
        return status, head, body

    try:
        run(compose + ["up", "-d", "--wait", "--wait-timeout", "60"])
        identifier = run(compose + ["ps", "-q", "gateway"])
        configuration = json.loads(run(["docker", "inspect", identifier]))[0]
        assert configuration["Config"]["User"] != "root"
        assert not configuration["HostConfig"]["PortBindings"]
        assert configuration["State"]["Health"]["Status"] == "healthy"
        assert request("/api/ready", authenticated=False)[0] == 200
        metadata_status, metadata = request("/.well-known/oauth-protected-resource", authenticated=False)
        assert metadata_status == 200 and metadata["resource"] == environment["MCP_PUBLIC_URL"] + "/mcp"
        discovery_status, discovery = request("/.well-known/openid-configuration", authenticated=False)
        assert discovery_status == 200 and discovery["authorization_response_iss_parameter_supported"] is True
        assert request("/mcp", {"jsonrpc": "2.0", "id": 1, "method": "tools/list"}, authenticated=False)[0] == 401
        verifier = "smoke-verifier-0123456789-abcdefghijklmnopqrstuvwxyz-0123456789"
        query = {"response_type": "code", "client_id": "telegram-gateway", "redirect_uri": environment["MCP_OAUTH_REDIRECT_URI"],
                 "scope": "telegram:send offline_access", "resource": metadata["resource"], "state": "smoke-state",
                 "code_challenge_method": "S256", "code_challenge": base64.urlsafe_b64encode(hashlib.sha256(verifier.encode()).digest()).decode().rstrip("=")}
        status, _, page = oauth_request("/oauth/authorize?" + urlencode(query), cookies=True)
        assert status == 200
        csrf = html.unescape(re.search(r'name="__RequestVerificationToken" value="([^"]+)"', page)[1])
        status, head, _ = oauth_request("/oauth/authorize", {**query, "__RequestVerificationToken": csrf,
                                      "accessKey": environment["API_KEY"], "action": "allow"}, cookies=True)
        assert status == 302
        redirect = re.search(r"(?im)^location: (.+)$", head)[1].strip()
        parameters = parse_qs(urlparse(redirect).query)
        assert parameters["iss"] == [environment["MCP_PUBLIC_URL"] + "/"]
        status, _, body = oauth_request("/oauth/token", {"grant_type": "authorization_code", "code": parameters["code"][0],
                                      "client_id": "telegram-gateway", "client_secret": environment["OAUTH_CLIENT_SECRET"],
                                      "redirect_uri": environment["MCP_OAUTH_REDIRECT_URI"], "code_verifier": verifier, "resource": metadata["resource"]})
        assert status == 200
        token = json.loads(body)["access_token"]
        def check_bearer():
            raw = run(compose + ["exec", "-T", "gateway", "curl", "-sS", "-w", "\n%{http_code}",
                      "-H", "X-Forwarded-Proto: https", "-H", "Authorization: Bearer " + token,
                      "-H", "Accept: application/json, text/event-stream", "-H", "Content-Type: application/json",
                      "--data-binary", "@-", "http://localhost:8080/mcp"], json.dumps({"jsonrpc": "2.0", "id": 1, "method": "initialize",
                      "params": {"protocolVersion": "2025-03-26", "capabilities": {}, "clientInfo": {"name": "smoke", "version": "1"}}}))
            result, status = raw.rsplit("\n", 1)
            assert status == "200" and '"serverInfo"' in result
        check_bearer()
        payload = {"text": "Контейнерний тест Україна", "idempotencyKey": "container-smoke"}
        accepted_status, accepted = request("/api/messages", payload)
        assert accepted_status == 202
        assert request("/api/deliveries/" + accepted["id"], authenticated=False)[0] == 401
        # Exercise the real HTTP handler before restart: an invalid URI must not hide behind readiness.
        for _ in range(45):
            _, outcome = request("/api/deliveries/" + accepted["id"])
            if outcome["status"] == "Failed":
                break
            time.sleep(1)
        else:
            raise AssertionError("The isolated send did not reach a recorded transport failure.")
        assert outcome["errorCode"] in {"telegram_connection_failed", "telegram_outcome_unknown"}, outcome["errorCode"]
        assert outcome["attempts"] and all(attempt["errorCode"] != "process_interrupted" for attempt in outcome["attempts"])
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
        check_bearer()
        assert request("/api/messages", {**payload, "text": "Changed payload"})[0] == 409
        print("Docker smoke passed: non-root/read-only service, OAuth consent/PKCE/token/MCP, real HTTP handler transport failure, persistent ledger and bearer access after restart.")
        print("Telegram delivery was blocked by the isolated network; no real send was performed.")
    finally:
        run(compose + ["down", "--volumes", "--remove-orphans"])


if __name__ == "__main__":
    main()
