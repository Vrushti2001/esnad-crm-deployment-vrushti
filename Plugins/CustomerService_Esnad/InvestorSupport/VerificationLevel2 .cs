using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;

namespace InvestorSupport
{
    public class VerificationLevel2 : IPlugin
    {
        private const string RecipientFieldOnIncident = "new_notificationusersverificationl2";
        private const string OrgUrlEnvName = "OrgURL";
        private const string RoleNameToFind = "KI: Relationship management manager";
        private static readonly Guid FixedTeamId = new Guid("D5F16E18-B4BF-F011-A42C-F76DBBE58AA4");
        private static readonly Guid RoleIdToFind = new Guid("DC3D719B-B8BF-F011-A42C-F76DBBE58AA4");//Dev KI: Relationship Management Officer
        // private static readonly Guid RoleIdToFind = new Guid("DC3D719B-B8BF-F011-A42C-F76DBBE58AA4");//Prod KI: Relationship Management Officer
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("VerificationLevel2: execution started.");

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("VerificationLevel2: Target not provided or not an EntityReference; exiting.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"VerificationLevel2: CaseId = {caseId}");

                var incident = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid", "new_rmname"));
                if (incident == null)
                {
                    tracing.Trace("VerificationLevel2: Incident not found; exiting.");
                    return;
                }

                string caseTitle = incident.GetAttributeValue<string>("title") ?? "(No Title)";
                string ticketNumber = incident.GetAttributeValue<string>("ticketnumber") ?? "(No Number)";
                string rmName = incident.GetAttributeValue<string>("new_rmname") ?? "";
                EntityReference ownerRef = incident.GetAttributeValue<EntityReference>("ownerid");

                tracing.Trace($"VerificationLevel2: Case title='{caseTitle}', ticket='{ticketNumber}', rmName='{rmName}'");

                // Find crmadmin user for 'from' party (fallback to initiating user)
                var crmAdmin = GetCrmAdminUser(service);
                Guid fromUserId = crmAdmin != null ? crmAdmin.Id : context.InitiatingUserId;
                tracing.Trace($"VerificationLevel2: using fromUserId = {fromUserId} (crmadmin present: {crmAdmin != null})");

                var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

                string orgUrl = GetOrgURLSafe(service, tracing);
                string caseUrl = string.IsNullOrEmpty(orgUrl) ? "" : $"{orgUrl}{caseId}";

                // Determine recipients (Department Managers in owner team(s) or if owner is team directly)
                var recipients = new List<Entity>();

                if (ownerRef != null && ownerRef.LogicalName == "team")
                {
                    tracing.Trace($"VerificationLevel2: Owner is team {ownerRef.Id}. Fetching department managers in team.");
                    recipients.AddRange(GetUsersByRoleInTeam(service, ownerRef.Id, RoleNameToFind, tracing));
                    
                }
                else if (ownerRef != null && ownerRef.LogicalName == "systemuser")
                {
                    tracing.Trace($"VerificationLevel2: Owner is user {ownerRef.Id}. Fetching user's teams.");
                    var teams = GetUserTeams(service, ownerRef.Id, tracing);
                    tracing.Trace($"VerificationLevel2: Found {teams.Count} teams for owner.");
                    foreach (var team in teams)
                    {   
                        recipients.AddRange(GetUsersByRoleInTeam(service, ownerRef.Id, RoleNameToFind, tracing));
                    }
                }
                else
                {
                    tracing.Trace("VerificationLevel2: Owner not present or unknown type; no recipients.");
                }

                // Deduplicate by id
                recipients = recipients.GroupBy(x => x.Id).Select(g => g.First()).ToList();

                if (recipients.Count == 0)
                {
                    tracing.Trace("VerificationLevel2: No recipients found. Updating incident and exiting.");
                    SafeUpdateIncidentRecipientField(service, caseId, "No recipients found", tracing);
                    return;
                }

                // Build activityparty list and recipient string
                var toParties = new List<Entity>();
                var recipientDisplayList = new List<string>();

                foreach (var u in recipients)
                {
                    toParties.Add(new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", u.Id) });
                    var name = u.GetAttributeValue<string>("fullname") ?? u.Id.ToString();
                    var email1 = u.GetAttributeValue<string>("internalemailaddress") ?? "";
                    recipientDisplayList.Add(string.IsNullOrWhiteSpace(email1) ? name : $"{name} <{email1}>");
                }

                string recipientsJoined = string.Join("; ", recipientDisplayList);
                const int maxLength = 3000;
                if (recipientsJoined.Length > maxLength) recipientsJoined = recipientsJoined.Substring(0, maxLength);

                // Update incident recipient field
                SafeUpdateIncidentRecipientField(service, caseId, recipientsJoined, tracing);

                // Build email body (escape values)
                string subject = $"[Verification SLA Escalation Level 2 - KI: Department Manager] - Case Breach Alert";
                string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";

                var description = $@"
<html>
  <body style='font-family:Segoe UI, Tahoma, sans-serif; font-size:14px;'>
    <div dir='rtl' style='text-align:right; margin-bottom:20px;'>
      <p>مع التحية والتقدير،</p>
      <p>نود إعلامكم بأن التذكرة التالية قد تجاوزت المدة المحددة في اتفاقية مستوى الخدمة (SLA):</p>
      <p>عنوان التذكرة: <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{SecurityElement.Escape(caseTitle)}</a></p>
      <p>المسؤول عنها: {SecurityElement.Escape(rmName)}</p>
      <p>رقم التذكرة: {SecurityElement.Escape(ticketNumber)}</p>
    </div>
    <hr style='border:0; border-top:1px solid #ccc; margin:20px 0;' />
    <div dir='ltr' style='text-align:left; margin-top:20px;'>
      <p>With Regards and Appreciation,</p>
      <p>Ticket Title: <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{SecurityElement.Escape(caseTitle)}</a></p>
      <p>Responsible: {SecurityElement.Escape(rmName)}</p>
      <p>Ticket Number: {SecurityElement.Escape(ticketNumber)}</p>
      <br/>
      <p>Thank you,</p>
      <p>Investor Support Center – Mining Sector</p>
      <p><img src='{imageUrl}' alt='Logo' style='width:200px;' /></p>
    </div>
  </body>
</html>";

                var email = new Entity("email")
                {
                    ["subject"] = subject,
                    ["description"] = description,
                    ["directioncode"] = true,
                    ["from"] = new EntityCollection(new[] { fromParty }),
                    ["to"] = new EntityCollection(toParties),
                    ["regardingobjectid"] = new EntityReference("incident", caseId)
                };

                Guid emailId = service.Create(email);
                tracing.Trace($"VerificationLevel2: Email created. Id = {emailId}");

                var sendRequest = new OrganizationRequest("SendEmail");
                sendRequest["EmailId"] = emailId;
                sendRequest["IssueSend"] = true;
                sendRequest["TrackingToken"] = "";
                service.Execute(sendRequest);

                tracing.Trace($"VerificationLevel2: Email sent successfully to {recipients.Count} recipient(s).");
            }
            catch (Exception ex)
            {
                tracing.Trace("VerificationLevel2: Exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in VerificationLevel2 plugin.", ex);
            }
            finally
            {
                tracing.Trace("VerificationLevel2: execution finished.");
            }
        }

        private void SafeUpdateIncidentRecipientField(IOrganizationService service, Guid incidentId, string recipientText, ITracingService tracing)
        {
            try
            {
                if (recipientText == null) recipientText = "";
                const int maxLength = 3000;
                if (recipientText.Length > maxLength) recipientText = recipientText.Substring(0, maxLength);

                var incidentToUpdate = new Entity("incident", incidentId);
                incidentToUpdate[RecipientFieldOnIncident] = recipientText;
                service.Update(incidentToUpdate);
                tracing.Trace($"VerificationLevel2: Incident field '{RecipientFieldOnIncident}' updated.");
            }
            catch (Exception uex)
            {
                tracing.Trace("VerificationLevel2: Failed to update incident recipient field: " + uex.ToString());
            }
        }

        private List<Entity> GetUserTeams(IOrganizationService service, Guid userId, ITracingService tracing)
        {
            var fetchXml = $@"
<fetch>
  <entity name='team'>
    <attribute name='name'/>
    <attribute name='teamid'/>
    <link-entity name='teammembership' from='teamid' to='teamid' intersect='true'>
      <filter>
        <condition attribute='systemuserid' operator='eq' value='{userId}'/>
      </filter>
    </link-entity>
  </entity>
</fetch>";
            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"VerificationLevel2: Found {result.Entities.Count} teams for user {userId}.");
            return result.Entities.ToList();
        }

        private List<Entity> GetDepartmentManagerInTeam(IOrganizationService service, Guid teamId, ITracingService tracing)
        {
            var fetchXml = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid'/>
    <attribute name='fullname'/>
    <attribute name='internalemailaddress'/>
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
          <condition attribute='name' operator='eq' value='KI: Department Manager' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";
            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"VerificationLevel2: Found {result.Entities.Count} Department Manager(s) in team {teamId}.");
            return result.Entities.ToList();
        }
        private List<Entity> GetUsersByRoleInTeam(IOrganizationService service, Guid teamId, string roleName, ITracingService tracing)
        {
     
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
        <condition attribute='teamid' operator='eq' value='{FixedTeamId}' />
      </filter>
    </link-entity>
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
      <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
        <filter>
          <condition attribute='name' operator='eq' value='{System.Security.SecurityElement.Escape(RoleNameToFind)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";
            var res = service.RetrieveMultiple(new FetchExpression(fetch));
            tracing.Trace($"GetUsersByRoleInTeam: found {res.Entities.Count} user(s) with role '{roleName}' in team {teamId}.");
            return res.Entities.ToList();
        }

        private Entity GetCrmAdminUser(IOrganizationService service)
        {
            var q = new QueryExpression("systemuser")
            {
                ColumnSet = new ColumnSet("systemuserid", "internalemailaddress", "fullname"),
                Criteria = new FilterExpression()
            };
            q.Criteria.AddCondition("domainname", ConditionOperator.Equal, "CRM-ESNAD\\crmadmin");
            q.Criteria.AddCondition("accessmode", ConditionOperator.Equal, 0);

            var res = service.RetrieveMultiple(q);
            return res.Entities.FirstOrDefault();
        }

        private string GetOrgURLSafe(IOrganizationService service, ITracingService tracing)
        {
            try
            {
                var q = new QueryExpression("new_environmentvariable")
                {
                    ColumnSet = new ColumnSet("new_value"),
                    Criteria = new FilterExpression()
                };
                q.Criteria.AddCondition("new_name", ConditionOperator.Equal, OrgUrlEnvName);
                var res = service.RetrieveMultiple(q);
                if (res.Entities.Count > 0) return res.Entities[0].GetAttributeValue<string>("new_value") ?? "";
            }
            catch (Exception ex)
            {
                tracing.Trace("VerificationLevel2: GetOrgURLSafe error: " + ex.ToString());
            }
            return "";
        }
    }
}
