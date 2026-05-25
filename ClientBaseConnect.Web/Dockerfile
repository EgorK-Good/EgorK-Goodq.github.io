FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src
COPY ClientBaseConnect.Web/ClientBaseConnect.Web.csproj ClientBaseConnect.Web/
RUN dotnet restore ClientBaseConnect.Web/ClientBaseConnect.Web.csproj
COPY ClientBaseConnect.Web/ ClientBaseConnect.Web/
RUN dotnet publish ClientBaseConnect.Web/ClientBaseConnect.Web.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://0.0.0.0:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "ClientBaseConnect.Web.dll"]
