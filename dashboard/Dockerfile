# Hugo dashboard image. Build from the repository root:
#   docker build -t jchristn77/hugo-dashboard:<tag> dashboard

FROM node:22-alpine AS build
WORKDIR /app
COPY package*.json ./
RUN npm ci
COPY . .
RUN npm run build

FROM nginx:alpine
ARG HUGO_VERSION=0.1.0-alpha
LABEL org.opencontainers.image.title="hugo-dashboard" \
      org.opencontainers.image.version="${HUGO_VERSION}" \
      org.opencontainers.image.source="https://github.com/jchristn/AgentSmith" \
      org.opencontainers.image.licenses="MIT"
RUN apk add --no-cache curl
COPY --from=build /app/dist/ /usr/share/nginx/html/
COPY nginx.conf /etc/nginx/conf.d/default.conf
COPY docker-entrypoint.sh /docker-entrypoint.d/40-hugo-config.sh
RUN chmod 755 /docker-entrypoint.d/40-hugo-config.sh
ENV HUGO_API_URL="" \
    HUGO_API_UPSTREAM="http://hugo-server:8800"
EXPOSE 80
HEALTHCHECK --interval=5s --timeout=3s --retries=2 CMD curl -f http://127.0.0.1/health || exit 1
