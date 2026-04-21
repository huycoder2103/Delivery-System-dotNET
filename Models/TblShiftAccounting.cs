using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblShiftAccounting
{
    public int ShiftId { get; set; }

    public decimal SystemPrepaid { get; set; }

    public decimal SystemCod { get; set; }

    public decimal TotalSystem { get; set; }

    public decimal? ActualCash { get; set; }

    public decimal? Discrepancy { get; set; }

    public int Status { get; set; }

    public string? AccountingNote { get; set; }

    public string? VerifiedBy { get; set; }

    public DateTime? VerifiedAt { get; set; }

    public virtual TblWorkShift Shift { get; set; } = null!;

    public virtual TblUser? VerifiedByNavigation { get; set; }
}
