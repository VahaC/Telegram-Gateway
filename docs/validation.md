# Validation evidence

Validated on 2026-10-08 in the local Windows workspace using .NET SDK 10.0.400 and Docker
Desktop's Linux/amd64 engine. Automated checks used fake external-service credentials.

## Completed checks

| Check | Result |
|---|---|
| `dotnet build -c Release --no-restore` | Passed, 0 warnings and 0 errors |
| `bash run-tests.sh` (Git Bash on Windows) | Passed: 69 tests, 0 failures, 0 skips |
| Core tests | 5 passed: exact credential comparison, case/length/Unicode differences |
| Infrastructure tests | 20 passed: HTTPS destination including token colon, escaping, Markdown/headings/lists/links, Unicode/graphemes, long nested splitting, unsafe input, depth bounds, network and malformed responses |
| API tests | 44 passed: real HTTP auth, shape/rule validation, body limits, rate limiting, host/origin rejection, metadata, idempotency/concurrent duplicates, partial recovery, retry budgets, restart recovery and MCP |
| Python external workflow tests | 9 passed: private client-file selection and secret-safe rejection, researcher response parsing, completion checks, private immutable cache, polling, failure status and HTTPS URL validation |
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

The gateway has no browser UI, so browser layout validation is not applicable. Deployment
guides describe installation procedures; automated checks do not verify a particular live
proxy, management interface or caller configuration.

## Setup-send defect reproduction

The setup-send defect was reproduced locally: `bot123456:test-token-not-real/sendMessage`
is an absolute URI with scheme `bot123456`; a real HttpClient throws NotSupportedException
before sending. The existing fake handler accepted that invalid URI.

Two outbound-URI regression cases failed against the original adapter, then passed after adding
`./` to the relative path. The full 69-test suite and the strengthened Linux container smoke
passed. Unexpected worker failures now log the exception type without message/URL disclosure.
Installation verification must include an actual Telegram success check. The correction
preserves existing uncertain rows and does not automatically replay them.

## Installation verification

Each installation should verify:

1. The configured bot can send to the intended private chat.
2. Submission produces both Delivered metadata and the actual Telegram message.
3. Long content retains Unicode, formatting and clickable links across all message parts.
4. Repeating an identical key/payload produces no new messages; recorded IDs survive restart.
5. HTTPS, proxy routing and authentication work from the calling system's network.
6. An MCP client can discover and use the enabled tools with the required authentication.
7. Any caller-controlled event trigger or schedule submits content at the intended time.

Automated checks establish implementation behavior within the boundaries above. Live Telegram
delivery, client compatibility and deployment acceptance require installation-specific checks.
