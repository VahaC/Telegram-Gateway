# Roadmap

The local implementation and mocked delivery checks are available. Production acceptance is
pending the installation-specific checks below; no release version has been selected.

- Configure the real private bot/chat and verify a message plus a long Ukrainian digest.
- Deploy through the actual Portainer and HTTPS proxy/tunnel installation.
- Verify a scheduled external researcher run through to the actual Telegram chat.
- If direct ChatGPT integration is selected, implement supported OAuth/transport requirements,
  register the custom app and verify permitted write tools in the target account.

No Redis/broker, multi-user UI, arbitrary chat destinations or ambiguous-send override is planned.
