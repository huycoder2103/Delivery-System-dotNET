using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblUser
{
    public string UserId { get; set; } = null!;

    public string FullName { get; set; } = null!;

    public string RoleId { get; set; } = null!;

    public string Password { get; set; } = null!;

    public string? Phone { get; set; }

    public string? Email { get; set; }

    public string? Username { get; set; }

    public int? StationId { get; set; }

    public bool? Status { get; set; }

    public DateTime? CreatedAt { get; set; }

    public virtual TblRole Role { get; set; } = null!;

    public virtual TblStation? Station { get; set; }

    public virtual ICollection<TblAnnouncement> TblAnnouncements { get; set; } = new List<TblAnnouncement>();

    public virtual ICollection<TblFeedback> TblFeedbacks { get; set; } = new List<TblFeedback>();

    public virtual ICollection<TblOrder> TblOrderStaffInputNavigations { get; set; } = new List<TblOrder>();

    public virtual ICollection<TblOrder> TblOrderStaffReceiveNavigations { get; set; } = new List<TblOrder>();

    public virtual ICollection<TblTrip> TblTrips { get; set; } = new List<TblTrip>();

    public virtual ICollection<TblWorkShift> TblWorkShifts { get; set; } = new List<TblWorkShift>();

    public virtual ICollection<TblShiftAccounting> TblShiftAccountings { get; set; } = new List<TblShiftAccounting>();
}
