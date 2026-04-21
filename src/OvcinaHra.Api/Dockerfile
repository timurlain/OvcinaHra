FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG DEVEXPRESS_NUGET_SOURCE
ARG DEVEXPRESS_LICENSE_CONTENT
WORKDIR /src

COPY ["global.json", "."]
COPY ["NuGet.config.ci.template", "./"]
COPY ["src/OvcinaHra.Api/OvcinaHra.Api.csproj", "src/OvcinaHra.Api/"]
COPY ["src/OvcinaHra.Shared/OvcinaHra.Shared.csproj", "src/OvcinaHra.Shared/"]

RUN if [ -n "${DEVEXPRESS_LICENSE_CONTENT}" ]; then \
      mkdir -p "$HOME/.config/DevExpress"; \
      printf '%s' "${DEVEXPRESS_LICENSE_CONTENT}" > "$HOME/.config/DevExpress/DevExpress_License.txt"; \
    fi

RUN if [ -n "${DEVEXPRESS_NUGET_SOURCE}" ]; then \
      sed "s|__DEVEXPRESS_SOURCE__|${DEVEXPRESS_NUGET_SOURCE}|g" NuGet.config.ci.template > NuGet.config; \
      dotnet restore src/OvcinaHra.Api/OvcinaHra.Api.csproj --configfile NuGet.config; \
    else \
      dotnet restore src/OvcinaHra.Api/OvcinaHra.Api.csproj; \
    fi

COPY src/ src/

RUN dotnet publish src/OvcinaHra.Api/OvcinaHra.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
ARG GIT_SHA=unknown
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
# Propagate the build-time commit SHA to the runtime so /api/version can report it.
ENV GIT_SHA=$GIT_SHA
EXPOSE 8080

COPY --from=build /app/publish .

ENTRYPOINT ["dotnet", "OvcinaHra.Api.dll"]
