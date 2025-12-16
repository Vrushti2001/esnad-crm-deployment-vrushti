using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace CustomerService_Esnad
{
    public class ApprovalAndFowardinglevel2 : IPlugin
    {
        // Change if you want a different logical name on the incident
        private const string NotificationFieldLogicalName = "new_notificationusersassignmentl1";
        private const string DepartmentManagerRoleName = "Esnad: Department Manager";
        private const string StaticTeamName = "Customer Service Management Team";
        private const int SafeMaxLength = 5000;

        public void Execute(IServiceProvider serviceProvider)
        {
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("ApprovalAndForwardingLevel2Plugin started.");

            try
            {
                // Expecting "Target" input parameter as EntityReference to incident (like your console app)
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("Target not provided or not an EntityReference. Exiting plugin.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"CaseId: {caseId}");

                // Retrieve the incident
                var caseEntity = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid"));
                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "<no title>";
                string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? string.Empty;
                tracing.Trace($"Incident loaded. Title='{caseTitle}', Ticket='{ticketNumber}'");

                // Resolve team by name
                var teamRef = GetTeamByName(service, StaticTeamName, tracing);
                if (teamRef == null)
                {
                    tracing.Trace($"Team '{StaticTeamName}' not found. Exiting plugin.");
                    return;
                }
                tracing.Trace($"Team resolved: {teamRef.Name} ({teamRef.Id})");

                // Get department managers in the team
                var managers = GetDepartmentManagersInTeam(service, teamRef.Id, tracing);
                if (managers == null || managers.Count == 0)
                {
                    tracing.Trace($"No users with role '{DepartmentManagerRoleName}' found in team '{teamRef.Name}'. Exiting.");
                    return;
                }

                tracing.Trace($"Found {managers.Count} Department Manager(s).");

                // Build activityparty (to) and recipient display list
                var toParties = new List<Entity>();
                var recipientDisplay = new List<string>();

                foreach (var u in managers)
                {
                    var uid = u.GetAttributeValue<Guid>("systemuserid");
                    var name = u.GetAttributeValue<string>("fullname") ?? string.Empty;
                    var email1 = u.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;

                    var ap = new Entity("activityparty");
                    ap["partyid"] = new EntityReference("systemuser", uid);
                    toParties.Add(ap);

                    if (!string.IsNullOrWhiteSpace(email1))
                        recipientDisplay.Add($"{name} <{email1}>");
                    else if (!string.IsNullOrWhiteSpace(name))
                        recipientDisplay.Add(name);
                    else
                        recipientDisplay.Add(uid.ToString());
                }

                string recipientsJoined = string.Join("; ", recipientDisplay);
                if (recipientsJoined.Length > SafeMaxLength)
                    recipientsJoined = recipientsJoined.Substring(0, SafeMaxLength);

                tracing.Trace($"Recipients string prepared (length={recipientsJoined.Length}).");

                // Update the incident field (wrapped in try/catch so email still sent on failure)
                try
                {
                    var incidentToUpdate = new Entity("incident", caseId);
                    incidentToUpdate[NotificationFieldLogicalName] = recipientsJoined;
                    service.Update(incidentToUpdate);
                    tracing.Trace($"Incident updated: field '{NotificationFieldLogicalName}' set.");
                }
                catch (Exception exUpd)
                {
                    tracing.Trace($"Warning: failed to update incident field '{NotificationFieldLogicalName}': {exUpd}");
                    // continue
                }

                // Determine 'from' user: prefer crmadmin by domain, else use initiating user
                var crmAdmin = GetCrmAdminUser(service, tracing);
                Guid fromUserId = Guid.Empty;
                if (crmAdmin != null)
                {
                    fromUserId = crmAdmin.Id;
                    tracing.Trace($"Using crmadmin as sender: {crmAdmin.GetAttributeValue<string>("internalemailaddress") ?? crmAdmin.Id.ToString()}");
                }
                else
                {
                    // fall back to initiating user in plugin context
                    fromUserId = context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId;
                    tracing.Trace($"crmadmin not found. Using initiating/user id as sender: {fromUserId}");
                }

                var fromParty = new Entity("activityparty");
                fromParty["partyid"] = new EntityReference("systemuser", fromUserId);

                // Build email body and create email record
                string orgUrl = GetOrgURL(service, tracing);
                string caseUrl = $"{orgUrl}{caseId}";
                string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";

                string body = $@"
<html>
  <body style='font-family:Segoe UI, Tahoma, sans-serif; font-size:14px;'>
    <div dir='rtl' style='text-align:right; margin-bottom:20px;'>
      <p>مع التحية والتقدير،</p>
      <p>نود إعلامكم بأن التذكرة التالية قد تجاوزت المدة المحددة في اتفاقية مستوى الخدمة (SLA):</p>
      <p>عنوان التذكرة:
        <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a>
      </p>
      <p>رقم التذكرة: {ticketNumber}</p>
    </div>
    <hr style='border:0; border-top:1px solid #ccc; margin:20px 0;' />
    <div dir='ltr' style='text-align:left; margin-top:20px;'>
      <p>With Regards and Appreciation,</p>
      <p>Ticket Title: <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
      <p>Ticket Number: {ticketNumber}</p>
      <p>Thank you,</p>
      <p><img src='{imageUrl}' alt='Logo' style='width:200px;' /></p>
    </div>
  </body>
</html>";

                var email = new Entity("email");
                email["subject"] = $"[SLA Escalation Level 1 - Department Manager] - Case {ticketNumber}";
                email["description"] = body;
                email["directioncode"] = true;
                email["from"] = new EntityCollection(new[] { fromParty });
                email["to"] = new EntityCollection(toParties);
                email["regardingobjectid"] = new EntityReference("incident", caseId);

                Guid emailId = service.Create(email);
                tracing.Trace($"Email created (id={emailId}).");

                // Send email
                var sendReq = new SendEmailRequest
                {
                    EmailId = emailId,
                    IssueSend = true,
                    TrackingToken = string.Empty
                };
                service.Execute(sendReq);
                tracing.Trace("Email sent successfully.");

                tracing.Trace("ApprovalAndForwardingLevel2Plugin completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Plugin exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in ApprovalAndForwardingLevel2Plugin.", ex);
            }
        }

        private static EntityReference GetTeamByName(IOrganizationService service, string teamName, ITracingService tracing)
        {
            var qe = new QueryExpression("team")
            {
                ColumnSet = new ColumnSet("teamid", "name"),
                Criteria = new FilterExpression()
            };
            qe.Criteria.AddCondition("name", ConditionOperator.Equal, teamName);

            var res = service.RetrieveMultiple(qe);
            if (res.Entities.Count == 0)
            {
                tracing.Trace($"GetTeamByName: Team '{teamName}' not found.");
                return null;
            }

            var t = res.Entities[0];
            return new EntityReference("team", t.Id) { Name = t.GetAttributeValue<string>("name") };
        }

        private static List<Entity> GetDepartmentManagersInTeam(IOrganizationService service, Guid teamId, ITracingService tracing)
        {
            // FetchXML matches previous behavior: users who are in the team and with role 'Esnad: Department Manager'
            var fetch = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid' />
    <attribute name='fullname' />
    <attribute name='internalemailaddress' />
    <filter>
      <condition attribute='accessmode' operator='eq' value='0' />
    </filter>
    <link-entity name='teammembership' from='systemuserid' to='systemuserid' link-type='inner'>
      <filter>
        <condition attribute='teamid' operator='eq' value='{teamId}' />
      </filter>
    </link-entity>
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
      <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
        <filter>
          <condition attribute='name' operator='eq' value='{System.Security.SecurityElement.Escape(DepartmentManagerRoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            var coll = service.RetrieveMultiple(new FetchExpression(fetch));
            tracing.Trace($"GetDepartmentManagersInTeam: fetched {coll.Entities.Count} user(s).");
            return coll.Entities.ToList();
        }

        private static Entity GetCrmAdminUser(IOrganizationService service, ITracingService tracing)
        {
            var q = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "internalemailaddress"),
                Criteria = new FilterExpression()
            };
            q.Criteria.AddCondition("domainname", ConditionOperator.Equal, "CRM-ESNAD\\crmadmin");
            q.Criteria.AddCondition("accessmode", ConditionOperator.Equal, 0);

            var res = service.RetrieveMultiple(q);
            if (res.Entities.Count == 0)
            {
                tracing.Trace("GetCrmAdminUser: crmadmin not found.");
                return null;
            }

            return res.Entities.First();
        }

        private static string GetOrgURL(IOrganizationService service, ITracingService tracing)
        {
            var q = new QueryExpression("new_environmentvariable")
            {
                ColumnSet = new ColumnSet("new_value"),
                Criteria = new FilterExpression()
            };
            q.Criteria.AddCondition("new_name", ConditionOperator.Equal, "OrgURL");

            var res = service.RetrieveMultiple(q);
            if (res.Entities.Count > 0)
            {
                var val = res.Entities[0].GetAttributeValue<string>("new_value");
                tracing.Trace($"GetOrgURL: found {val}");
                return val ?? string.Empty;
            }

            tracing.Trace("GetOrgURL: not found, returning empty string.");
            return string.Empty;
        }
    }
}
