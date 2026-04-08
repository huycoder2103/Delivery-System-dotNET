using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class VwTripList
{
    public string TripId { get; set; } = null!;

    public string RouteInfo { get; set; } = null!;

    public string Departure { get; set; } = null!;

    public string Destination { get; set; } = null!;

    public string TruckId { get; set; } = null!;

    public string? LicensePlate { get; set; }

    public string DepartureTime { get; set; } = null!;

    public string? DriverName { get; set; }

    public string? AssistantName { get; set; }

    public string? Status { get; set; }

    public string TripType { get; set; } = null!;

    public string? StaffCreated { get; set; }

    public string? StaffCreatedName { get; set; }

    public string? Notes { get; set; }

    public DateTime? CreatedAt { get; set; }
}
