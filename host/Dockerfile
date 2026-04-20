FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS base
WORKDIR /app
EXPOSE 80
EXPOSE 443

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY ["host/src/Dignite.Paperbase.Host.csproj", "host/src/"]
COPY ["core/src/Dignite.Paperbase.Abstractions/Dignite.Paperbase.Abstractions.csproj", "core/src/Dignite.Paperbase.Abstractions/"]
COPY ["core/src/Dignite.Paperbase.Domain.Shared/Dignite.Paperbase.Domain.Shared.csproj", "core/src/Dignite.Paperbase.Domain.Shared/"]
COPY ["core/src/Dignite.Paperbase.Domain/Dignite.Paperbase.Domain.csproj", "core/src/Dignite.Paperbase.Domain/"]
COPY ["core/src/Dignite.Paperbase.Application.Contracts/Dignite.Paperbase.Application.Contracts.csproj", "core/src/Dignite.Paperbase.Application.Contracts/"]
COPY ["core/src/Dignite.Paperbase.Application/Dignite.Paperbase.Application.csproj", "core/src/Dignite.Paperbase.Application/"]
COPY ["core/src/Dignite.Paperbase.EntityFrameworkCore/Dignite.Paperbase.EntityFrameworkCore.csproj", "core/src/Dignite.Paperbase.EntityFrameworkCore/"]
COPY ["core/src/Dignite.Paperbase.HttpApi/Dignite.Paperbase.HttpApi.csproj", "core/src/Dignite.Paperbase.HttpApi/"]
COPY ["host/common.props", "host/"]
COPY ["core/common.props", "core/"]

RUN dotnet restore "host/src/Dignite.Paperbase.Host.csproj"

COPY host/ host/
COPY core/src/ core/src/

RUN dotnet build "host/src/Dignite.Paperbase.Host.csproj" -c Release -o /app/build

FROM build AS publish
RUN dotnet publish "host/src/Dignite.Paperbase.Host.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM base AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "Dignite.Paperbase.Host.dll"]
