# Apply public listing metadata only after qualifying the immutable application.
# The private candidate keeps its reporter asleep until the curtain rises.
ARG HIREZERO_BASE
FROM ${HIREZERO_BASE}
ARG HIREZERO_REVISION
LABEL org.opencontainers.image.source="https://github.com/raydeStar/marketing-hire" \
      org.opencontainers.image.title="HireZero · Marketing Lead" \
      org.opencontainers.image.description="A marketing employee and review cockpit powered by OpenClaw on Plow." \
      org.opencontainers.image.revision="${HIREZERO_REVISION}"
ENV AGENT_ID="hirezero-marketing"
