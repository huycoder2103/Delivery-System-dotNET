# Giai đoạn Build
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

# Sao chép file dự án và restore các thư viện (để tận dụng cache của Docker)
COPY ["Delivery-System.csproj", "./"]
RUN dotnet restore "Delivery-System.csproj"

# Sao chép toàn bộ mã nguồn và build ứng dụng
COPY . .
RUN dotnet publish "Delivery-System.csproj" -c Release -o /app/publish /p:UseAppHost=false

# Giai đoạn Chạy (Runtime)
FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

# Cấu hình cổng cho Render
ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

# Lệnh khởi chạy ứng dụng
ENTRYPOINT ["dotnet", "Delivery-System.dll"]
