FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
RUN mkdir -p /data

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["Ptlk.RedisScpi.csproj", "."]
RUN dotnet restore
COPY . .
RUN dotnet publish -c Release -o /app/publish --no-restore

FROM base AS final
WORKDIR /app
COPY --from=build /app/publish /app
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080
VOLUME ["/data"]
ENTRYPOINT ["dotnet", "/app/Ptlk.RedisScpi.dll"]
