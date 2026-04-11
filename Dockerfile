<<<<<<< HEAD
# Build stage
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /app

# Copy csproj and restore
COPY *.csproj ./
RUN dotnet restore

# Copy everything else and build
COPY . ./
RUN dotnet publish -c Release -o out

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/out .

# Expose port 8080 (Render default)
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

=======
# Giai đoạn Build (Sử dụng .NET 10 SDK)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Copy file project và restore dependencies
COPY ["Delivery-System.csproj", "./"]
RUN dotnet restore "Delivery-System.csproj"

# Copy toàn bộ mã nguồn và build ứng dụng
COPY . .
RUN dotnet build "Delivery-System.csproj" -c Release -o /app/build

# Giai đoạn Publish
FROM build AS publish
RUN dotnet publish "Delivery-System.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Giai đoạn Chạy (Sử dụng .NET 10 Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=publish /app/publish .

# Cấu hình cổng cho Render (10000)
ENV ASPNETCORE_URLS=http://+:10000
EXPOSE 10000

# Lệnh khởi chạy ứng dụng
>>>>>>> aeafdbe (docker)
ENTRYPOINT ["dotnet", "Delivery-System.dll"]
