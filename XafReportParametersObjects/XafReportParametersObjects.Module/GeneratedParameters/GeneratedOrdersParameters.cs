using System;
using System.Collections.Generic;
using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.DC;
using DevExpress.ExpressApp.ReportsV2;
using DevExpress.Xpo;
using System.ComponentModel;

namespace XafReportParametersObjects.Module.GeneratedParameters;

[DomainComponent]
public class GeneratedOrdersParameters : ReportParametersObjectBase
{
    public GeneratedOrdersParameters(IObjectSpaceCreator provider) : base(provider)
    {
    }

    public string CustomerName { get; set; } = string.Empty;
    public DateTime StartDate { get; set; }
    public decimal MinAmount { get; set; }

    protected override IObjectSpace CreateObjectSpace()
    {
        return objectSpaceCreator.CreateObjectSpace(typeof(XafReportParametersObjects.Module.BusinessObjects.Order));
    }

    public override CriteriaOperator GetCriteria()
    {
        var criteria = new List<CriteriaOperator>();

        if (!string.IsNullOrEmpty(CustomerName))
            criteria.Add(CriteriaOperator.Parse("Customer.Name = ?", CustomerName));
        if (StartDate != default)
            criteria.Add(CriteriaOperator.Parse("OrderDate >= ?", StartDate));
        if (MinAmount != default)
            criteria.Add(CriteriaOperator.Parse("Amount >= ?", MinAmount));

        return CriteriaOperator.And(criteria);
    }

    public override SortProperty[] GetSorting() => null;
}
