FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY src/BusinessOS.Api/BusinessOS.Api.csproj src/BusinessOS.Api/
RUN dotnet restore src/BusinessOS.Api/BusinessOS.Api.csproj
COPY . .
RUN dotnet publish src/BusinessOS.Api/BusinessOS.Api.csproj -c Release -o /app/publish --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
ENTRYPOINT ["dotnet", "BusinessOS.Api.dll"]
