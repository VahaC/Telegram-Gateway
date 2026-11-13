# 2. Optional single-owner OAuth for MCP

Status: accepted

## Context

Some remote MCP clients support OAuth rather than custom API-key headers. The gateway targets
one private Telegram chat and should not require a separate identity-provider deployment.

## Decision

Use [OpenIddict](https://documentation.openiddict.com/) for authorization-code and refresh-token
handling. Register one confidential client with exact HTTPS callbacks. Require PKCE S256,
HTTPS and explicit owner consent using the existing gateway API key. The key is entered only
in the gateway authorization form and is not given to the OAuth client. Anonymous notifications,
dynamic registration, implicit grants and client-credentials grants are not exposed.

Protected-resource metadata identifies `/mcp`. Discovery and authorization responses use the
same issuer with RFC 9207 issuer identification. Tokens are signed and encrypted, restricted
to the MCP resource, `telegram:send` scope and gateway owner. Validate issuer, audience,
lifetime, scope and token status on every call. Rotating API_KEY invalidates existing owner
tokens through a credential-version claim.

Store records separately in `oauth.db`, alongside the delivery database. Keep signing/encryption
material and encrypted antiforgery keys under the persistent data directory with restricted
permissions. Backups must include these files. OAuth is off by default and requires an explicit
public origin, client secret and callbacks.

The consent form's Content Security Policy permits same-origin submission and the requested,
validated callback path. Browsers enforce `form-action` on the POST's redirect to the client,
so a same-origin-only policy would prevent OAuth completion. Script execution remains disabled.

## Consequences

HTTP clients retain X-Api-Key authentication. MCP accepts either an API key or, when enabled,
an OAuth bearer token. HTTP container deployments require a trusted HTTPS reverse proxy.
Operators supply the preconfigured client ID and secret. Authorization does not provide
content research, event triggers or scheduling.

## Update: two scopes and several clients (AgentGateway)

The gateway gained a news module, used by a Claude cloud routine through a claude.ai custom
connector, while ChatGPT keeps sending Telegram messages. Both are OAuth clients of the same
owner, but they need different powers.

- Scopes: `telegram:send` (Telegram tools) and `news:publish` (news tools). Each MCP tool call is
  checked against its scope; tools a token may not call are also hidden from its tool list. The
  API key is the owner's credential and grants both.
- Clients: `McpOAuth:Clients:{n}:ClientId|ClientSecret|RedirectUris|Scopes`. The original keys
  (`McpOAuth:ClientId`, `OAUTH_CLIENT_SECRET`, `McpOAuth:RedirectUris`) remain client 0 with scope
  `telegram:send`, so the ChatGPT registration, its stored tokens and its client ID are unchanged.
  A client removed from configuration is deleted from `oauth.db` with its tokens at startup.
- Discovery advertises both scopes. A client may ask for any of them; the authorization endpoint
  grants only the requested scopes that the client is configured for (RFC 6749 §3.3 permits a
  narrower grant), and the token exchange re-applies that limit, so a refresh cannot regain a
  scope removed from the client's configuration.
