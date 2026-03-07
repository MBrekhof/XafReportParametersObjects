using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using System.ComponentModel.DataAnnotations;

namespace XafReportParametersObjects.Module.BusinessObjects;

[DefaultClassOptions]
[NavigationItem("SampleData")]
public class Order : BaseObject
{
    [Required]
    public virtual string OrderNumber { get; set; } = string.Empty;

    public virtual DateTime OrderDate { get; set; } = DateTime.Today;

    public virtual decimal Amount { get; set; }

    public virtual Customer? Customer { get; set; }

    public virtual string Description { get; set; } = string.Empty;
}
