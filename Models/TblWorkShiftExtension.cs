using System.ComponentModel.DataAnnotations.Schema;

namespace Delivery_System.Models;

public partial class TblWorkShift
{
    [NotMapped]
    public decimal Revenue { get; set; }
}
