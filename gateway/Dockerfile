FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
ENV DOTNET_CLI_TELEMETRY_OPTOUT=1 \
    DOTNET_NOLOGO=1
COPY Directory.Build.props Directory.Packages.props LlmGateway.slnx ./
COPY src/LlmGateway.Core/LlmGateway.Core.csproj src/LlmGateway.Core/
COPY src/LlmGateway.Infrastructure/LlmGateway.Infrastructure.csproj src/LlmGateway.Infrastructure/
COPY src/LlmGateway.Api/LlmGateway.Api.csproj src/LlmGateway.Api/
COPY tests/LlmGateway.Tests/LlmGateway.Tests.csproj tests/LlmGateway.Tests/
RUN dotnet restore LlmGateway.slnx
COPY . .

FROM build AS test
CMD ["sh", "-c", "dotnet format LlmGateway.slnx --verify-no-changes --no-restore && dotnet test LlmGateway.slnx --no-restore --logger \"console;verbosity=normal\""]

FROM build AS publish
RUN dotnet publish src/LlmGateway.Api/LlmGateway.Api.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=publish /app .
USER $APP_UID
EXPOSE 8080
HEALTHCHECK --interval=5s --timeout=3s --retries=20 CMD ["dotnet", "LlmGateway.Api.dll", "--healthcheck"]
ENTRYPOINT ["dotnet", "LlmGateway.Api.dll"]
