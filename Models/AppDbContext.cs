using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;
using Pomelo.EntityFrameworkCore.MySql.Scaffolding.Internal;

namespace Delivery_System.Models;

public partial class AppDbContext : DbContext
{
    public AppDbContext()
    {
    }

    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<TblAnnouncement> TblAnnouncements { get; set; }

    public virtual DbSet<TblFeedback> TblFeedbacks { get; set; }

    public virtual DbSet<TblOrder> TblOrders { get; set; }

    public virtual DbSet<TblOrderTrip> TblOrderTrips { get; set; }

    public virtual DbSet<TblRole> TblRoles { get; set; }

    public virtual DbSet<TblStation> TblStations { get; set; }

    public virtual DbSet<TblTrip> TblTrips { get; set; }

    public virtual DbSet<TblTruck> TblTrucks { get; set; }

    public virtual DbSet<TblUser> TblUsers { get; set; }

    public virtual DbSet<TblWorkShift> TblWorkShifts { get; set; }

    public virtual DbSet<TblShiftAccounting> TblShiftAccountings { get; set; }

    public virtual DbSet<VwOrderList> VwOrderLists { get; set; }

    public virtual DbSet<VwTripList> VwTripLists { get; set; }

    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Connection string is configured in Program.cs
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .UseCollation("utf8mb4_0900_ai_ci")
            .HasCharSet("utf8mb4");

        // 1. GLOBAL QUERY FILTER: Tự động loại bỏ các bản ghi đã xóa (Soft Delete)
        modelBuilder.Entity<TblOrder>().HasQueryFilter(o => o.IsDeleted == false || o.IsDeleted == null);

        modelBuilder.Entity<TblAnnouncement>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("tblAnnouncements");

            entity.HasIndex(e => e.CreatedBy, "createdBy");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.Content)
                .HasColumnType("text")
                .HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.CreatedBy)
                .HasMaxLength(20)
                .HasColumnName("createdBy");
            entity.Property(e => e.IsActive)
                .HasDefaultValueSql("'1'")
                .HasColumnName("isActive");
            entity.Property(e => e.Title)
                .HasMaxLength(200)
                .HasColumnName("title");

            entity.HasOne(d => d.CreatedByNavigation).WithMany(p => p.TblAnnouncements)
                .HasForeignKey(d => d.CreatedBy)
                .HasConstraintName("tblAnnouncements_ibfk_1");
        });

        modelBuilder.Entity<TblFeedback>(entity =>
        {
            entity.HasKey(e => e.FeedbackId).HasName("PRIMARY");

            entity.ToTable("tblFeedbacks");

            entity.HasIndex(e => e.UserId, "FK_Feedback_User");

            entity.Property(e => e.FeedbackId).HasColumnName("feedbackID");
            entity.Property(e => e.Content)
                .HasColumnType("text")
                .HasColumnName("content");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.UserId)
                .HasMaxLength(50)
                .HasColumnName("userID");

            entity.HasOne(d => d.User).WithMany(p => p.TblFeedbacks)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("FK_Feedback_User");
        });

        modelBuilder.Entity<TblOrder>(entity =>
        {
            entity.HasKey(e => e.OrderId).HasName("PRIMARY");

            entity.ToTable("tblOrders");

            entity.HasIndex(e => e.CreatedAt, "idx_orders_createdAt");

            entity.HasIndex(e => e.ShiftId, "idx_orders_shiftID");

            // TỐI ƯU: Index hỗn hợp giúp lọc cực nhanh các đơn chưa xóa theo trạng thái
            entity.HasIndex(e => new { e.IsDeleted, e.ShipStatus }, "idx_orders_status_deleted");

            entity.HasIndex(e => e.StaffInput, "staffInput");

            entity.HasIndex(e => e.StaffReceive, "staffReceive");

            entity.HasIndex(e => e.SendStation, "idx_orders_sendStation");
            entity.HasIndex(e => e.ReceiveStation, "idx_orders_receiveStation");
            entity.HasIndex(e => e.SenderPhone, "idx_orders_senderPhone");
            entity.HasIndex(e => e.ReceiverPhone, "idx_orders_receiverPhone");

            entity.HasIndex(e => e.StaffReceive, "idx_orders_staffReceive");
            entity.HasIndex(e => e.StaffInput, "idx_orders_staffInput");
            entity.HasIndex(e => e.ReceiveDate, "idx_orders_receiveDate");
            entity.HasIndex(e => new { e.StaffReceive, e.ShipStatus, e.IsDeleted }, "idx_orders_delivery_tracking");

            entity.Property(e => e.OrderId)
                .HasMaxLength(20)
                .HasColumnName("orderID");
            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.Ct)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("ct");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValueSql("'0'")
                .HasColumnName("isDeleted");
            entity.Property(e => e.ItemName)
                .HasMaxLength(200)
                .HasColumnName("itemName");
            entity.Property(e => e.Note)
                .HasMaxLength(500)
                .HasColumnName("note");
            entity.Property(e => e.ReceiveDate)
                .HasMaxLength(30)
                .HasColumnName("receiveDate");
            entity.Property(e => e.ReceiveStation)
                .HasMaxLength(100)
                .HasColumnName("receiveStation");
            entity.Property(e => e.ReceiverName)
                .HasMaxLength(100)
                .HasColumnName("receiverName");
            entity.Property(e => e.ReceiverPhone)
                .HasMaxLength(15)
                .HasColumnName("receiverPhone");
            entity.Property(e => e.SendStation)
                .HasMaxLength(100)
                .HasColumnName("sendStation");
            entity.Property(e => e.SenderName)
                .HasMaxLength(100)
                .HasColumnName("senderName");
            entity.Property(e => e.SenderPhone)
                .HasMaxLength(15)
                .HasColumnName("senderPhone");
            entity.Property(e => e.ShiftId).HasColumnName("shiftID");
            entity.Property(e => e.ShipStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Chưa Chuyển'")
                .HasColumnName("shipStatus");
            entity.Property(e => e.StaffInput)
                .HasMaxLength(20)
                .HasColumnName("staffInput");
            entity.Property(e => e.StaffReceive)
                .HasMaxLength(20)
                .HasColumnName("staffReceive");
            entity.Property(e => e.Tr)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("tr");
            entity.Property(e => e.TripId)
                .HasMaxLength(20)
                .HasColumnName("tripID");

            entity.HasOne(d => d.StaffInputNavigation).WithMany(p => p.TblOrderStaffInputNavigations)
                .HasForeignKey(d => d.StaffInput)
                .HasConstraintName("tblOrders_ibfk_1");

            entity.HasOne(d => d.StaffReceiveNavigation).WithMany(p => p.TblOrderStaffReceiveNavigations)
                .HasForeignKey(d => d.StaffReceive)
                .HasConstraintName("tblOrders_ibfk_2");
        });

        modelBuilder.Entity<TblOrderTrip>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("PRIMARY");

            entity.ToTable("tblOrderTrip");

            entity.HasIndex(e => e.OrderId, "orderID");

            entity.HasIndex(e => e.TripId, "tripID");

            entity.Property(e => e.Id).HasColumnName("id");
            entity.Property(e => e.OrderId)
                .HasMaxLength(20)
                .HasColumnName("orderID");
            entity.Property(e => e.TripId)
                .HasMaxLength(20)
                .HasColumnName("tripID");

            entity.HasOne(d => d.Order).WithMany(p => p.TblOrderTrips)
                .HasForeignKey(d => d.OrderId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblOrderTrip_ibfk_1");

            entity.HasOne(d => d.Trip).WithMany(p => p.TblOrderTrips)
                .HasForeignKey(d => d.TripId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblOrderTrip_ibfk_2");
        });

        modelBuilder.Entity<TblRole>(entity =>
        {
            entity.HasKey(e => e.RoleId).HasName("PRIMARY");

            entity.ToTable("tblRoles");

            entity.Property(e => e.RoleId)
                .HasMaxLength(5)
                .HasColumnName("roleID");
            entity.Property(e => e.RoleName)
                .HasMaxLength(50)
                .HasColumnName("roleName");
        });

        modelBuilder.Entity<TblStation>(entity =>
        {
            entity.HasKey(e => e.StationId).HasName("PRIMARY");

            entity.ToTable("tblStations");

            entity.Property(e => e.StationId).HasColumnName("stationID");
            entity.Property(e => e.Address)
                .HasMaxLength(200)
                .HasColumnName("address");
            entity.Property(e => e.IsActive)
                .HasDefaultValueSql("'1'")
                .HasColumnName("isActive");
            entity.Property(e => e.Phone)
                .HasMaxLength(15)
                .HasColumnName("phone");
            entity.Property(e => e.StationName)
                .HasMaxLength(100)
                .HasColumnName("stationName");
        });

        modelBuilder.Entity<TblTrip>(entity =>
        {
            entity.HasKey(e => e.TripId).HasName("PRIMARY");

            entity.ToTable("tblTrips");

            entity.HasIndex(e => e.Status, "idx_trips_status");

            entity.HasIndex(e => e.TripType, "idx_trips_tripType");

            entity.HasIndex(e => e.StaffCreated, "staffCreated");

            entity.HasIndex(e => e.TruckId, "truckID");

            entity.HasIndex(e => e.Departure, "idx_trips_departure");
            entity.HasIndex(e => e.Destination, "idx_trips_destination");

            entity.Property(e => e.TripId)
                .HasMaxLength(20)
                .HasColumnName("tripID");
            entity.Property(e => e.AssistantName)
                .HasMaxLength(100)
                .HasColumnName("assistantName");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.Departure)
                .HasMaxLength(100)
                .HasColumnName("departure");
            entity.Property(e => e.DepartureTime)
                .HasMaxLength(20)
                .HasColumnName("departureTime");
            entity.Property(e => e.Destination)
                .HasMaxLength(100)
                .HasColumnName("destination");
            entity.Property(e => e.DriverName)
                .HasMaxLength(100)
                .HasColumnName("driverName");
            entity.Property(e => e.Notes)
                .HasMaxLength(200)
                .HasColumnName("notes");
            entity.Property(e => e.ShiftId).HasColumnName("shiftID");
            entity.Property(e => e.StaffCreated)
                .HasMaxLength(20)
                .HasColumnName("staffCreated");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Đang đi'")
                .HasColumnName("status");
            entity.Property(e => e.TripType)
                .HasMaxLength(10)
                .HasDefaultValueSql("'depart'")
                .HasColumnName("tripType");
            entity.Property(e => e.TruckId)
                .HasMaxLength(20)
                .HasColumnName("truckID");

            entity.HasOne(d => d.StaffCreatedNavigation).WithMany(p => p.TblTrips)
                .HasForeignKey(d => d.StaffCreated)
                .HasConstraintName("tblTrips_ibfk_2");

            entity.HasOne(d => d.Truck).WithMany(p => p.TblTrips)
                .HasForeignKey(d => d.TruckId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblTrips_ibfk_1");
        });

        modelBuilder.Entity<TblTruck>(entity =>
        {
            entity.HasKey(e => e.TruckId).HasName("PRIMARY");

            entity.ToTable("tblTrucks");

            entity.Property(e => e.TruckId)
                .HasMaxLength(20)
                .HasColumnName("truckID");
            entity.Property(e => e.DriverName)
                .HasMaxLength(100)
                .HasColumnName("driverName");
            entity.Property(e => e.DriverPhone)
                .HasMaxLength(15)
                .HasColumnName("driverPhone");
            entity.Property(e => e.LicensePlate)
                .HasMaxLength(20)
                .HasColumnName("licensePlate");
            entity.Property(e => e.Notes)
                .HasMaxLength(200)
                .HasColumnName("notes");
            entity.Property(e => e.Status)
                .IsRequired()
                .HasDefaultValueSql("'1'")
                .HasColumnName("status");
        });

        modelBuilder.Entity<TblUser>(entity =>
        {
            entity.HasKey(e => e.UserId).HasName("PRIMARY");

            entity.ToTable("tblUsers");

            entity.HasIndex(e => e.RoleId, "roleID");

            entity.Property(e => e.UserId)
                .HasMaxLength(20)
                .HasColumnName("userID");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.Email)
                .HasMaxLength(100)
                .HasColumnName("email");
            entity.Property(e => e.FullName)
                .HasMaxLength(100)
                .HasColumnName("fullName");
            entity.Property(e => e.Password)
                .HasMaxLength(100)
                .HasColumnName("password");
            entity.Property(e => e.Phone)
                .HasMaxLength(15)
                .HasColumnName("phone");
entity.Property(e => e.Username)
    .HasMaxLength(50)
    .HasColumnName("username");

entity.Property(e => e.StationId)
    .HasColumnName("stationID");

entity.Property(e => e.RoleId)
    .HasMaxLength(5)
    .HasDefaultValueSql("'US'")
    .HasColumnName("roleID");

entity.Property(e => e.Status)
    .IsRequired()
    .HasDefaultValueSql("'1'")
    .HasColumnName("status");

entity.HasOne(d => d.Station).WithMany()
    .HasForeignKey(d => d.StationId)
    .HasConstraintName("FK_User_Station");

entity.HasOne(d => d.Role).WithMany(p => p.TblUsers)
    .HasForeignKey(d => d.RoleId)
    .OnDelete(DeleteBehavior.ClientSetNull)
    .HasConstraintName("tblUsers_ibfk_1");
        });

        modelBuilder.Entity<TblWorkShift>(entity =>
        {
            entity.HasKey(e => e.ShiftId).HasName("PRIMARY");

            entity.ToTable("tblWorkShifts");

            entity.HasIndex(e => new { e.StaffId, e.Status }, "idx_shift_staff_status");

            entity.Property(e => e.ShiftId).HasColumnName("shiftID");
            entity.Property(e => e.EndTime)
                .HasColumnType("datetime")
                .HasColumnName("endTime");
            entity.Property(e => e.StaffId)
                .HasMaxLength(50)
                .HasColumnName("staffID");
            entity.Property(e => e.StartTime)
                .HasColumnType("datetime")
                .HasColumnName("startTime");
            entity.Property(e => e.Status)
                .HasMaxLength(20)
                .HasDefaultValueSql("'ACTIVE'")
                .HasColumnName("status");
            
            entity.Property(e => e.TotalPrepaid)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("totalPrepaid");
            
            entity.Property(e => e.TotalCod)
                .HasColumnType("decimal(18, 2)")
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("totalCod");
            
            entity.Property(e => e.OrderCount)
                .HasColumnType("int")
                .HasDefaultValueSql("'0'")
                .HasColumnName("orderCount");

            entity.HasOne(d => d.Staff).WithMany(p => p.TblWorkShifts)
                .HasForeignKey(d => d.StaffId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("tblWorkShifts_ibfk_1");
        });

        modelBuilder.Entity<VwOrderList>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_OrderList");

            entity.Property(e => e.Amount)
                .HasPrecision(15, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("amount");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.Ct)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("ct");
            entity.Property(e => e.ItemName)
                .HasMaxLength(200)
                .HasColumnName("itemName");
            entity.Property(e => e.Note)
                .HasMaxLength(500)
                .HasColumnName("note");
            entity.Property(e => e.OrderId)
                .HasMaxLength(20)
                .HasColumnName("orderID");
            entity.Property(e => e.ReceiveDate)
                .HasMaxLength(30)
                .HasColumnName("receiveDate");
            entity.Property(e => e.ReceiveStation)
                .HasMaxLength(100)
                .HasColumnName("receiveStation");
            entity.Property(e => e.ReceiverName)
                .HasMaxLength(100)
                .HasColumnName("receiverName");
            entity.Property(e => e.ReceiverPhone)
                .HasMaxLength(15)
                .HasColumnName("receiverPhone");
            entity.Property(e => e.SendStation)
                .HasMaxLength(100)
                .HasColumnName("sendStation");
            entity.Property(e => e.SenderName)
                .HasMaxLength(100)
                .HasColumnName("senderName");
            entity.Property(e => e.SenderPhone)
                .HasMaxLength(15)
                .HasColumnName("senderPhone");
            entity.Property(e => e.ShipStatus)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Chưa Chuyển'")
                .HasColumnName("shipStatus");
            entity.Property(e => e.StaffInput)
                .HasMaxLength(20)
                .HasColumnName("staffInput");
            entity.Property(e => e.StaffInputName)
                .HasMaxLength(100)
                .HasColumnName("staffInputName");
            entity.Property(e => e.StaffReceive)
                .HasMaxLength(20)
                .HasColumnName("staffReceive");
            entity.Property(e => e.StaffReceiveName)
                .HasMaxLength(100)
                .HasColumnName("staffReceiveName");
            entity.Property(e => e.Tr)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("tr");
            entity.Property(e => e.TripId)
                .HasMaxLength(20)
                .HasColumnName("tripID");
        });

        modelBuilder.Entity<VwTripList>(entity =>
        {
            entity
                .HasNoKey()
                .ToView("vw_TripList");

            entity.Property(e => e.AssistantName)
                .HasMaxLength(100)
                .HasColumnName("assistantName");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("datetime")
                .HasColumnName("createdAt");
            entity.Property(e => e.Departure)
                .HasMaxLength(100)
                .HasColumnName("departure");
            entity.Property(e => e.DepartureTime)
                .HasMaxLength(20)
                .HasColumnName("departureTime");
            entity.Property(e => e.Destination)
                .HasMaxLength(100)
                .HasColumnName("destination");
            entity.Property(e => e.DriverName)
                .HasMaxLength(100)
                .HasColumnName("driverName");
            entity.Property(e => e.LicensePlate)
                .HasMaxLength(20)
                .HasColumnName("licensePlate");
            entity.Property(e => e.Notes)
                .HasMaxLength(200)
                .HasColumnName("notes");
            entity.Property(e => e.RouteInfo)
                .HasMaxLength(203)
                .HasDefaultValueSql("''")
                .HasColumnName("routeInfo");
            entity.Property(e => e.StaffCreated)
                .HasMaxLength(20)
                .HasColumnName("staffCreated");
            entity.Property(e => e.StaffCreatedName)
                .HasMaxLength(100)
                .HasColumnName("staffCreatedName");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Đang đi'")
                .HasColumnName("status");
            entity.Property(e => e.TripId)
                .HasMaxLength(20)
                .HasColumnName("tripID");
            entity.Property(e => e.TripType)
                .HasMaxLength(10)
                .HasDefaultValueSql("'depart'")
                .HasColumnName("tripType");
            entity.Property(e => e.TruckId)
                .HasMaxLength(20)
                .HasColumnName("truckID");
        });

        modelBuilder.Entity<TblShiftAccounting>(entity =>
        {
            entity.HasKey(e => e.ShiftId).HasName("PRIMARY");

            entity.ToTable("tblShiftAccounting");

            entity.Property(e => e.ShiftId)
                .ValueGeneratedNever()
                .HasColumnName("shiftID");
            entity.Property(e => e.AccountingNote)
                .HasMaxLength(500)
                .HasColumnName("accountingNote");
            entity.Property(e => e.ActualCash)
                .HasPrecision(18, 2)
                .HasColumnName("actualCash");
            entity.Property(e => e.Discrepancy)
                .HasPrecision(18, 2)
                .HasColumnName("discrepancy");
            entity.Property(e => e.Status)
                .HasDefaultValueSql("'0'")
                .HasColumnName("status");
            entity.Property(e => e.SystemCod)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("systemCod");
            entity.Property(e => e.SystemPrepaid)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("systemPrepaid");
            entity.Property(e => e.TotalSystem)
                .HasPrecision(18, 2)
                .HasDefaultValueSql("'0.00'")
                .HasColumnName("totalSystem");
            entity.Property(e => e.VerifiedAt)
                .HasColumnType("datetime")
                .HasColumnName("verifiedAt");
            entity.Property(e => e.VerifiedBy)
                .HasMaxLength(50)
                .HasColumnName("verifiedBy");

            entity.HasOne(d => d.Shift).WithOne(p => p.TblShiftAccounting)
                .HasForeignKey<TblShiftAccounting>(d => d.ShiftId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("FK_Accounting_Shift");

            entity.HasOne(d => d.VerifiedByNavigation).WithMany(p => p.TblShiftAccountings)
                .HasForeignKey(d => d.VerifiedBy)
                .HasConstraintName("FK_Accounting_Verifier");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
