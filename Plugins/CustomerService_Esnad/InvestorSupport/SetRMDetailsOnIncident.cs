using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace InvestorSupport
{
    public class SetRMDetailsOnIncident : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = serviceFactory.CreateOrganizationService(context.UserId);

            if (context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity)
            {
                Entity incident = (Entity)context.InputParameters["Target"];

                // Check customer exists
                if (!incident.Attributes.Contains("customerid"))
                    return;

                EntityReference customerRef = incident.GetAttributeValue<EntityReference>("customerid");

                // Only proceed if Customer = Account
                if (customerRef.LogicalName != "account")
                    return;

                // Retrieve Account with RM Lookup
                Entity account = service.Retrieve("account", customerRef.Id,
                    new ColumnSet("new_relationshipmanager"));   // <-- RM USER LOOKUP FIELD
                var isKI = "Yes";
                if (!account.Attributes.Contains("new_relationshipmanager"))
                {
                    isKI = "No";
                    if (isKI == "No")
                    {
                        incident["new_formtype"] = new OptionSetValue(0);
                    }
                    service.Update(incident);

                    return;
                }
                    

                EntityReference rmRef = account.GetAttributeValue<EntityReference>("new_relationshipmanager");

                // Retrieve the USER (systemuser)
                Entity rmUser = service.Retrieve("systemuser", rmRef.Id,
                    new ColumnSet("fullname", "internalemailaddress", "mobilephone"));

                // Set RM data on Incident
                // Map the fields according to your incident schema
                if (rmUser != null)
                {
                    if (rmUser.Contains("fullname"))
                        incident["new_rmname"] = rmUser.GetAttributeValue<string>("fullname");

                    if (rmUser.Contains("internalemailaddress"))
                        incident["new_rmemail"] = rmUser.GetAttributeValue<string>("internalemailaddress");

                    if (rmUser.Contains("mobilephone"))
                        incident["new_rmphonenumber"] = rmUser.GetAttributeValue<string>("mobilephone");

                    // If you want RM lookup on Incident too
                    incident["new_tciketrelationshipmanager"] = rmRef;
                    
                    if(isKI == "Yes")
                    {
                       
                        incident["new_formtype"] = new OptionSetValue(1);
                        incident["prioritycode"] = new OptionSetValue(2);
                    }
                    service.Update(incident);

                }
            }
        }
    }
}
