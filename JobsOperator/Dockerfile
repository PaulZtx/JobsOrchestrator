FROM mcr.microsoft.com/dotnet/runtime:10.0 AS base
USER $APP_UID
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["JobsOperator/JobsOperator.csproj", "JobsOperator/"]
COPY ["Jobs/Jobs.csproj", "Jobs/"]
COPY ["TestBuilds/TestBuilds.csproj", "TestBuilds/"]
RUN dotnet restore "JobsOperator/JobsOperator.csproj"
COPY . .
WORKDIR "/src/JobsOperator"
RUN dotnet build "./JobsOperator.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./JobsOperator.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM base AS final
USER root
WORKDIR /app
COPY --from=publish /app/publish .
RUN mkdir -p /app/checkpoints \
    && chown "$APP_UID:$APP_UID" /app/checkpoints
USER $APP_UID
ENTRYPOINT ["dotnet", "JobsOperator.dll"]
