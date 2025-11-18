FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:10.0 AS build
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
FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app

COPY --from=build /app ./
COPY VenstarTranslator/web/. ./web

# Create data directories with proper permissions before switching to non-root user
RUN mkdir -p /app/data /data && chown -R $APP_UID /app/data /data

# Run as non-root user
USER $APP_UID

# Set production environment
ENV ASPNETCORE_ENVIRONMENT=Production

EXPOSE 8080 8443
ENTRYPOINT ["/app/VenstarTranslator"]
