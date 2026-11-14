FROM mcr.microsoft.com/dotnet/sdk:10.0.404 AS build
ARG SERVICE
ARG HOST
WORKDIR /source
COPY . .
RUN --mount=type=cache,id=mini-wallet-nuget,target=/root/.nuget/packages,sharing=locked dotnet publish src/${SERVICE}/${SERVICE}.${HOST}/${SERVICE}.${HOST}.csproj -c Release -o /app --no-self-contained /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.15
ARG SERVICE
ARG HOST
ENV APP_DLL=${SERVICE}.${HOST}.dll
WORKDIR /app
COPY --from=build /app .
USER $APP_UID
ENTRYPOINT ["sh", "-c", "exec dotnet \"$APP_DLL\" \"$@\"", "--"]
