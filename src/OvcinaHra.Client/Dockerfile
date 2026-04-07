FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG DEVEXPRESS_NUGET_SOURCE
ARG DEVEXPRESS_LICENSE_CONTENT
ARG API_BASE_URL=https://localhost:5280
WORKDIR /src

COPY ["global.json", "."]
COPY ["NuGet.config.ci.template", "./"]
COPY ["src/OvcinaHra.Client/OvcinaHra.Client.csproj", "src/OvcinaHra.Client/"]
COPY ["src/OvcinaHra.Shared/OvcinaHra.Shared.csproj", "src/OvcinaHra.Shared/"]

RUN if [ -n "${DEVEXPRESS_LICENSE_CONTENT}" ]; then \
      mkdir -p "$HOME/.config/DevExpress"; \
      printf '%s' "${DEVEXPRESS_LICENSE_CONTENT}" > "$HOME/.config/DevExpress/DevExpress_License.txt"; \
    fi

RUN if [ -n "${DEVEXPRESS_NUGET_SOURCE}" ]; then \
      sed "s|__DEVEXPRESS_SOURCE__|${DEVEXPRESS_NUGET_SOURCE}|g" NuGet.config.ci.template > NuGet.config; \
      dotnet restore src/OvcinaHra.Client/OvcinaHra.Client.csproj --configfile NuGet.config; \
    else \
      dotnet restore src/OvcinaHra.Client/OvcinaHra.Client.csproj; \
    fi

COPY src/ src/

RUN dotnet publish src/OvcinaHra.Client/OvcinaHra.Client.csproj -c Release -o /app/publish --no-restore

# Inject production API URL into the published appsettings.json
RUN if [ "${API_BASE_URL}" != "https://localhost:5280" ]; then \
      sed -i "s|https://localhost:5280|${API_BASE_URL}|g" /app/publish/wwwroot/appsettings.json; \
    fi

FROM nginx:alpine AS final

COPY --from=build /app/publish/wwwroot /usr/share/nginx/html
COPY src/OvcinaHra.Client/nginx.conf /etc/nginx/nginx.conf

EXPOSE 8080
