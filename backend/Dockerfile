FROM mcr.microsoft.com/dotnet/sdk:8.0-bookworm-slim AS build
WORKDIR /src
COPY . .
RUN dotnet restore DeliveryOps.sln

ARG SERVICE_PROJECT
RUN test -n "$SERVICE_PROJECT" && \
    dotnet publish "$SERVICE_PROJECT" -c Release --no-restore -o /app/publish /p:UseAppHost=false

FROM build AS migrations
RUN dotnet tool restore && dotnet build DeliveryOps.sln -c Release --no-restore
COPY docker/run-migrations.sh /usr/local/bin/run-migrations
RUN chmod +x /usr/local/bin/run-migrations
ENTRYPOINT ["/usr/local/bin/run-migrations"]

FROM mcr.microsoft.com/dotnet/aspnet:8.0-bookworm-slim AS runtime
RUN apt-get update && apt-get install -y --no-install-recommends curl && \
    rm -rf /var/lib/apt/lists/* && \
    mkdir -p /app/keys/core /app/keys/integrations && \
    chown -R "$APP_UID:$APP_UID" /app
WORKDIR /app
COPY --from=build /app/publish .
ARG SERVICE_DLL
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production \
    SERVICE_DLL=${SERVICE_DLL}
EXPOSE 8080
USER $APP_UID
ENTRYPOINT ["sh", "-c", "dotnet $SERVICE_DLL"]
