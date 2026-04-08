using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class VwOrderList
{
    public string OrderId { get; set; } = null!;

    public string ItemName { get; set; } = null!;

    public decimal? Amount { get; set; }

    public string? SenderName { get; set; }

    public string? SenderPhone { get; set; }

    public string? SendStation { get; set; }

    public string? ReceiverName { get; set; }

    public string? ReceiverPhone { get; set; }

    public string? ReceiveStation { get; set; }

    public string? StaffInput { get; set; }

    public string? StaffInputName { get; set; }

    public string? StaffReceive { get; set; }

    public string? StaffReceiveName { get; set; }

    public string? Tr { get; set; }

    public string? Ct { get; set; }

    public string? ReceiveDate { get; set; }

    public string? TripId { get; set; }

    public string? Note { get; set; }

    public string? ShipStatus { get; set; }

    public DateTime? CreatedAt { get; set; }
}
