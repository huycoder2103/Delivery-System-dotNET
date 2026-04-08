using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblAnnouncement
{
    public int Id { get; set; }

    public string Title { get; set; } = null!;

    public string Content { get; set; } = null!;

    public string? CreatedBy { get; set; }

    public DateTime? CreatedAt { get; set; }

    public bool? IsActive { get; set; }

    public virtual TblUser? CreatedByNavigation { get; set; }
}
