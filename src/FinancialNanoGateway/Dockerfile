FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/FinancialNanoGateway/FinancialNanoGateway.csproj src/FinancialNanoGateway/
RUN dotnet restore src/FinancialNanoGateway/FinancialNanoGateway.csproj

COPY . .
RUN dotnet publish src/FinancialNanoGateway/FinancialNanoGateway.csproj \
    --configuration Release \
    --output /app/publish \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "FinancialNanoGateway.dll"]
