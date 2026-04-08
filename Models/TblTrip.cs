using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblTrip
{
    public string TripId { get; set; } = null!;

    public string TruckId { get; set; } = null!;

    public string Departure { get; set; } = null!;

    public string Destination { get; set; } = null!;

    public string DepartureTime { get; set; } = null!;

    public string? DriverName { get; set; }

    public string? AssistantName { get; set; }

    public string? Status { get; set; }

    public string TripType { get; set; } = null!;

    public string? StaffCreated { get; set; }

    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }

    public int? ShiftId { get; set; }

    public virtual TblUser? StaffCreatedNavigation { get; set; }

    public virtual ICollection<TblOrderTrip> TblOrderTrips { get; set; } = new List<TblOrderTrip>();

    public virtual TblTruck Truck { get; set; } = null!;
}
