FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# Copy solution and project files for dependency restoration
COPY VenstarTranslator.sln ./
COPY VenstarTranslator/*.csproj ./VenstarTranslator/
COPY VenstarTranslator.Tests/*.csproj ./VenstarTranslator.Tests/
RUN dotnet restore VenstarTranslator/VenstarTranslator.csproj -a $TARGETARCH

# Copy source code and build
COPY VenstarTranslator/. ./VenstarTranslator/

WORKDIR /source/VenstarTranslator
RUN dotnet publish -c Release -o /app -a $TARGETARCH --no-restore

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app

# Run as non-root user
USER $APP_UID

COPY --from=build /app ./
COPY VenstarTranslator/web/. ./web

EXPOSE 8080
ENTRYPOINT ["/app/VenstarTranslator"]
