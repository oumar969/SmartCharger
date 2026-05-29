FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY ["src/SmartCharger.Api/SmartCharger.Api.csproj", "src/SmartCharger.Api/"]
RUN dotnet restore "src/SmartCharger.Api/SmartCharger.Api.csproj" --runtime linux-musl-x64
COPY . .
RUN dotnet publish "src/SmartCharger.Api/SmartCharger.Api.csproj" \
    -c Release -o /app/publish \
    --runtime linux-musl-x64 --self-contained false

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
EXPOSE 8080
# Run as non-root for security
RUN adduser -D appuser
USER appuser
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "SmartCharger.Api.dll"]
