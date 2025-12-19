using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;

namespace CustomerService_Esnad
{
    public class ContactToCaseFieldMapperAsync : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            try
            {
                tracingService.Trace("Plugin execution started.");

                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity targetEntity))
                    return;

                if (targetEntity.LogicalName != "incident")
                    return;

                Guid caseId = targetEntity.Id;
                if (caseId == Guid.Empty)
                {
                    tracingService.Trace("Case ID is empty.");
                    return;
                }

                tracingService.Trace("Retrieving case with ID: " + caseId);
                Entity caseRecord = service.Retrieve("incident", caseId, new ColumnSet("customerid"));

                if (!caseRecord.Contains("customerid") || !(caseRecord["customerid"] is EntityReference customerRef))
                {
                    tracingService.Trace("Customer is missing or invalid.");
                    return;
                }

                Entity update = new Entity("incident", caseId);
                EntityReference companyLookup = null;

                if (customerRef.LogicalName == "contact")
                {
                    tracingService.Trace("Customer is a contact. Retrieving contact details...");
                    Entity contact = service.Retrieve("contact", customerRef.Id,
                        new ColumnSet("fullname", "emailaddress1", "mobilephone", "new_nationalidnumber", "parentcustomerid"));

                    string companyName = string.Empty;
                    string crNumber = string.Empty;

                    if (contact.Contains("parentcustomerid") && contact["parentcustomerid"] is EntityReference companyRef)
                    {
                        tracingService.Trace("Contact has a company lookup. Retrieving account...");
                        companyLookup = companyRef; // ✅ keep the lookup reference
                        // Retrieve both name and CR number from the account
                        Entity account = service.Retrieve("account", companyRef.Id, new ColumnSet("name", "new_crnumber"));

                        companyName = account.GetAttributeValue<string>("name") ?? string.Empty;
                        crNumber = account.GetAttributeValue<string>("new_crnumber") ?? string.Empty;

                        tracingService.Trace("Retrieved company name: " + companyName);
                        tracingService.Trace("Retrieved CR number: " + crNumber);
                    }
                    update["new_company"] = companyLookup;
                    update["new_customername"] = contact.GetAttributeValue<string>("fullname") ?? string.Empty;
                    update["new_email"] = contact.GetAttributeValue<string>("emailaddress1") ?? string.Empty;
                    update["new_phonenumber"] = contact.GetAttributeValue<string>("mobilephone") ?? string.Empty;
                    update["new_nationalidnumber"] = contact.GetAttributeValue<string>("new_nationalidnumber") ?? string.Empty;
                    update["new_companeyname"] = companyName;
                    update["new_crnumber"] = crNumber;     // Set CR number on the Case record
                }
                else if (customerRef.LogicalName == "account")
                {
                    tracingService.Trace("Customer is an account. Retrieving account details...");
                    Entity account = service.Retrieve("account", customerRef.Id,
                        new ColumnSet("name", "emailaddress1", "new_companyrepresentativephonenumber", "new_crnumber", "new_relationshipmanager"));
                    update["new_company"] = customerRef;
                    update["new_email"] = account.GetAttributeValue<string>("emailaddress1") ?? string.Empty;
                    update["new_phonenumber"] = account.GetAttributeValue<string>("new_companyrepresentativephonenumber") ?? string.Empty;
                    update["new_companeyname"] = account.GetAttributeValue<string>("name") ?? string.Empty;
                    update["new_crnumber"] = account.GetAttributeValue<string>("new_crnumber") ?? string.Empty;
                   
                    //if (!account.Attributes.Contains("new_relationshipmanager"))
                    //{
                         update["new_formtype"] = new OptionSetValue(0);
                    //}
                    //else
                    //{
                    //    EntityReference rmRef = account.GetAttributeValue<EntityReference>("new_relationshipmanager");
                    //    Entity rmUser = service.Retrieve("systemuser", rmRef.Id,
                    //                new ColumnSet("fullname", "internalemailaddress", "mobilephone"));
                    //    if (rmUser != null)
                    //    {
                    //        if (rmUser.Contains("fullname"))
                    //            update["new_rmname"] = rmUser.GetAttributeValue<string>("fullname");

                    //        if (rmUser.Contains("internalemailaddress"))
                    //            update["new_rmemail"] = rmUser.GetAttributeValue<string>("internalemailaddress");

                    //        if (rmUser.Contains("mobilephone"))
                    //            update["new_rmphonenumber"] = rmUser.GetAttributeValue<string>("mobilephone");

                    //        // If you want RM lookup on Incident too
                    //        update["new_tciketrelationshipmanager"] = rmRef;

                    //        update["new_formtype"] = new OptionSetValue(1);
                    //        update["prioritycode"] = new OptionSetValue(2);
                         
                    //    }

                    //}

                }
                else
                {
                    tracingService.Trace("Customer is neither contact nor account.");
                    return;
                }

                tracingService.Trace("Updating case with mapped fields...");
                service.Update(update);
                tracingService.Trace("Case updated successfully.");
            }
            catch (Exception ex)
            {
                // 🔇 Silent exception: just trace, do NOT throw
                if (tracingService != null)
                {
                    // tracingService.Trace("ContactToCaseFieldMapperAsync error: {0}", ex.ToString());
                    return;
                }

                // DO NOT rethrow – this keeps the operation from failing
                // Previously:
                // throw new InvalidPluginExecutionException("❌ Contact-to-Case plugin failed: " + ex.Message, ex);
            }
        }
    }
}
