using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblStation
{
    public int StationId { get; set; }

    public string StationName { get; set; } = null!;

    public string? Address { get; set; }

    public string? Phone { get; set; }

    public bool? IsActive { get; set; }
}
