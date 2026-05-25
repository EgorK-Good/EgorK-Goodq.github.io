FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ClientBaseConnect.ApiMock/ClientBaseConnect.ApiMock.csproj ClientBaseConnect.ApiMock/
RUN dotnet restore ClientBaseConnect.ApiMock/ClientBaseConnect.ApiMock.csproj
COPY ClientBaseConnect.ApiMock/ ClientBaseConnect.ApiMock/
RUN dotnet publish ClientBaseConnect.ApiMock/ClientBaseConnect.ApiMock.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ClientBaseConnect.ApiMock.dll"]
