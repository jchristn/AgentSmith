#!/usr/bin/env bash
# Builds and pushes the multi-arch (linux/amd64, linux/arm64) jchristn77/hugo-dashboard image with Docker Build Cloud,
# tagged with the supplied tag and latest, then pulls both tags back locally.
# Usage: ./build-dashboard.sh <tag>   (for example v0.1.0)
# Overrides: BUILDER (default cloud-jchristn77-jchristn77), PLATFORMS (default linux/amd64,linux/arm64/v8).
set -euo pipefail
if [ $# -lt 1 ] || [ -z "$1" ]; then
  echo "usage: $0 <tag>" >&2
  exit 2
fi
TAG="$1"
IMAGE_NAME="jchristn77/hugo-dashboard"
DOCKERFILE_PATH="dashboard/Dockerfile"
BUILD_CONTEXT="dashboard"
BUILDER="${BUILDER:-cloud-jchristn77-jchristn77}"
PLATFORMS="${PLATFORMS:-linux/amd64,linux/arm64/v8}"
cd "$(dirname "$0")"
docker buildx build --builder "${BUILDER}" --platform "${PLATFORMS}" \
  -f "${DOCKERFILE_PATH}" -t "${IMAGE_NAME}:${TAG}" -t "${IMAGE_NAME}:latest" --push "${BUILD_CONTEXT}"
docker pull "${IMAGE_NAME}:${TAG}"
docker pull "${IMAGE_NAME}:latest"
echo "built and pushed ${IMAGE_NAME}:${TAG} and ${IMAGE_NAME}:latest"
