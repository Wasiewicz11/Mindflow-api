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

# Configuration comes from environment variables in production, so the file watchers
# the host sets up by default are pure cost - and they exhaust the host's small inotify
# limit, which crashes the container before Program.cs runs.
ENV DOTNET_hostBuilder__reloadConfigOnChange=false

COPY --from=build /app/out ./
ENTRYPOINT ["dotnet", "Mindflow.Api.dll"]
