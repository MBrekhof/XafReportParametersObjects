using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Xpo;
using XafReportParametersObjects.Module.BusinessObjects;

namespace XafReportParametersObjects.Module.Reports;

[DomainComponent]
public class OrdersReportParameters : ReportParametersObjectBase
{
    public OrdersReportParameters(IObjectSpaceCreator provider) : base(provider)
    {
        StartDate = DateTime.Today.AddMonths(-3);
        MinAmount = 0m;
    }

    public string CustomerName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public decimal MinAmount { get; set; }

    protected override IObjectSpace CreateObjectSpace()
    {
        return objectSpaceCreator.CreateObjectSpace(typeof(Order));
    }

    public override CriteriaOperator GetCriteria()
    {
        var criteria = new List<CriteriaOperator>();

        if (!string.IsNullOrWhiteSpace(CustomerName))
            criteria.Add(CriteriaOperator.Parse("Customer.Name = ?", CustomerName));

        if (StartDate != default)
            criteria.Add(CriteriaOperator.Parse("OrderDate >= ?", StartDate));

        if (MinAmount > 0m)
            criteria.Add(CriteriaOperator.Parse("Amount >= ?", MinAmount));

        return CriteriaOperator.And(criteria);
    }

    public override SortProperty[] GetSorting() => null;
}
