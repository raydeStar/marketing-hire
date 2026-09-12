# Release manifests include amd64 and arm64. Host qualification is recorded separately.
FROM openclaw/openclaw@sha256:cc596b846506a5f4cfcee111394a2725f375f01cca2ebb492a161fd1b747f101 AS openclaw
FROM docker/sandbox-templates@sha256:da4458d89a5f739df50a2a334c08a3d15f15e11f0024bf47b37c88ad348a4644

USER root
COPY --from=openclaw /usr/local/bin/node /usr/local/bin/node
COPY --from=openclaw /app /app
RUN ln -sf /app/openclaw.mjs /usr/local/bin/openclaw \
    && chmod 755 /app/openclaw.mjs \
    && install -d -m 0700 -o agent -g agent /home/agent/.openclaw /home/agent/thaddeus-artifacts
USER agent
RUN python3 --version && node --version && openclaw --version
WORKDIR /home/agent
# Start the engine only after the host supplies its task grant and checks the boundary.
CMD ["/bin/bash"]
