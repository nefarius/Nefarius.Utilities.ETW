# Build context: the repository root (see docker-compose.yml).
FROM mcr.microsoft.com/dotnet/sdk:11.0 AS build
WORKDIR /src
COPY . .
RUN dotnet publish tools/Nefarius.Utilities.ETW.OtlpGateway -c Release -f net10.0 -o /out \
    -p:EnableWindowsTargeting=true

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /out .
EXPOSE 4317 4318
ENTRYPOINT ["dotnet", "Nefarius.Utilities.ETW.OtlpGateway.dll"]