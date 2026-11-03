FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY Directory.Build.props ./
COPY apps/HotelPulse.Domain/HotelPulse.Domain.csproj apps/HotelPulse.Domain/
COPY apps/HotelPulse.Infrastructure/HotelPulse.Infrastructure.csproj apps/HotelPulse.Infrastructure/
COPY apps/api/HotelPulse.Api.csproj apps/api/
RUN dotnet restore apps/api/HotelPulse.Api.csproj
COPY apps/HotelPulse.Domain/ apps/HotelPulse.Domain/
COPY apps/HotelPulse.Infrastructure/ apps/HotelPulse.Infrastructure/
COPY apps/api/ apps/api/
RUN dotnet publish apps/api/HotelPulse.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080
ENTRYPOINT ["dotnet", "HotelPulse.Api.dll"]
