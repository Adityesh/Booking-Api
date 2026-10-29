
# BUILD SDK
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY BookingApi/BookingApi.csproj BookingApi/
COPY BookingApi.Contracts/BookingApi.Contracts.csproj BookingApi.Contracts/
RUN dotnet restore BookingApi/BookingApi.csproj

COPY BookingApi/ BookingApi/
COPY BookingApi.Contracts/ BookingApi.Contracts
RUN dotnet publish BookingApi/BookingApi.csproj -c Release -o /app/publish --no-restore

# RUN MIGRATIONS FOR DB
# ---------- Stage: migration bundle ----------
FROM build AS migrate
# Match the major version of your EF Core packages
RUN dotnet tool install --global dotnet-ef --version 10.*
ENV PATH="${PATH}:/root/.dotnet/tools"

# Compile all migrations into one self-contained executable
RUN dotnet ef migrations bundle \
      --project BookingApi/BookingApi.csproj \
      --self-contained -r linux-x64 \
      --output /app/efbundle --force

ENTRYPOINT ["/app/efbundle"]


# BUILD RUNTIME

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app

COPY --from=build /app/publish .

USER $APP_UID

ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "BookingApi.dll"]