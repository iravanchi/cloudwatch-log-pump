FROM mcr.microsoft.com/dotnet/runtime:3.1 AS base
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:3.1 AS build
WORKDIR /src
COPY . .
RUN dotnet restore cloudwatch-log-pump.sln
RUN dotnet build ./src/CloudWatchLogPump/CloudWatchLogPump.csproj -c Release --no-restore -o /app/build 
RUN dotnet publish ./src/CloudWatchLogPump/CloudWatchLogPump.csproj -c Release -o /app/publish
 
FROM base AS final
WORKDIR /app
COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "CloudWatchLogPump.dll"]
