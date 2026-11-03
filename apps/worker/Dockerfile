FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY apps/HotelPulse.Domain/HotelPulse.Domain.csproj apps/HotelPulse.Domain/
COPY apps/HotelPulse.Infrastructure/HotelPulse.Infrastructure.csproj apps/HotelPulse.Infrastructure/
COPY apps/worker/HotelPulse.Worker.csproj apps/worker/
RUN dotnet restore apps/worker/HotelPulse.Worker.csproj
COPY apps/HotelPulse.Domain/ apps/HotelPulse.Domain/
COPY apps/HotelPulse.Infrastructure/ apps/HotelPulse.Infrastructure/
COPY apps/worker/ apps/worker/
RUN dotnet publish apps/worker/HotelPulse.Worker.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
USER $APP_UID
ENTRYPOINT ["dotnet", "HotelPulse.Worker.dll"]
