# Build and run the DataHub API without installing .NET on the VPS host.
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AutoJMS.DataHub.Api/AutoJMS.DataHub.Api.csproj src/AutoJMS.DataHub.Api/
RUN dotnet restore src/AutoJMS.DataHub.Api/AutoJMS.DataHub.Api.csproj
COPY src/AutoJMS.DataHub.Api/ src/AutoJMS.DataHub.Api/
RUN dotnet publish src/AutoJMS.DataHub.Api/AutoJMS.DataHub.Api.csproj \
    -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_URLS=http://+:8080 \
    DOTNET_SYSTEM_GLOBALIZATION_INVARIANT=false
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
COPY --from=build /app/publish .
# Create and chown the manifest mount point BEFORE dropping privileges: Docker copies
# the image's ownership onto a fresh named volume, so a directory created later (or
# owned by root) leaves the non-root process unable to publish into it.
RUN mkdir -p /manifests && chown -R $APP_UID:$APP_UID /manifests
USER $APP_UID
VOLUME ["/manifests"]
EXPOSE 8080
ENTRYPOINT ["dotnet", "AutoJMS.DataHub.Api.dll"]
