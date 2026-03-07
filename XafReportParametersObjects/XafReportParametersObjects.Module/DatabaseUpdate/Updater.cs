using DevExpress.Data.Filtering;
using DevExpress.ExpressApp;
using DevExpress.ExpressApp.EF;
using DevExpress.ExpressApp.Updating;
using DevExpress.Persistent.Base;
using DevExpress.Persistent.BaseImpl.EF;
using Microsoft.Extensions.DependencyInjection;
using XafReportParametersObjects.Module.BusinessObjects;

namespace XafReportParametersObjects.Module.DatabaseUpdate
{
    // For more typical usage scenarios, be sure to check out https://docs.devexpress.com/eXpressAppFramework/DevExpress.ExpressApp.Updating.ModuleUpdater
    public class Updater : ModuleUpdater
    {
        public Updater(IObjectSpace objectSpace, Version currentDBVersion) :
            base(objectSpace, currentDBVersion)
        {
        }
        public override void UpdateDatabaseAfterUpdateSchema()
        {
            base.UpdateDatabaseAfterUpdateSchema();

            if (ObjectSpace.FirstOrDefault<Customer>(c => c.Name == "Acme Corp") is null)
            {
                var acme = ObjectSpace.CreateObject<Customer>();
                acme.Name = "Acme Corp";
                acme.Email = "info@acme.com";
                acme.City = "Amsterdam";

                var globex = ObjectSpace.CreateObject<Customer>();
                globex.Name = "Globex";
                globex.Email = "info@globex.com";
                globex.City = "Rotterdam";

                var order1 = ObjectSpace.CreateObject<Order>();
                order1.OrderNumber = "ORD-001";
                order1.OrderDate = new DateTime(2026, 1, 15);
                order1.Amount = 1500m;
                order1.Customer = acme;
                order1.Description = "Widget batch";

                var order2 = ObjectSpace.CreateObject<Order>();
                order2.OrderNumber = "ORD-002";
                order2.OrderDate = new DateTime(2026, 2, 20);
                order2.Amount = 3200m;
                order2.Customer = globex;
                order2.Description = "Gadget shipment";

                var order3 = ObjectSpace.CreateObject<Order>();
                order3.OrderNumber = "ORD-003";
                order3.OrderDate = new DateTime(2026, 3, 1);
                order3.Amount = 750m;
                order3.Customer = acme;
                order3.Description = "Spare parts";

                ObjectSpace.CommitChanges();
            }
        }
        public override void UpdateDatabaseBeforeUpdateSchema()
        {
            base.UpdateDatabaseBeforeUpdateSchema();
        }
    }
}
