FROM node:22-alpine AS deps
WORKDIR /app
ENV NG_CLI_ANALYTICS=false
COPY package.json package-lock.json ./
RUN npm ci --no-audit --no-fund

FROM deps AS test
COPY . .
CMD ["sh", "-c", "npx ng build --configuration development && npx ng test --watch=false"]

FROM deps AS build
COPY . .
RUN npx ng build

FROM nginx:1.27-alpine AS runtime
ENV GATEWAY_URL=http://gateway:8080 \
    ADMIN_KEY=change-me
COPY nginx.conf.template /etc/nginx/templates/default.conf.template
COPY --from=build /app/dist/llm-gateway-admin/browser /usr/share/nginx/html
EXPOSE 80
