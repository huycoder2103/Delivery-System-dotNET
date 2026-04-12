using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace Delivery_System.Models;

public partial class TblOrder
{
    [Key]
    public string OrderId { get; set; } = null!;

    [Required(ErrorMessage = "Tên hàng không được để trống")]
    [StringLength(200)]
    public string ItemName { get; set; } = null!;

    [Range(0, 1000000000, ErrorMessage = "Số tiền không hợp lệ")]
    public decimal? Amount { get; set; }

    [Required(ErrorMessage = "Tên người gửi không được để trống")]
    public string? SenderName { get; set; }

    [Required(ErrorMessage = "SĐT người gửi không được để trống")]
    [RegularExpression(@"^\d{10,11}$", ErrorMessage = "SĐT phải có 10-11 chữ số")]
    public string? SenderPhone { get; set; }

    public string? SendStation { get; set; }

    [Required(ErrorMessage = "Tên người nhận không được để trống")]
    public string? ReceiverName { get; set; }

    [Required(ErrorMessage = "SĐT người nhận không được để trống")]
    [RegularExpression(@"^\d{10,11}$", ErrorMessage = "SĐT phải có 10-11 chữ số")]
    public string? ReceiverPhone { get; set; }

    public string? ReceiveStation { get; set; }

    public string? StaffInput { get; set; }

    public string? StaffReceive { get; set; }

    public string? Tr { get; set; }

    public string? Ct { get; set; }

    public string? ReceiveDate { get; set; }

    public string? TripId { get; set; }

    public string? Note { get; set; }

    public string? ShipStatus { get; set; }

    public bool? IsDeleted { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? ShiftId { get; set; }

    public virtual TblUser? StaffInputNavigation { get; set; }

    public virtual TblUser? StaffReceiveNavigation { get; set; }

    public virtual ICollection<TblOrderTrip> TblOrderTrips { get; set; } = new List<TblOrderTrip>();
}
