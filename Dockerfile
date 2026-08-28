FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Keep the project in its own directory: an SDK-style project compiles every .cs
# below it, so flattening it here would pull in sibling projects such as the tests.
COPY src/Mindflow/Mindflow.Api/Mindflow.Api.csproj src/Mindflow/Mindflow.Api/
RUN dotnet restore src/Mindflow/Mindflow.Api/Mindflow.Api.csproj

COPY src/ src/
RUN dotnet publish src/Mindflow/Mindflow.Api/Mindflow.Api.csproj -c Release -o /app/out

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "Mindflow.Api.dll"]
