FROM node:24-alpine AS build
WORKDIR /app
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
RUN corepack enable && corepack prepare pnpm@11.1.1 --activate
ENV CI=true
RUN pnpm install --frozen-lockfile
COPY . .
RUN pnpm run build

FROM node:24-alpine AS runtime
WORKDIR /app
COPY --from=build /app/.output .
EXPOSE 3000
ENV PORT=3000
CMD ["node", "server/index.mjs"]
