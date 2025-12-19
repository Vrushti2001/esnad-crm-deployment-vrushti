using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;


namespace CustomerService_Esnad
{
    public class SyncContactCompanyToIncidents : IPlugin
    {
       
            public void Execute(IServiceProvider serviceProvider)
            {
                var context = (IPluginExecutionContext)
                    serviceProvider.GetService(typeof(IPluginExecutionContext));

                var service = ((IOrganizationServiceFactory)
                    serviceProvider.GetService(typeof(IOrganizationServiceFactory)))
                    .CreateOrganizationService(context.UserId);

                if (!context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity target))
                    return;

                if (target.LogicalName != "contact")
                    return;

                EntityReference companyRef = null;

                if (target.Contains("parentcustomerid"))
                    companyRef = target.GetAttributeValue<EntityReference>("parentcustomerid");
                else if (context.PreEntityImages.Contains("PreImage"))
                    companyRef = context.PreEntityImages["PreImage"]
                        .GetAttributeValue<EntityReference>("parentcustomerid");

                if (companyRef == null)
                    return;

                Guid contactId = target.Id;

                QueryExpression query = new QueryExpression("incident")
                {
                    ColumnSet = new ColumnSet("incidentid", "statecode"),
                    Criteria =
            {
                Conditions =
                {
                    new ConditionExpression("customerid", ConditionOperator.Equal, contactId),
                    new ConditionExpression("statecode", ConditionOperator.NotEqual, 1)
                }
            }
                };

                foreach (var inc in service.RetrieveMultiple(query).Entities)
                {
                    var upd = new Entity("incident", inc.Id);
                    upd["new_company"] = new EntityReference("account", companyRef.Id);
                    service.Update(upd);
                }
            }
        }

    }

