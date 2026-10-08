# Packaging and publishing

No release number or registry namespace has been selected. Builds use the development fallback
0.0.0-dev; no version bump, tag, commit, push or registry publication is performed automatically.

Build locally with `docker build -t telegram-gateway:local .`. On a remote Portainer host,
either build there or privately transfer a local image using `docker save` / `docker load`.
The compose stack expects that local image on its Docker endpoint.

Before a release, choose an explicit version and registry owner, complete the real acceptance
checks in docs/validation.md, and review the changelog. Only then tag/push an image with those
chosen values and update Compose's image reference. CI currently validates/builds and does not
publish images. Back up the data volume before applying future schema migrations.
