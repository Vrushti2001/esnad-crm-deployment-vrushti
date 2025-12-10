using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace InvestorSupport
{
    public class ApprovalAndFowardinglevel3 : IPlugin
    {
        // Adjust as needed
        private const string NotificationFieldLogicalName = "new_notificationusersassignmentl3";
        private const string RoleNameToFind = "KI: Sector Head";
        private const int SafeMaxRecipientsLength = 3800;

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("ApprovalAndFowardinglevel3 started.");

            try
            {
                // Expect "Target" as EntityReference pointing to incident
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("Target not provided or not an EntityReference. Exiting.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"Processing Case ID: {caseId}");

                // Retrieve incident fields we need
                var incident = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid", "new_rmname"));
                if (incident == null)
                {
                    tracing.Trace("Incident not found. Exiting.");
                    return;
                }

                var ownerRef = incident.GetAttributeValue<EntityReference>("ownerid");
                if (ownerRef == null)
                {
                    tracing.Trace("Incident owner missing. Exiting.");
                    return;
                }

                string caseTitle = incident.GetAttributeValue<string>("title") ?? "(No Title)";
                string ticketNumber = incident.GetAttributeValue<string>("ticketnumber") ?? "(No Number)";
                string rmName = incident.GetAttributeValue<string>("new_rmname") ?? string.Empty;

                tracing.Trace($"Incident loaded: Title='{caseTitle}', Ticket='{ticketNumber}', Owner='{ownerRef.Name}' ({ownerRef.LogicalName})");

                // Choose sender (prefer crmadmin)
                var crmAdmin = GetCrmAdminUser(service);
                Guid fromUserId = crmAdmin != null ? crmAdmin.Id : (context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId);
                tracing.Trace($"From user chosen: {fromUserId}");

                // Build recipients (activityparty) & readable recipient string
                var recipientsParties = new List<Entity>();
                var recipientsDisplay = new List<string>();

                if (ownerRef.LogicalName == "team")
                {
                    tracing.Trace("Owner is a team — fetching Sector Head role users in that team.");
                    var users = GetUsersByRoleInTeam(service, ownerRef.Id, RoleNameToFind, tracing);
                    AddUsersToRecipientLists(users, recipientsParties, recipientsDisplay);
                }
                else if (ownerRef.LogicalName == "systemuser")
                {
                    tracing.Trace("Owner is a user — fetching user's teams and gathering recipients.");
                    var teams = GetUserTeams(service, ownerRef.Id, tracing);
                    foreach (var team in teams)
                    {
                        var users = GetUsersByRoleInTeam(service, team.Id, RoleNameToFind, tracing);
                        AddUsersToRecipientLists(users, recipientsParties, recipientsDisplay);
                    }
                }
                else
                {
                    tracing.Trace($"Owner logical name '{ownerRef.LogicalName}' not handled.");
                }

                if (recipientsParties.Count == 0)
                {
                    tracing.Trace("No recipients found for escalation — exiting without sending email.");
                    return;
                }

                // Persist recipients list to incident field (non-blocking)
                string recipientsJoined = string.Join("; ", recipientsDisplay);
                if (recipientsJoined.Length > SafeMaxRecipientsLength)
                    recipientsJoined = recipientsJoined.Substring(0, SafeMaxRecipientsLength);

                try
                {
                    var updateIncident = new Entity("incident", caseId);
                    updateIncident[NotificationFieldLogicalName] = recipientsJoined;
                    service.Update(updateIncident);
                    tracing.Trace($"Incident updated: '{NotificationFieldLogicalName}' set.");
                }
                catch (Exception exUpd)
                {
                    tracing.Trace($"Warning: failed to update incident field '{NotificationFieldLogicalName}': {exUpd.Message}");
                    // continue to send email regardless
                }

                // Compose and send email
                var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };
                var email = new Entity("email")
                {
                    ["subject"] = $"[Assignment SLA Escalation Level 3 - KI: Sector Head] - Case Breach Alert",
                    ["description"] = BuildEmailBody(caseTitle, rmName, ticketNumber, GetOrgURLSafe(service, tracing), caseId),
                    ["directioncode"] = true,
                    ["from"] = new EntityCollection(new[] { fromParty }),
                    ["to"] = new EntityCollection(recipientsParties),
                    ["regardingobjectid"] = new EntityReference("incident", caseId),
                    ["statuscode"] = new OptionSetValue(1) // Draft
                };

                Guid emailId = service.Create(email);
                tracing.Trace($"Email created: {emailId}");

                var sendReq = new SendEmailRequest { EmailId = emailId, IssueSend = true, TrackingToken = string.Empty };
                service.Execute(sendReq);

                tracing.Trace("Email sent successfully.");
                tracing.Trace("ApprovalAndFowardinglevel3 completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Plugin error: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in ApprovalAndFowardinglevel3 plugin.", ex);
            }
        }

        private string BuildEmailBody(string caseTitle, string rmName, string ticketNumber, string orgUrl, Guid caseId)
        {
            string caseUrl = $"{orgUrl}{caseId}";
            string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";
            return $@"
<html>
  <body style='font-family:Segoe UI, Tahoma, sans-serif; font-size:14px;'>
    <div dir='rtl' style='text-align:right; margin-bottom:20px;'>
      <p>مع التحية والتقدير،</p>
      <p>نود إعلامكم بأن التذكرة التالية قد تجاوزت المدة المحددة في اتفاقية مستوى الخدمة (SLA):</p>
      <p>عنوان التذكرة: <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
      <p>المسؤول عنها: {SecurityElement.Escape(rmName)}</p>
      <p>رقم التذكرة: {SecurityElement.Escape(ticketNumber)}</p>
      <p>يرجى اتخاذ الإجراءات اللازمة حسب آلية التصعيد المعتمدة لضمان سرعة المعالجة.</p>
      <p>شكرًا لتعاونكم،</p>
      <p>مركز دعم كبار المستثمرين – قطاع التعدين</p>
    </div>
    <hr style='border:0; border-top:1px solid #ccc; margin:20px 0;' />
    <div dir='ltr' style='text-align:left; margin-top:20px;'>
      <p>With Regards and Appreciation,</p>
      <p>We would like to inform you that the following ticket has exceeded the time frame specified in the Service Level Agreement (SLA):</p>
      <p>Ticket Title: <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
      <p>Responsible: {SecurityElement.Escape(rmName)}</p>
      <p>Ticket Number: {SecurityElement.Escape(ticketNumber)}</p>
      <p>Please take the necessary actions according to the approved escalation procedure to ensure prompt handling.</p>
      <br/>
      <p>Thank you for your cooperation,</p>
      <p>Investor Support Center – Mining Sector</p>
      <p><img src='{imageUrl}' alt='CRM Logo' style='width:200px; margin-bottom:10px;' /></p>
    </div>
  </body>
</html>";
        }

        private void AddUsersToRecipientLists(IEnumerable<Entity> users, List<Entity> parties, List<string> recipientsDisplay)
        {
            foreach (var u in users)
            {
                var userId = u.Id;
                parties.Add(new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", userId) });

                var name = u.Contains("fullname") ? u.GetAttributeValue<string>("fullname") : null;
                var email = u.Contains("internalemailaddress") ? u.GetAttributeValue<string>("internalemailaddress") : null;

                if (!string.IsNullOrWhiteSpace(email) && !string.IsNullOrWhiteSpace(name))
                    recipientsDisplay.Add($"{name} <{email}>");
                else if (!string.IsNullOrWhiteSpace(name))
                    recipientsDisplay.Add(name);
                else
                    recipientsDisplay.Add(userId.ToString());
            }
        }

        private List<Entity> GetUserTeams(IOrganizationService service, Guid userId, ITracingService tracing)
        {
            var fetch = $@"
<fetch>
  <entity name='team'>
    <attribute name='teamid' />
    <attribute name='name' />
    <link-entity name='teammembership' from='teamid' to='teamid' intersect='true'>
      <filter>
        <condition attribute='systemuserid' operator='eq' value='{userId}' />
      </filter>
    </link-entity>
  </entity>
</fetch>";
            var res = service.RetrieveMultiple(new FetchExpression(fetch));
            tracing.Trace($"GetUserTeams returned {res.Entities.Count} teams for user {userId}.");
            return res.Entities.ToList();
        }

        private List<Entity> GetUsersByRoleInTeam(IOrganizationService service, Guid teamId, string roleName, ITracingService tracing)
        {
            var fetch = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid' />
    <attribute name='fullname' />
    <attribute name='internalemailaddress' />
    <link-entity name='teammembership' from='systemuserid' to='systemuserid' link-type='inner'>
      <filter>
        <condition attribute='teamid' operator='eq' value='{teamId}'/>
      </filter>
    </link-entity>
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
      <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
        <filter>
          <condition attribute='name' operator='eq' value='{SecurityElement.Escape(roleName)}' />
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
                ColumnSet = new ColumnSet("systemuserid", "internalemailaddress"),
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
                q.Criteria.AddCondition("new_name", ConditionOperator.Equal, "OrgURL");

                var res = service.RetrieveMultiple(q);
                if (res.Entities.Count > 0)
                {
                    var val = res.Entities[0].GetAttributeValue<string>("new_value");
                    tracing.Trace($"GetOrgURLSafe found: {val}");
                    return val ?? string.Empty;
                }
            }
            catch (Exception ex)
            {
                tracing.Trace("GetOrgURLSafe error: " + ex.Message);
            }
            tracing.Trace("GetOrgURLSafe returning empty string.");
            return string.Empty;
        }
    }
}
