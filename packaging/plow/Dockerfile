FROM mcr.microsoft.com/dotnet/aspnet:10.0@sha256:a4556ed033fa96f984bb7a8d348851cb2d36b1281dd2420070045f664fbb5f94 AS dotnet
FROM public.ecr.aws/e1h7x4a2/plow-cloud-agents:base-1e73c82c4b3e0c9f76935bc0cc45061875b34aee@sha256:5f8ef7c3762b037420cd8843a767a7ab7e2433b1c8319e7cfe2ad1bdef5dee8a
USER root
RUN apt-get update \
 && apt-get install -y --no-install-recommends libicu72 ffmpeg fonts-dejavu-core python3-venv libzstd1 \
 && rm -rf /var/lib/apt/lists/*
COPY --from=dotnet /usr/share/dotnet /usr/share/dotnet
RUN ln -s /usr/share/dotnet/dotnet /usr/local/bin/dotnet
ENV DOTNET_ROOT=/usr/share/dotnet \
    AGENT_ID="" \
    AGENT_NAME="HireZero · Marketing Lead" \
    AGENT_BLURB="The marketing hire that wrote its own listing. Evidence-backed campaigns, shipped only with your approval." \
    HIRE_STATE=/var/lib/plow/hire
COPY host/ /opt/hirezero/host/
COPY package/ /opt/hirezero/
ADD --checksum=sha256:c93dd6535125c0eb0d5eeef8807976b3e29748f02852cd9ff891c02812ee8f5c https://codeload.github.com/VladUZH/harken/tar.gz/d0710a427dbbe712594ef3a6c25112e1d14cc027 /tmp/harken.tgz
RUN python3 -m venv /opt/hirezero/pulse-env \
 && /opt/hirezero/pulse-env/bin/pip install --no-cache-dir -r /opt/hirezero/harken-requirements.lock \
 && mkdir -p /opt/hirezero/pulse-src /opt/hirezero/notices \
 && tar -xzf /tmp/harken.tgz -C /tmp \
 && cp -R /tmp/harken-d0710a427dbbe712594ef3a6c25112e1d14cc027/src/harken /opt/hirezero/pulse-src/ \
 && cp /tmp/harken-d0710a427dbbe712594ef3a6c25112e1d14cc027/LICENSE /opt/hirezero/notices/Harken-LICENSE \
 && rm -rf /tmp/harken.tgz /tmp/harken-d0710a427dbbe712594ef3a6c25112e1d14cc027
COPY hire/ /opt/hire/bin/
COPY meter/ /app/marketing-meter/
COPY prompt/ /opt/plow/prompt/
COPY skills/ /opt/plow/skills/
RUN printf '#!/bin/sh\nexec python3 /opt/hire/bin/hire.py "$@"\n' > /usr/local/bin/hire \
 && printf '#!/bin/sh\nPYTHONPATH=/opt/hirezero/pulse-src exec /opt/hirezero/pulse-env/bin/python /opt/hire/bin/pulse.py "$@"\n' > /usr/local/bin/pulse \
 && chmod 755 /usr/local/bin/hire /usr/local/bin/pulse \
 && chmod -R go-w /opt/hirezero /opt/hire /app/marketing-meter \
 && node /opt/hirezero/extend-base.mjs
WORKDIR /opt/hirezero/host
USER node
CMD ["node", "/opt/hirezero/boot.mjs"]
