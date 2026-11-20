FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OrderWorker/OrderWorker.csproj OrderWorker/
RUN dotnet restore OrderWorker/OrderWorker.csproj
COPY OrderWorker/ OrderWorker/
RUN dotnet publish OrderWorker/OrderWorker.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderWorker.dll"]
