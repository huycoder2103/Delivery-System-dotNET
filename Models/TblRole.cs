using System;
using System.Collections.Generic;

namespace Delivery_System.Models;

public partial class TblRole
{
    public string RoleId { get; set; } = null!;

    public string RoleName { get; set; } = null!;

    public virtual ICollection<TblUser> TblUsers { get; set; } = new List<TblUser>();
}
