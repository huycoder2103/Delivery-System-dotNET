using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblWorkShift
{
    public int ShiftId { get; set; }

    public string StaffId { get; set; } = null!;

    public DateTime StartTime { get; set; }

    public DateTime? EndTime { get; set; }

    public string? Status { get; set; }

    public virtual TblUser Staff { get; set; } = null!;
}
