FROM mcr.microsoft.com/dotnet/runtime:5.0 AS base
WORKDIR /app
# install git
RUN apt-get update && \
  apt-get upgrade -y && \
  apt-get install -y git-core && \
  apt-get install -y curl
  
FROM mcr.microsoft.com/dotnet/sdk:5.0 AS build
WORKDIR /src
COPY ["dotnetthanks-loader.csproj", "./"]
RUN dotnet restore "dotnetthanks-loader.csproj"
COPY . .
WORKDIR "/src/."
RUN dotnet build "dotnetthanks-loader.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "dotnetthanks-loader.csproj" -c Release -o /app/publish

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
#ENTRYPOINT ["dotnet", "dotnetthanks-loader.dll"]


COPY ./startup.sh .
RUN chmod 777 startup.sh
ENTRYPOINT ["/bin/bash", "./startup.sh"]