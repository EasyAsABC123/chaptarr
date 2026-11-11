FROM docker.io/chaptarr/chaptarr:0.9.965@sha256:bb00d67cbe1485d1cd61ef178362a29b42aa08f86b290841aab4b115ad2647a6
ARG SOURCE_REVISION
LABEL org.opencontainers.image.source="https://github.com/EasyAsABC123/chaptarr" \
      org.opencontainers.image.revision="${SOURCE_REVISION}"
COPY .homelab-image/Chaptarr.Core.dll .homelab-image/Chaptarr.Core.pdb .homelab-image/Chaptarr.Http.dll .homelab-image/Chaptarr.Http.pdb .homelab-image/Chaptarr.Api.V1.dll .homelab-image/Chaptarr.Api.V1.pdb /app/
COPY .homelab-image/build.json /app/homelab-build.json
