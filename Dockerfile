# syntax=docker/dockerfile:1.7
#
# Multi-stage build for the SpeiseDirekt3 Blazor Server app.
# Two final targets:
#   - `app`     : runtime image (ASP.NET runtime only)
#   - `migrate` : SDK + dotnet-ef, applies EF migrations once at deploy time

ARG DOTNET_VERSION=9.0
ARG MAIN_PROJECT=SpeiseDirekt3/SpeiseDirekt3.csproj
ARG MAIN_DLL=SpeiseDirekt3.dll
ARG EF_PROJECT=SpeiseDirekt.Model/SpeiseDirekt.Model.csproj

# ─── Restore + build + publish ──────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS build
ARG MAIN_PROJECT
WORKDIR /src

# Copy project files first to maximise Docker layer cache reuse
COPY ["SpeiseDirekt3/SpeiseDirekt3.csproj", "SpeiseDirekt3/"]
COPY ["SpeiseDirekt.Model/SpeiseDirekt.Model.csproj", "SpeiseDirekt.Model/"]
COPY ["SpeiseDirekt.Api/SpeiseDirekt.Api.csproj", "SpeiseDirekt.Api/"]
COPY ["SpeiseDirekt.Api.IntegrationTests/SpeiseDirekt.Api.IntegrationTests.csproj", "SpeiseDirekt.Api.IntegrationTests/"]
RUN dotnet restore "${MAIN_PROJECT}"

COPY . .
RUN dotnet publish "${MAIN_PROJECT}" \
    --configuration Release \
    --no-restore \
    --output /publish

# ─── App runtime image ──────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/aspnet:${DOTNET_VERSION} AS app
ARG MAIN_DLL
ENV ASPNETCORE_HTTP_PORTS=8080 \
    DOTNET_RUNNING_IN_CONTAINER=true \
    DOTNET_NOLOGO=1 \
    MAIN_DLL=${MAIN_DLL}
WORKDIR /app
COPY --from=build /publish ./
EXPOSE 8080
ENTRYPOINT ["sh", "-c", "exec dotnet ${MAIN_DLL}"]

# ─── Migration runner ───────────────────────────────────────────────────────
FROM mcr.microsoft.com/dotnet/sdk:${DOTNET_VERSION} AS migrate
ARG MAIN_PROJECT
ARG EF_PROJECT
WORKDIR /src

COPY ["SpeiseDirekt3/SpeiseDirekt3.csproj", "SpeiseDirekt3/"]
COPY ["SpeiseDirekt.Model/SpeiseDirekt.Model.csproj", "SpeiseDirekt.Model/"]
COPY ["SpeiseDirekt.Api/SpeiseDirekt.Api.csproj", "SpeiseDirekt.Api/"]
COPY ["SpeiseDirekt.Api.IntegrationTests/SpeiseDirekt.Api.IntegrationTests.csproj", "SpeiseDirekt.Api.IntegrationTests/"]
RUN dotnet restore "${MAIN_PROJECT}"

COPY . .
RUN dotnet tool install --tool-path /usr/local/bin dotnet-ef
ENV MAIN_PROJECT=${MAIN_PROJECT} \
    EF_PROJECT=${EF_PROJECT}
ENTRYPOINT ["sh", "-c", "exec dotnet ef database update --project ${EF_PROJECT} --startup-project ${MAIN_PROJECT}"]
