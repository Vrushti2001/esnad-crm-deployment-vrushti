using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace CustomerService_Esnad
{
    public class VerificationLevel2 : IPlugin
    {
        // change logical name if required
        private const string NotificationFieldLogicalName = "new_notificationusersverificationl1";
        private const string DepartmentManagerRoleName = "Esnad: Department Manager";
        private const int SafeMaxLength = 5000;
        private const string StaticTeamName = "Customer Service Management Team";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("VerificationLevel2 plugin started.");

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("Target not provided or not an EntityReference. Exiting plugin.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"CaseId: {caseId}");

                // Retrieve incident
                var caseEntity = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid"));
                if (caseEntity == null)
                {
                    tracing.Trace("Incident not found. Exiting.");
                    return;
                }

                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "(No Title)";
                string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? "(No number)";

                // Resolve owner. If you want to use static team instead, uncomment the next line:
                var ownerRef = GetTeamByName(service, StaticTeamName, tracing);
                //var ownerRef = caseEntity.GetAttributeValue<EntityReference>("ownerid");
                tracing.Trace($"Incident loaded. Title='{caseTitle}', Ticket='{ticketNumber}', Owner='{ownerRef?.Name}' ({ownerRef?.LogicalName})");

                // get crmadmin user if available
                var crmAdmin = GetCRMAdminUser(service);
                if (crmAdmin == null)
                    tracing.Trace("crmadmin not found - will fall back to initiating/context user as sender.");

                string orgUrl = GetOrgURL(service, tracing);
                string caseUrl = $"{orgUrl}{caseId}";

                if (ownerRef != null && ownerRef.LogicalName == "team")
                {
                    tracing.Trace("Owner is a team - sending to Department Manager(s) of that team.");
                    SendEmailToTeam(service, crmAdmin, caseId, caseTitle, ownerRef, ownerRef.Id, caseUrl, ticketNumber, tracing, ownerRef.Name, context);
                }
                else if (ownerRef != null && ownerRef.LogicalName == "systemuser")
                {
                    tracing.Trace("Owner is a systemuser - retrieving teams for user.");
                    var teams = GetUserTeams(service, ownerRef.Id, tracing);
                    tracing.Trace($"Found {teams.Count} team(s) for user.");

                    foreach (var t in teams)
                    {
                        var teamName = t.GetAttributeValue<string>("name");
                        tracing.Trace($"Processing team: {teamName} ({t.Id})");
                        SendEmailToTeam(service, crmAdmin, caseId, caseTitle, ownerRef, t.Id, caseUrl, ticketNumber, tracing, teamName, context);
                    }
                }
                else
                {
                    tracing.Trace("Owner missing or unknown type - no action taken.");
                }

                tracing.Trace("VerificationLevel2 plugin completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Plugin exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in VerificationLevel2 plugin.", ex);
            }
        }

        private void SendEmailToTeam(
            IOrganizationService service,
            Entity crmAdminUser,
            Guid caseId,
            string caseTitle,
            EntityReference ownerRef,
            Guid teamId,
            string caseUrl,
            string ticketNumber,
            ITracingService tracing,
            string teamName,
            IPluginExecutionContext context)
        {
            var users = GetDepartmentManagerInTeam(service, teamId, tracing);
            if (users == null || users.Count == 0)
            {
                tracing.Trace($"No Department Manager users found for team {teamId}. Skipping.");
                return;
            }

            var toParties = new List<Entity>();
            var recipientDisplay = new List<string>();

            foreach (var u in users)
            {
                var uid = u.GetAttributeValue<Guid>("systemuserid");
                var name = u.GetAttributeValue<string>("fullname") ?? string.Empty;
                var email1 = u.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;

                toParties.Add(new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", uid) });

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

            tracing.Trace($"Recipients built for team '{teamName}' ({recipientDisplay.Count}). Length={recipientsJoined.Length}");

            // Update incident field (non-blocking)
            try
            {
                var incidentUpdate = new Entity("incident", caseId);
                incidentUpdate[NotificationFieldLogicalName] = recipientsJoined;
                service.Update(incidentUpdate);
                tracing.Trace($"Incident updated: field '{NotificationFieldLogicalName}' set.");
            }
            catch (Exception exUpd)
            {
                tracing.Trace($"Warning: failed to update incident field '{NotificationFieldLogicalName}': {exUpd}");
            }

            // Determine sender (from) - prefer crmadmin, else fallback to initiating/context user
            Guid fromUserId = crmAdminUser != null ? crmAdminUser.Id : (context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId);
            var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

            // Build email
            string subject = $"[Verification SLA Escalation Level 1 - Department Manager] {teamName} - Case Breach Alert";
            string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";

            var email = new Entity("email")
            {
                ["subject"] = subject,
                ["description"] = $@"
<html>
  <body style='font-family:Segoe UI, Tahoma, sans-serif; font-size:14px;'>

    <!-- Arabic section -->
    <div dir='rtl' style='text-align:right; margin-bottom:20px;'>
      <p>مع التحية والتقدير،</p>
      <p>نود إعلامكم بأن التذكرة التالية قد تجاوزت المدة المحددة في اتفاقية مستوى الخدمة (SLA):</p>
      <p>عنوان التذكرة:
        <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a>
      </p>
      <p>المسؤول عنها: {teamName}</p>
      <p>رقم التذكرة: {ticketNumber}</p>
      <p>يرجى اتخاذ الإجراءات اللازمة حسب آلية التصعيد المعتمدة لضمان سرعة المعالجة.</p>
      <p>شكرًا لتعاونكم،</p>
      <p>مركز دعم المستثمرين لقطاع التعدين</p>
    </div>

    <hr style='border:0; border-top:1px solid #ccc; margin:20px 0;' />

    <!-- English section -->
    <div dir='ltr' style='text-align:left; margin-top:20px;'>
      <p>With Regards and Appreciation,</p>
      <p>We would like to inform you that the following ticket has exceeded the time frame specified in the Service Level Agreement (SLA):</p>
      <p>Ticket Title:
        <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a>
      </p>
      <p>Responsible Team: {teamName}</p>
      <p>Ticket Number: {ticketNumber}</p>
      <p>Please take the necessary actions according to the approved escalation procedure to ensure prompt handling.</p>
      <br/>
      <p>Thank you for your cooperation,</p>
      <p>Investor Support Center – Mining Sector</p>
      <p>
        <img src='{imageUrl}' alt='CRM Logo' style='width:200px; margin-bottom:10px;' />
      </p>
    </div>

  </body>
</html>",
                ["directioncode"] = true,
                ["from"] = new EntityCollection(new[] { fromParty }),
                ["to"] = new EntityCollection(toParties),
                ["regardingobjectid"] = new EntityReference("incident", caseId),
                ["statuscode"] = new OptionSetValue(1) // Draft
            };

            Guid emailId = service.Create(email);
            tracing.Trace($"Email created for team {teamName}. ID: {emailId}");

            var sendRequest = new SendEmailRequest
            {
                EmailId = emailId,
                IssueSend = true,
                TrackingToken = string.Empty
            };
            service.Execute(sendRequest);
            tracing.Trace($"Email sent to team {teamName} successfully.");
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
            tracing.Trace($"GetUserTeams: found {result.Entities.Count} team(s) for user {userId}.");
            return result.Entities.ToList();
        }

        private List<Entity> GetDepartmentManagerInTeam(IOrganizationService service, Guid teamId, ITracingService tracing)
        {
            var fetchXml = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid'/>
    <attribute name='internalemailaddress'/>
    <attribute name='fullname'/>
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
          <condition attribute='name' operator='eq' value='{SecurityElement.Escape(DepartmentManagerRoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"GetDepartmentManagerInTeam: fetched {result.Entities.Count} user(s) for team {teamId}.");
            return result.Entities.ToList();
        }

        private Entity GetCRMAdminUser(IOrganizationService service)
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

        private string GetOrgURL(IOrganizationService service, ITracingService tracing)
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

            tracing.Trace("GetOrgURL: not found - returning empty string.");
            return string.Empty;
        }

        private EntityReference GetTeamByName(IOrganizationService service, string teamName, ITracingService tracing)
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
    }
}
