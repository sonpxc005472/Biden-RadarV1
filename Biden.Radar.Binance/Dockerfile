FROM mcr.microsoft.com/dotnet/sdk:6.0-alpine3.14 AS builder

ENV GLIBC_REPO=https://github.com/sgerrand/alpine-pkg-glibc
ENV GLIBC_VERSION=2.30-r0
RUN set -ex && \
    apk --update add libstdc++ curl ca-certificates && \
    for pkg in glibc-${GLIBC_VERSION} glibc-bin-${GLIBC_VERSION}; \
        do curl -sSL ${GLIBC_REPO}/releases/download/${GLIBC_VERSION}/${pkg}.apk -o /tmp/${pkg}.apk; done && \
    apk add --allow-untrusted /tmp/*.apk && \
    rm -v /tmp/*.apk && \
    /usr/glibc-compat/sbin/ldconfig /lib /usr/glibc-compat/lib


WORKDIR /src

COPY ["nuget.config", "./"]
RUN true
COPY ["Biden.Radar.Binance/*.csproj", "Biden.Radar.Binance/"]
RUN true
COPY ["Biden.Radar.Common/*.csproj", "Biden.Radar.Common/"]

RUN dotnet restore Biden.Radar.Binance/ /property:Configuration=Release -nowarn:msb3202,nu1503 --configfile nuget.config

COPY ["Biden.Radar.Binance/.", "Biden.Radar.Binance/"]
RUN true
COPY ["Biden.Radar.Common/.", "Biden.Radar.Common/"]

RUN dotnet publish Biden.Radar.Binance/ -c Release -o /app

FROM mcr.microsoft.com/dotnet/aspnet:6.0-alpine3.14

WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT docker

COPY --from=builder /app .

ENTRYPOINT ["dotnet", "Biden.Radar.Binance.dll"]