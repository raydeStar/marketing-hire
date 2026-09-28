# Reuse the qualified application and apply only the small reporting compatibility overlay.
ARG HIREZERO_BASE
FROM ${HIREZERO_BASE}
USER root
COPY index-client.mjs index-reporter.mjs entrance.mjs /opt/hirezero/
RUN node --input-type=module -e "import {readFile,writeFile} from 'node:fs/promises'; import {adaptIndexCollectorExecution} from '/opt/hirezero/index-client.mjs'; import {adaptIndexReporter} from '/opt/hirezero/index-reporter.mjs'; for (const [path,adapt] of [['/opt/plow/agent-index-client.py',adaptIndexCollectorExecution],['/opt/plow/boot/agent-index.js',adaptIndexReporter]]) await writeFile(path,adapt(await readFile(path,'utf8')));"
USER node
ARG HIREZERO_REVISION
LABEL org.opencontainers.image.source="https://github.com/raydeStar/marketing-hire" \
      org.opencontainers.image.title="HireZero · Marketing Lead" \
      org.opencontainers.image.description="A marketing employee and review cockpit powered by OpenClaw on Plow." \
      org.opencontainers.image.revision="${HIREZERO_REVISION}"
ENV AGENT_ID="hirezero-marketing"
