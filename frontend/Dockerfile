# syntax=docker/dockerfile:1

# ---- build the Angular app ----
FROM node:24-alpine AS build
WORKDIR /app
COPY package.json package-lock.json ./
RUN npm ci
COPY . .
RUN npm run build

# ---- serve with nginx (non-root, listens on 8080) ----
FROM nginxinc/nginx-unprivileged:1.29-alpine
# Where nginx forwards /api requests. Substituted into the config template at container start.
ENV API_UPSTREAM=http://api:8080
COPY nginx/default.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/frontend/browser /usr/share/nginx/html
EXPOSE 8080
HEALTHCHECK --interval=10s --timeout=3s --retries=3 CMD wget -q -O /dev/null http://127.0.0.1:8080/healthz || exit 1
