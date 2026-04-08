using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblFeedback
{
    public int FeedbackId { get; set; }

    public string? UserId { get; set; }

    public string Content { get; set; } = null!;

    public DateTime? CreatedAt { get; set; }

    public virtual TblUser? User { get; set; }
}
