FROM mcr.microsoft.com/dotnet/sdk:10.0.302-noble AS build
WORKDIR /src

COPY ["global.json", "."]
COPY ["DetectDoc.slnx", "."]
COPY ["src/DetectDoc.Api/DetectDoc.Api.csproj", "src/DetectDoc.Api/"]
COPY ["src/DetectDoc.Core/DetectDoc.Core.csproj", "src/DetectDoc.Core/"]
COPY ["src/DetectDoc.Infrastructure/DetectDoc.Infrastructure.csproj", "src/DetectDoc.Infrastructure/"]
COPY ["tests/DetectDoc.Tests/DetectDoc.Tests.csproj", "tests/DetectDoc.Tests/"]
RUN dotnet restore DetectDoc.slnx

COPY . .
RUN dotnet test DetectDoc.slnx --no-restore
RUN dotnet publish src/DetectDoc.Api/DetectDoc.Api.csproj --configuration Release --no-restore --output /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0.10-noble AS final
WORKDIR /app
COPY --from=build --chown=app:app /app/publish .

USER app
EXPOSE 8080
ENV ASPNETCORE_HTTP_PORTS=8080
ENTRYPOINT ["dotnet", "DetectDoc.Api.dll"]
