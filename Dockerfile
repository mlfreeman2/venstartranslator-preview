# https://hub.docker.com/_/microsoft-dotnet
# docker build --platform linux/amd64 -t 12264-apps:5000/mlfreeman/venstartranslator .
# docker push 12264-apps:5000/mlfreeman/venstartranslator 
FROM --platform=$BUILDPLATFORM mcr.microsoft.com/dotnet/sdk:8.0 AS build
ARG TARGETARCH
WORKDIR /source

# copy csproj and restore as distinct layers
COPY VenstarTranslator/. ./VenstarTranslator/

# copy everything else and build app
WORKDIR /source/VenstarTranslator
RUN dotnet publish -c release -o /app -a $TARGETARCH

# final stage/image
FROM mcr.microsoft.com/dotnet/aspnet:8.0
WORKDIR /app
COPY --from=build /app ./
COPY VenstarTranslator/web/. ./web
#docker run -i -t 
#ENTRYPOINT ["/bin/bash"]
EXPOSE 8080
ENTRYPOINT ["/app/VenstarTranslator"]
