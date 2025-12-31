using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace CustomerService_Esnad
{
    public class SLALevel2 : IPlugin
    {
        // Change this logical name if you want to store recipients in a different field
        private const string NotificationFieldLogicalName = "new_notificationusersprocessingl1"; // <-- adjust if needed
        private const string DepartmentManagerRoleName = "Esnad: Department Manager";
        private const int SafeMaxLength = 5000;
        private static readonly Guid RoleIdToFind = new Guid("FDE06907-B53F-F011-AE53-D066006ED8F0");//Dev
       // private static readonly Guid RoleIdToFind = new Guid("98E0066B-FEBE-F011-A42B-C842AD1D2D99");//Prod

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("SLALevel2Plugin started.");

            try
            {
                // Expect Target as EntityReference to incident
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("Target not provided or not an EntityReference. Exiting plugin.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"Case ID from Target: {caseId}");

                // Retrieve the incident
                var caseEntity = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid"));
                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "Unknown";
                string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? string.Empty;
                EntityReference ownerRef = caseEntity.GetAttributeValue<EntityReference>("ownerid");
                tracing.Trace($"Loaded incident. Title='{caseTitle}', Ticket='{ticketNumber}', Owner='{ownerRef?.Name}' ({ownerRef?.LogicalName})");

                // Get crmadmin as sender (preferred)
                var crmAdmin = GetCRMAdminUser(service);
                if (crmAdmin == null)
                {
                    tracing.Trace("crmadmin not found; will fall back to initiating user.");
                }

                // Build fromParty later after selecting sending user
                string orgUrl = GetOrgURL(service, tracing);
                string caseUrl = $"{orgUrl}{caseId}";

                // If owner is team -> send to Department Managers in that team
                if (ownerRef != null && ownerRef.LogicalName == "team")
                {
                    tracing.Trace("Owner is a team. Sending email to Department Manager(s) of the team.");
                    SendEmailToTeam(service, crmAdmin, caseId, caseTitle, ownerRef, ownerRef.Id, caseUrl, ticketNumber, tracing, ownerRef.Name, context);
                }
                // If owner is systemuser -> find user's teams and send for each team
                else if (ownerRef != null && ownerRef.LogicalName == "systemuser")
                {
                    tracing.Trace("Owner is a systemuser. Retrieving teams for user: " + ownerRef.Id);
                    var teams = GetUserTeams(service, ownerRef.Id, tracing);
                    tracing.Trace($"Found {teams.Count} team(s) for user.");

                    foreach (var team in teams)
                    {
                        var teamName = team.GetAttributeValue<string>("name");
                        tracing.Trace($"Processing team: {teamName} ({team.Id})");
                        SendEmailToTeam(service, crmAdmin, caseId, caseTitle, ownerRef, team.Id, caseUrl, ticketNumber, tracing, teamName, context);
                    }
                }
                else
                {
                    tracing.Trace("Owner missing or of unexpected type. No email sent.");
                }

                tracing.Trace("SLALevel2Plugin completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Plugin exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in SLALevel2Plugin.", ex);
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
            //var users = GetDepartmentManagerInTeam(service, teamId, tracing);
            var users = GetUsersByRoleInTeam(service, teamId, RoleIdToFind, tracing);
            if (users == null || users.Count == 0)
            {
                tracing.Trace($"No Department Manager found in team {teamId}. Skipping.");
                return;
            }

            // Build to parties and recipients string
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

            tracing.Trace($"Recipients prepared for team '{teamName}' ({recipientDisplay.Count}): length={recipientsJoined.Length}");

            // Update incident with recipients (non-blocking)
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
                // continue to send email even if update fails
            }

            // Determine from user id: prefer crmAdmin, else initiating user or plugin user
            Guid fromUserId = crmAdminUser != null ? crmAdminUser.Id : (context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId);
            var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

            // Build email
            string subject = $"[Processing SLA Escalation Level 1 - Department Manager] {teamName} - Case Breach Alert";
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

            // Send the email
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
          <condition attribute='name' operator='eq' value='{DepartmentManagerRoleName}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"GetDepartmentManagerInTeam: fetched {result.Entities.Count} user(s).");
            return result.Entities.ToList();
        }
        private List<Entity> GetUsersByRoleInTeam(IOrganizationService service, Guid teamId, Guid roleId, ITracingService tracing)
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
      <filter>
        <condition attribute='roleid' operator='eq' value='{roleId}' />
      </filter>
    </link-entity>
  </entity>
</fetch>";

            var res = service.RetrieveMultiple(new FetchExpression(fetch));
            tracing.Trace($"GetUsersByRoleInTeam: found {res.Entities.Count} user(s) with roleId '{roleId}' in team {teamId}.");
            return res.Entities.ToList();
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
    }
}
