# Validation evidence

Validated on 2026-10-08 in the local Windows workspace using .NET SDK 10.0.400 and Docker
Desktop's Linux/amd64 engine. No real bot token, private chat ID, or OpenAI API key was used.

## Completed checks

| Check | Result |
|---|---|
| `dotnet build -c Release --no-restore` | Passed, 0 warnings and 0 errors |
| `bash run-tests.sh` (Git Bash on Windows) | Passed: 69 tests, 0 failures, 0 skips |
| Core tests | 5 passed: exact credential comparison, case/length/Unicode differences |
| Infrastructure tests | 20 passed: HTTPS destination including token colon, escaping, Markdown/headings/lists/links, Unicode/graphemes, long nested splitting, unsafe input, depth bounds, network and malformed responses |
| API tests | 44 passed: real HTTP auth, shape/rule validation, body limits, rate limiting, host/origin rejection, metadata, idempotency/concurrent duplicates, partial recovery, retry budgets, restart recovery and MCP |
| Python external workflow tests | 7 passed: researcher response parsing, completion checks, private immutable cache, polling, failure status and HTTPS URL validation |
| EF pending-model check | No pending model changes after the two source migrations |
| Top-level type check | 61 handwritten/generated C# source files checked; no multiple public/internal top-level types per file |
| `git diff --check` | Passed; existing CRLF .gitignore has an LF normalization notice |
| `docker build -q -t telegram-gateway:local .` | Passed, multi-stage Linux/amd64 image |
| `python scripts/docker-smoke.py` on final image | Passed: Compose startup/readiness, non-root user, no host ports, API auth, real HTTP handler transport failure without egress, persisted ledger after actual container restart, changed payload rejected |

Final local image at verification:
`sha256:ebb70a2cdab8147484517eda17520b8ec83f407d9e17ae415dd69cfe4f06bf33`.
The unique smoke Compose stack and its test volume were removed after each run.

TRX outputs are generated under each test project's ignored TestResults directory. CI runs the
same suite on Ubuntu and includes a required container check, but CI itself has not run here.
Migrations were generated with the installed EF CLI 10.0.0 against runtime 10.0.12; the CLI
printed its version mismatch notice. Migrations, real SQLite HTTP tests, pending-model check,
and container startup all passed. Use a matching CLI for future migration work.

## Important test boundaries

All Telegram calls in .NET tests go through a recording fake primary HttpMessageHandler.
Routing, validation, authentication, serialization, EF Core and SQLite are real. The official
MCP SDK client discovers and calls the secured HTTP endpoint, then the actual application
worker uses the fake Telegram adapter's HTTP response. These prove implementation behavior,
not actual Telegram delivery or actual client account registration.

Container smoke uses fake credentials and an internal Docker network with no Telegram egress.
It now exercises the production HTTP handler until a transport failure is recorded, rejecting
`process_interrupted` as a false pass. It verifies container lifecycle and persistence, not
message acceptance by Telegram.
`FloodControlHttpTests` stops and restarts the application over the same actual SQLite path,
then proves the persisted 429 deadline blocks another delivery until the injected clock advances.

No UI was built or requested, so browser layout validation is not applicable. The Portainer
guide is documentation; no real Portainer instance was accessed. Nothing was committed,
pushed, tagged, published to a registry, or scheduled on the user's server.

## Setup-send defect reproduction

The user-supplied server status showed `Failed`, `requiresReview: true` and `process_interrupted`
after a healthy container accepted a setup test. The worker log reported an unexpected exception
without its type. Local reproduction confirmed that `bot123456:test-token-not-real/sendMessage`
is an absolute URI with scheme `bot123456`; a real HttpClient throws NotSupportedException
before sending. The existing fake handler accepted that invalid URI.

Two outbound-URI regression cases failed against the original adapter, then passed after adding
`./` to the relative path. The full 69-test suite and the strengthened Linux container smoke
passed. Unexpected worker failures now log the exception type without message/URL disclosure.
The corrected image still needs deployment and an actual Telegram success check; existing
uncertain rows were not reset or deleted.

## Installation acceptance still required

1. Configure real BotFather credentials, start the private chat, and verify its positive ID.
2. Send the setup message and confirm both Delivered metadata and the actual private message.
3. Send a long Ukrainian digest and inspect every part, formatting and clickable source URL.
4. Repeat the same key/payload and verify no new messages; restart and verify recorded IDs.
5. Deploy through the actual Portainer and HTTPS proxy/tunnel installation.
6. Configure the external researcher API/model, review personalization, install the timer only
   at the selected local time, and demonstrate one actual scheduled AI-to-Telegram run.
7. If direct ChatGPT integration is required, implement compatible auth, register the app and
   validate its permitted write tools in the target account. Scheduled-task support is not
   inferred from the interactive MCP endpoint.

The production installation and automatic ChatGPT delivery are therefore not claimed complete.
