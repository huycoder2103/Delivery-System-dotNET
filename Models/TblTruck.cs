using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblTruck
{
    public string TruckId { get; set; } = null!;

    public string LicensePlate { get; set; } = null!;

    public string? DriverName { get; set; }

    public string? DriverPhone { get; set; }

    public bool? Status { get; set; }

    public string? Notes { get; set; }

    public virtual ICollection<TblTrip> TblTrips { get; set; } = new List<TblTrip>();
}
