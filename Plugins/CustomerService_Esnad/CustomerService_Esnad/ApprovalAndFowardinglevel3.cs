using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace CustomerService_Esnad
{
    public class ApprovalAndFowardinglevel3 : IPlugin
    {
        // Change this logical name if you want to store recipients in a different field
        private const string NotificationFieldLogicalName = "new_notificationusersassignmentl2";
        private const string StaticTeamName = "Customer Service Management Team";
        private const string SectorHeadRoleName = "Esnad: Sector Head";
        private const int SafeMaxLength = 3800;

        public void Execute(IServiceProvider serviceProvider)
        {
            // Get services
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("ApprovalAndFowardinglevel3 started.");

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("Target not provided or not an EntityReference. Exiting plugin.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"Processing Case ID: {caseId}");

                // Retrieve case
                var caseEntity = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid"));
                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "Unknown";
                string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? string.Empty;

                // Resolve static team
                var ownerRef = GetTeamByName(service, StaticTeamName, tracing);
                if (ownerRef == null)
                {
                    tracing.Trace($"Team '{StaticTeamName}' not found. Exiting.");
                    return;
                }
                tracing.Trace($"Static team resolved: {ownerRef.Name} ({ownerRef.Id})");

                // Get crmadmin (preferred sender)
                var crmAdmin = GetCRMAdminUser(service);
                if (crmAdmin == null)
                {
                    tracing.Trace("crmadmin not found; will fall back to initiating user for sender.");
                }

                // Build from party (will set after deciding fromUserId)
                Guid fromUserId = crmAdmin != null ? crmAdmin.Id : (context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId);

                var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

                string orgUrl = GetOrgURL(service);
                string caseUrl = $"{orgUrl}{caseId}";

                // If owner is team, send to Sector Head(s)
                if (ownerRef.LogicalName == "team")
                {
                    tracing.Trace("Owner is static team. Retrieving Sector Head(s).");
                    SendEmailToTeam(service, crmAdmin, fromParty, caseId, caseTitle, ownerRef, ownerRef.Id, caseUrl, ticketNumber, tracing, ownerRef.Name, context);
                }
                else
                {
                    tracing.Trace("Owner is not a team — no action taken.");
                }

                tracing.Trace("ApprovalAndFowardinglevel3 completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Plugin exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in ApprovalAndFowardinglevel3 plugin.", ex);
            }
        }

        private void SendEmailToTeam(
            IOrganizationService service,
            Entity crmAdminUser,
            Entity fromParty,
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
            var users = GetSectorHeadInTeam(service, teamId, tracing);
            if (users == null || users.Count == 0)
            {
                tracing.Trace($"No Sector Head users found in team {teamId}. Exiting SendEmailToTeam.");
                return;
            }

            // Build to parties and recipient display strings
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

            tracing.Trace($"Recipients built ({recipientDisplay.Count}): length={recipientsJoined.Length}");

            // Update incident with recipients (safe, non-blocking)
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
                // continue to send mail
            }

            // Determine from user id (prefer crmAdmin, else initiating user or context user)
            Guid fromUserId = crmAdminUser != null ? crmAdminUser.Id : (context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId);
            var actualFromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

            // Create email
            string subject = $"[Approval and Forwarding SLA Escalation Level 2 - Customer Service Team - Sector Head] - Case Breach Alert";
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
      <p>المسؤول عنها: Customer Service Management Team</p>
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
      <p>Responsible Team: Customer Service Management Team</p>
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
                ["from"] = new EntityCollection(new[] { actualFromParty }),
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
          <condition attribute='name' operator='eq' value='{System.Security.SecurityElement.Escape(SectorHeadRoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"GetSectorHeadInTeam: fetched {result.Entities.Count} user(s).");
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

        private string GetOrgURL(IOrganizationService service)
        {
            var q = new QueryExpression("new_environmentvariable")
            {
                ColumnSet = new ColumnSet("new_value"),
                Criteria = new FilterExpression()
            };
            q.Criteria.AddCondition("new_name", ConditionOperator.Equal, "OrgURL");

            var res = service.RetrieveMultiple(q);
            if (res.Entities.Count > 0)
                return res.Entities[0].GetAttributeValue<string>("new_value") ?? string.Empty;

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
