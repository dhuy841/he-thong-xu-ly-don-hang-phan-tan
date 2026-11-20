FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY OrderApi/OrderApi.csproj OrderApi/
RUN dotnet restore OrderApi/OrderApi.csproj
COPY OrderApi/ OrderApi/
RUN dotnet publish OrderApi/OrderApi.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "OrderApi.dll"]
