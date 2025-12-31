using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace CustomerService_Esnad
{
    public class VerificationLevel3 : IPlugin
    {
        private const string NotificationFieldLogicalName = "new_notificationusersverificationl2"; // change if required
        private const string SectorHeadRoleName = "Esnad: Sector Head";
        private const int SafeMaxLength = 5000;
        private const string StaticTeamName = "Customer Experience Management Team";
        private static readonly Guid StaticTeamId = new Guid("2B5DFFC5-A573-F011-A40D-C0B1F6211923");//Dev
         // private static readonly Guid StaticTeamId = new Guid("2B5DFFC5-A573-F011-A40D-C0B1F6211923");//Prod
        private static readonly Guid RoleIdToFind = new Guid("9E029863-C33F-F011-AE53-D066006ED8F0");//Dev
         // private static readonly Guid RoleIdToFind = new Guid("98E0066B-FEBE-F011-A42B-C842AD1D2D99");//Prod

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("VerificationLevel3Plugin started.");

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("Target not provided or not an EntityReference. Exiting plugin.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"Processing Case ID: {caseId}");

                var caseEntity = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid"));
                if (caseEntity == null)
                {
                    tracing.Trace("Incident not found. Exiting.");
                    return;
                }

                // Ensure owner exists (you previously used a static team; keep both patterns available)
                // var ownerRef = caseEntity.GetAttributeValue<EntityReference>("ownerid");
                //var ownerRef = GetTeamByName(service, StaticTeamName, tracing);
                //if (ownerRef == null)
                //{
                //    tracing.Trace("Incident has no owner. Exiting.");
                //    return;
                //}

                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "(No Title)";
                string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? "(No number)";

               // tracing.Trace($"Incident loaded. Title='{caseTitle}', Ticket='{ticketNumber}', Owner='{ownerRef.Name}' ({ownerRef.LogicalName})");
                var ownerRef = new EntityReference("team", StaticTeamId);
                tracing.Trace($"Using static TeamId: {StaticTeamId}");
                // Try to find crmadmin user to act as sender; fallback to initiating/context user later
                var crmAdmin = GetCRMAdminUser(service);
                if (crmAdmin == null) tracing.Trace("crmadmin not found; will fall back to initiating/context user as sender.");

                string orgUrl = GetOrgURL(service, tracing);
                string caseUrl = $"{orgUrl}{caseId}";

                // If owner is a team → send to Sector Head(s) of that team
                if (ownerRef.LogicalName == "team")
                {
                    tracing.Trace("Owner is a Team - sending to Sector Head(s) of that team.");
                    SendEmailToTeam(service, crmAdmin, caseId, caseTitle, ownerRef, ownerRef.Id, caseUrl, ticketNumber, tracing, ownerRef.Name, context);
                }
                // If owner is a user → find all teams that user belongs to and send for each team
                else if (ownerRef.LogicalName == "systemuser")
                {
                    tracing.Trace("Owner is a SystemUser - retrieving user's teams.");
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
                    tracing.Trace("Owner type not supported - no action taken.");
                }

                tracing.Trace("VerificationLevel3Plugin completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Plugin exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in VerificationLevel3Plugin.", ex);
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
            var users = GetUsersByRoleInTeam(service, teamId, RoleIdToFind, tracing);
            if (users == null || users.Count == 0)
            {
                tracing.Trace($"No Sector Head users found in team {teamId}. Skipping.");
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

            tracing.Trace($"Recipients prepared for team '{teamName}' ({recipientDisplay.Count}). Length={recipientsJoined.Length}");

            // Update incident field (non-blocking) — store recipient display list
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
                // continue to send email
            }

            // Determine sender: prefer crmadmin, else initiating user, else context user
            Guid fromUserId = crmAdminUser != null ? crmAdminUser.Id : (context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId);
            var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

            // Create email
            string subject = $"[Verification SLA Escalation Level 2 - Sector Head] {teamName} - Case Breach Alert";
            string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";

            var email = new Entity("email")
            {
                ["subject"] = subject,
                ["description"] = $@"
<html>
  <body style='font-family:Segoe UI, Tahoma, sans-serif; font-size:14px;'>
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

            // Send email
            var sendReq = new SendEmailRequest
            {
                EmailId = emailId,
                IssueSend = true,
                TrackingToken = string.Empty
            };
            service.Execute(sendReq);
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

        private List<Entity> GetSectorHeadInTeam(IOrganizationService service, Guid teamId, ITracingService tracing)
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
          <condition attribute='name' operator='eq' value='{SecurityElement.Escape(SectorHeadRoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"GetSectorHeadInTeam: fetched {result.Entities.Count} user(s) for team {teamId}.");
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
