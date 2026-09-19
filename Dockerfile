FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY ["WorkSphere/WorkSphere.csproj", "WorkSphere/"]
RUN dotnet restore "WorkSphere/WorkSphere.csproj"
COPY . .
WORKDIR "/src/WorkSphere"
RUN dotnet build "WorkSphere.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "WorkSphere.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "WorkSphere.dll"]
