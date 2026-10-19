FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["TutorScheduler.API/TutorScheduler.API.csproj", "TutorScheduler.API/"]
COPY ["TutorScheduler.Core/TutorScheduler.Core.csproj", "TutorScheduler.Core/"]
COPY ["TutorScheduler.Data/TutorScheduler.Data.csproj", "TutorScheduler.Data/"]
COPY ["TutorScheduler.Service/TutorScheduler.Service.csproj", "TutorScheduler.Service/"]

RUN dotnet restore "TutorScheduler.API/TutorScheduler.API.csproj"

COPY . .
WORKDIR /src/TutorScheduler.API
RUN dotnet publish "TutorScheduler.API.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000
ENTRYPOINT ["dotnet", "TutorScheduler.API.dll"]
