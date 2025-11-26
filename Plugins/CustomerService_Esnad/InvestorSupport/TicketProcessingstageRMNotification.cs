using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InvestorModule
{
    public class TicketProcessingstageRMNotification : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            IPluginExecutionContext service1 = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            ITracingService service2 = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IOrganizationService organizationService = ((IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory)))
                .CreateOrganizationService(new Guid?(((IExecutionContext)service1).UserId));

            service2.Trace("🔹 RM plugin started.", Array.Empty<object>());

            try
            {
                if (!((DataCollection<string, object>)service1.InputParameters).Contains("CaseId") ||
                    !(((DataCollection<string, object>)service1.InputParameters)["CaseId"] is EntityReference))
                {
                    service2.Trace("❌ 'CaseId' parameter missing or invalid.", Array.Empty<object>());
                    return;
                }

                EntityReference inputParameter = (EntityReference)((DataCollection<string, object>)service1.InputParameters)["CaseId"];
                service2.Trace($"CaseId received: {inputParameter.Id}", Array.Empty<object>());

                Entity caseEntity = organizationService.Retrieve("incident", inputParameter.Id, new ColumnSet(new string[]
                {
                    "ownerid",
                    "title",
                    "prioritycode",
                    "customerid",
                    "ticketnumber",
                    "new_companeyname"
                }));

                if (!caseEntity.Contains("ownerid"))
                {
                    service2.Trace("❌ Case owner not found.", Array.Empty<object>());
                    return;
                }

                string title = caseEntity.GetAttributeValue<string>("title") ?? "(No Title)";
                string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? " ";
                string companyName = caseEntity.GetAttributeValue<string>("new_companeyname") ?? "";
                string priority = caseEntity.FormattedValues.Contains("prioritycode") ? caseEntity.FormattedValues["prioritycode"] : "(No Priority)";

                // Fetch customer info
                GetCustomerOrAccountInfo(organizationService, caseEntity, service2);

                EntityReference ownerRef = caseEntity.GetAttributeValue<EntityReference>("ownerid");
                HashSet<Guid> adminUserIds = new HashSet<Guid>();

                if (ownerRef.LogicalName == "team")
                {
                    foreach (Entity entity in GetSpecializedAdminsInTeam(organizationService, ownerRef.Id, service2))
                        adminUserIds.Add(entity.Id);
                }
                else if (ownerRef.LogicalName == "systemuser")
                {
                    foreach (Guid teamId in GetUserTeams(organizationService, ownerRef.Id, service2))
                    {
                        foreach (Entity entity in GetSpecializedAdminsInTeam(organizationService, teamId, service2))
                            adminUserIds.Add(entity.Id);
                    }
                }

                if (!adminUserIds.Any())
                {
                    service2.Trace("⚠️ No specialized admin users found.", Array.Empty<object>());
                    return;
                }

                // Create "to" party list
                List<Entity> toParties = adminUserIds.Select(id => new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", id)
                }).ToList();

                // Find crmadmin user
                QueryExpression query = new QueryExpression("systemuser")
                {
                    ColumnSet = new ColumnSet("systemuserid", "internalemailaddress"),
                    Criteria = new FilterExpression()
                };
                query.Criteria.Conditions.Add(new ConditionExpression("domainname", ConditionOperator.Equal, "CRM-ESNAD\\crmadmin"));
                query.Criteria.Conditions.Add(new ConditionExpression("accessmode", ConditionOperator.Equal, 0));

                Entity crmAdmin = organizationService.RetrieveMultiple(query).Entities.FirstOrDefault();
                if (crmAdmin == null)
                {
                    service2.Trace("❌ crmadmin user not found.", Array.Empty<object>());
                    return;
                }

                string orgUrl = GetOrgURL(organizationService, service2);
                string logoUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";
                string ticketLink = $"<a href='{orgUrl}{inputParameter.Id}' style='color:#0078d4; font-weight:bold;'>{title}</a>";

                string emailBody = $@"
<html>
<body style='font-family:Arial, sans-serif;'>

    <!-- Arabic Section (Right-aligned) -->
    <div style='direction:rtl; text-align:right; font-family:Tahoma, sans-serif; margin-bottom:25px;'>
        <p>مدير العلاقة،</p>
        <p>تمت معالجة التذكرة رقم <b>{ticketNumber}</b> الخاصة بشركة <b>{companyName}</b> من قبل الإدارة المختصة.</p>
        <p><b>عنوان التذكرة:</b> {ticketLink}</p>
        <p>يرجى التحقق من الحل واغلاق التذكرة وفقاً لإتفاقية مستوى الخدمة (SLA) المعتمدة.</p>
    </div>

    <hr style='border:1px solid #ccc; margin:20px 0;' />

    <!-- English Section (Left-aligned) -->
    <div style='direction:ltr; text-align:left; margin-bottom:20px;'>
        <p>Dear Relationship Manager,</p>
        <p>The ticket <b>{ticketNumber}</b> for company <b>{companyName}</b> has been processed by the specialized department.</p>
        <p><b>Ticket:</b> {ticketLink}</p>
        <p>Please review and close the ticket in accordance with the approved Service Level Agreement (SLA).</p>
    </div>

    <div style='text-align:center; margin-top:30px;'>
        <img src='{logoUrl}' alt='CRM Logo' style='max-width:200px;' />
    </div>

</body>
</html>";

                // Create email
                Entity email = new Entity("email");
                email["subject"] = "Case Assigned on Processing: " + ticketNumber;
                email["description"] = emailBody;
                email["directioncode"] = true;

                // Correct "from" field setup
                email["from"] = new EntityCollection(new List<Entity>
                {
                    new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", crmAdmin.Id) }
                });

                email["to"] = new EntityCollection(toParties);
                email["regardingobjectid"] = new EntityReference("incident", inputParameter.Id);
                email["statuscode"] = new OptionSetValue(1);

                Guid emailId = organizationService.Create(email);
                service2.Trace($"📧 Email created with ID: {emailId}", Array.Empty<object>());

                // Send email
                OrganizationRequest sendRequest = new OrganizationRequest("SendEmail")
                {
                    ["EmailId"] = emailId,
                    ["IssueSend"] = true,
                    ["TrackingToken"] = ""
                };
                organizationService.Execute(sendRequest);

                service2.Trace("✅ Email sent successfully.", Array.Empty<object>());
            }
            catch (Exception ex)
            {
                service2.Trace($"💥 Error: {ex.Message} | {ex.StackTrace}", Array.Empty<object>());
                throw new InvalidPluginExecutionException("RM failed.", ex);
            }
        }

        private string GetCustomerOrAccountInfo(IOrganizationService service, Entity caseEntity, ITracingService tracing)
        {
            if (!caseEntity.Contains("customerid")) return "(No Customer Info)";
            EntityReference customerRef = caseEntity.GetAttributeValue<EntityReference>("customerid");
            tracing.Trace($"Fetching customer info from {customerRef.LogicalName}...", Array.Empty<object>());
            if (customerRef.LogicalName == "contact")
                return service.Retrieve("contact", customerRef.Id, new ColumnSet("emailaddress1")).GetAttributeValue<string>("emailaddress1") ?? "(No Email)";
            if (customerRef.LogicalName == "account")
                return service.Retrieve("account", customerRef.Id, new ColumnSet("name")).GetAttributeValue<string>("name") ?? "(No Name)";
            return "(Unknown Customer Type)";
        }

        private List<Guid> GetUserTeams(IOrganizationService service, Guid userId, ITracingService tracing)
        {
            tracing.Trace("Retrieving teams for user " + userId, Array.Empty<object>());
            QueryExpression query = new QueryExpression("teammembership")
            {
                ColumnSet = new ColumnSet("teamid"),
                Criteria = new FilterExpression()
            };
            query.Criteria.Conditions.Add(new ConditionExpression("systemuserid", ConditionOperator.Equal, userId));
            return service.RetrieveMultiple(query).Entities.Select(e => e.GetAttributeValue<Guid>("teamid")).ToList();
        }

        private List<Entity> GetSpecializedAdminsInTeam(IOrganizationService service, Guid teamId, ITracingService tracing)
        {
            string fetchXml = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid'/>
    <filter>
      <condition attribute='accessmode' operator='eq' value='0'/>
    </filter>
    <link-entity name='teammembership' from='systemuserid' to='systemuserid'>
      <filter>
        <condition attribute='teamid' operator='eq' value='{teamId}'/>
      </filter>
    </link-entity>
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid'>
      <link-entity name='role' from='roleid' to='roleid'>
        <filter>
          <condition attribute='name' operator='eq' value='KI: Relationship Manager'/>
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";
            EntityCollection users = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"Found {users.Entities.Count} specialized admins in team {teamId}", Array.Empty<object>());
            return users.Entities.ToList();
        }

        private string GetOrgURL(IOrganizationService service, ITracingService tracing)
        {
            tracing.Trace("Retrieving OrgURL from new_environmentvariable...", Array.Empty<object>());
            QueryExpression query = new QueryExpression("new_environmentvariable")
            {
                ColumnSet = new ColumnSet("new_value"),
                Criteria = new FilterExpression()
            };
            query.Criteria.Conditions.Add(new ConditionExpression("new_name", ConditionOperator.Equal, "OrgURL"));
            EntityCollection results = service.RetrieveMultiple(query);
            if (results.Entities.Count > 0) return results.Entities[0].GetAttributeValue<string>("new_value");
            throw new InvalidPluginExecutionException("No OrgURL found.");
        }
    }
}
