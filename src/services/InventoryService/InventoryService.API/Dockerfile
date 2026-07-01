FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 8080

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["InventoryReservationSystem.ServiceDefaults/InventoryReservationSystem.ServiceDefaults.csproj", "InventoryReservationSystem.ServiceDefaults/"]
COPY ["src/contracts/InventoryReservationSystem.Contracts/InventoryReservationSystem.Contracts.csproj", "src/contracts/InventoryReservationSystem.Contracts/"]
COPY ["src/services/InventoryService/InventoryService.API/InventoryService.API.csproj", "src/services/InventoryService/InventoryService.API/"]
RUN dotnet restore "src/services/InventoryService/InventoryService.API/InventoryService.API.csproj"
COPY . .
RUN dotnet publish "src/services/InventoryService/InventoryService.API/InventoryService.API.csproj" -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "InventoryService.API.dll"]
