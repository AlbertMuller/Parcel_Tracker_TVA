# syntax=docker/dockerfile:1

# ---- build: restore (cached layer) then copy sources ----
FROM mcr.microsoft.com/dotnet/sdk:10.0.401 AS build
WORKDIR /src
COPY global.json OrderTracker.sln ./
COPY src/OrderTracker.Api/OrderTracker.Api.csproj src/OrderTracker.Api/
COPY tests/OrderTracker.Tests/OrderTracker.Tests.csproj tests/OrderTracker.Tests/
RUN dotnet restore OrderTracker.sln
COPY . .

# ---- test: optional, run with `docker build --target test backend` ----
# Database tests need Docker (Testcontainers), so only the unit tests run inside the build.
FROM build AS test
RUN dotnet test OrderTracker.sln --no-restore --filter "Category!=Database"

# ---- publish ----
FROM build AS publish
RUN dotnet publish src/OrderTracker.Api/OrderTracker.Api.csproj -c Release -o /app/publish --no-restore /p:UseAppHost=false

# ---- runtime ----
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=publish /app/publish .
# Non-root user provided by the .NET base image.
USER $APP_UID
# The runtime image has no curl/wget, so probe /health with bash's /dev/tcp.
HEALTHCHECK --interval=10s --timeout=3s --start-period=10s --retries=3 \
  CMD ["bash", "-c", "exec 3<>/dev/tcp/127.0.0.1/8080 && printf 'GET /health HTTP/1.0\r\nHost: localhost\r\n\r\n' >&3 && grep -q Healthy <&3"]
ENTRYPOINT ["dotnet", "OrderTracker.Api.dll"]
