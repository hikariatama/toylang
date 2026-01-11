FROM mcr.microsoft.com/dotnet/sdk:9.0 AS base

ARG DEBIAN_FRONTEND=noninteractive
RUN apt-get update \
  && apt-get install -y --no-install-recommends openssh-server make unzip curl ca-certificates jq wabt git busybox-static \
  && rm -rf /var/lib/apt/lists/*

RUN curl -fsSL https://deb.nodesource.com/setup_20.x | bash - \
  && apt-get update \
  && apt-get install -y --no-install-recommends nodejs \
  && rm -rf /var/lib/apt/lists/* \
  && corepack enable \
  && corepack prepare pnpm@latest --activate

FROM base AS builder
WORKDIR /workspace
COPY . .
ENV NUGET_PACKAGES=/workspace/.nuget/packages
RUN dotnet restore compiler/CompilersApp.csproj
RUN cd frontend && pnpm install

FROM base AS runtime

ARG SSH_USER=dev
ARG SSH_PASSWORD=devpass
ENV SSH_USER=${SSH_USER}
ENV SSH_PASSWORD=${SSH_PASSWORD}

RUN useradd -m -s /bin/bash "$SSH_USER" \
  && echo "$SSH_USER:$SSH_PASSWORD" | chpasswd \
  && mkdir -p /var/run/sshd

RUN ssh-keygen -A

RUN sed -i "s/^#\\?PasswordAuthentication .*/PasswordAuthentication yes/" /etc/ssh/sshd_config \
  && sed -i "s/^#\\?PermitRootLogin .*/PermitRootLogin no/" /etc/ssh/sshd_config

COPY --from=builder /workspace "/home/${SSH_USER}"

COPY docker/cli-entrypoint.sh /usr/local/bin/cli-entrypoint.sh
RUN chmod +x /usr/local/bin/cli-entrypoint.sh \
  && mkdir -p /etc/profile.d \
  && printf 'export PNPM_HOME=$HOME/.local/share/pnpm\nexport PATH=$PNPM_HOME:$PATH\n' >/etc/profile.d/pnpm.sh \
  && chown -R "${SSH_USER}:${SSH_USER}" /home/"${SSH_USER}"

EXPOSE 22
ENTRYPOINT ["/usr/local/bin/cli-entrypoint.sh"]
