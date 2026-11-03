FROM node:26-alpine AS build
WORKDIR /app
COPY package.json pnpm-lock.yaml pnpm-workspace.yaml ./
# Node 26 images no longer ship Corepack, so install the pinned pnpm directly.
RUN npm install -g pnpm@12.16.1
ENV CI=true
RUN pnpm install --frozen-lockfile
COPY . .
RUN pnpm run build

FROM node:26-alpine AS runtime
WORKDIR /app
COPY --from=build /app/.output .
EXPOSE 3000
ENV HOST=0.0.0.0
ENV PORT=3000
CMD ["node", "server/index.mjs"]
