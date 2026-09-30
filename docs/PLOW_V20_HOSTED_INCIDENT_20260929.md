# Plow hosted web failure: inspection requested

**Update:** the owner authorized retiring this failed installation; it has been
deleted. Its image and local incident evidence remain available. Signup and
one-click installation now use [v19](PLOW_V19_ROUTING_20260929.md). The following
handoff describes the original failure before retirement.

Copy-ready platform handoff:

> HireZero's v20 installation initially opened, then lost web access during
> website onboarding. Please inspect runtime/container logs, exit/restart/OOM
> events, and web ingress health around **2026-09-30 01:52–01:58 UTC**.
>
> Agent Index ID: **hirezero-marketing**
>
> Installation: **d99bae1c68fa9d54136cdbed33e4db1d**
>
> Image: **ghcr.io/raydestar/hirezero-marketing@sha256:480ddc50a25efd81af130530999c741034e235a1ba9d223f5babf27c6a04388e**
>
> Source: **1aba701** in https://github.com/raydeStar/marketing-hire
>
> The cockpit returned 502 for both onboarding and state refresh.
> GET /v1/agents still says running, while POST
> /v1/agents/d99bae1c68fa9d54136cdbed33e4db1d/web returns
> **409 NO_WEB_PAGE**. Older installations still return working web launches.
> This installation was subsequently retired; v20 has not been promoted.
>
> The preceding v19 passed real cockpit chat and an owner-confirmed SMS reply.
> v20 only adds an early HTTP acknowledgement and polling for the saved brief;
> its Plow base, channel, GLM route and metering code are unchanged.

The exposed owner API has no runtime-log or restart operation. The current
evidence establishes an unavailable web service, not its root cause.

Local receipts and the screenshot are in the ignored
`artifacts/plow-onboarding-wait-deploy-20260929/` directory. No credentials are
included in this handoff. No Discord message has been sent automatically.
