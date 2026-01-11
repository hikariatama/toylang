FROM mcr.microsoft.com/dotnet/sdk:9.0 AS base
FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
  && apt-get install -y --no-install-recommends ca-certificates curl unzip git build-essential \
  && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
  && apt-get install -y --no-install-recommends nodejs \
  && corepack enable \
  && corepack prepare pnpm@latest --activate \
  && rm -rf /var/lib/apt/lists/*

WORKDIR /src

COPY frontend/package.json frontend/pnpm-lock.yaml ./frontend/
RUN cd frontend && pnpm install --frozen-lockfile

COPY . .

RUN make build

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS final

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
  && apt-get install -y --no-install-recommends make curl \
  && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://deb.nodesource.com/setup_22.x | bash - \
  && apt-get install -y --no-install-recommends nodejs \
  && corepack enable \
  && corepack prepare pnpm@latest --activate \
  && rm -rf /var/lib/apt/lists/*

WORKDIR /app

COPY Makefile ./
COPY --from=build /src/frontend/.next ./frontend/.next
COPY --from=build /src/frontend/public ./frontend/public
COPY --from=build /src/frontend/package.json ./frontend/
COPY --from=build /src/frontend/node_modules ./frontend/node_modules
COPY --from=build /src/frontend/Makefile ./frontend/Makefile

COPY --from=build /src/compiler/bin/Release/net9.0/ /app/compiler/bin/Release/net9.0/

ENV NODE_ENV=production \
    NEXT_TELEMETRY_DISABLED=1 \
    COMPILER_PATH=/app/compiler/bin/Release/net9.0/CompilersApp \
    PORT=3000 \
    HOST=0.0.0.0

EXPOSE 3000

CMD ["make","start"]
