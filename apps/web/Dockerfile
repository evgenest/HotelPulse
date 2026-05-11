FROM node:20-alpine AS build
WORKDIR /app
COPY package.json .
RUN npm install
COPY . .
RUN npm run build

FROM node:20-alpine AS runtime
WORKDIR /app
COPY --from=build /app/.output .
EXPOSE 3000
ENV PORT=3000
CMD ["node", "server/index.mjs"]
