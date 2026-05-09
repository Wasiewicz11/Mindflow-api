FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

COPY src/Mindflow/Mindflow.Api/Mindflow.Api.csproj ./
RUN dotnet restore ./Mindflow.Api.csproj

COPY src/ ./src/
RUN dotnet publish ./Mindflow.Api.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "Mindflow.Api.dll"]
