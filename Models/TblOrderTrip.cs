using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblOrderTrip
{
    public int Id { get; set; }

    public string OrderId { get; set; } = null!;

    public string TripId { get; set; } = null!;

    public virtual TblOrder Order { get; set; } = null!;

    public virtual TblTrip Trip { get; set; } = null!;
}
