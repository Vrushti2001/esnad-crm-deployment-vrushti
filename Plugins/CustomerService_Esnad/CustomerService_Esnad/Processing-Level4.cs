using System;
using System.Collections.Generic;
using System.Linq;
using System.Security;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Crm.Sdk.Messages;

namespace CustomerService_Esnad
{
    public class SLALevel4 : IPlugin
    {
        private const string NotificationFieldLogicalName = "new_notificationusersprocessingl3"; // change if required
        private const int SafeMaxLength = 5000;
        private const string StaticTeamName = "Customer Service Management Team";
        private const string DeptManagerRoleName = "Esnad: Department Manager";
        private const string SectorHeadRoleName = "Esnad: Sector Head";
        private const string CEORoleName = "Esnad: CEO";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("SLALevel4Plugin started.");

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

                var ownerRef = caseEntity.GetAttributeValue<EntityReference>("ownerid");
                if (ownerRef == null)
                {
                    tracing.Trace("Incident has no owner. Exiting.");
                    return;
                }

                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "(No Title)";
                string ticketNumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? "(No number)";

                tracing.Trace($"Incident loaded. Title='{caseTitle}', Ticket='{ticketNumber}', Owner='{ownerRef?.Name}' ({ownerRef?.LogicalName})");

                // Prefer crmadmin as sender
                var crmAdmin = GetCRMAdminUser(service);
                if (crmAdmin == null) tracing.Trace("crmadmin not found; will fall back to initiating/context user as sender.");

                string orgUrl = GetOrgURL(service, tracing);
                string caseUrl = $"{orgUrl}{caseId}";

                if (ownerRef.LogicalName == "team")
                {
                    var teamName = ownerRef.Name;
                    tracing.Trace($"Owner is team: {teamName}. Sending escalation emails for team.");
                    SendEmailToTeam(service, crmAdmin, caseId, caseTitle, ownerRef, ownerRef.Id, caseUrl, ticketNumber, tracing, teamName, context);
                }
                else if (ownerRef.LogicalName == "systemuser")
                {
                    tracing.Trace($"Owner is systemuser: {ownerRef.Id}. Retrieving user teams.");
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

                tracing.Trace("SLALevel4Plugin completed.");
            }
            catch (Exception ex)
            {
                tracing.Trace("Plugin exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in SLALevel4Plugin.", ex);
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
            // You can include Department Managers / Sector Heads by uncommenting the fetches below
            // var departmentManagers = GetDepartmentManagerInTeam(service, teamId, tracing);
            // var sectorHeads = GetSectorHeadInTeam(service, teamId, tracing);
            var ceos = GetCEOs(service, tracing);

            var toParties = new List<Entity>();
            var recipients = new List<string>();
            var addedUserIds = new HashSet<Guid>();

            // Example: add Department Managers (uncomment if required)
            // foreach (var m in departmentManagers)
            // {
            //     var userId = m.Id;
            //     if (!addedUserIds.Contains(userId))
            //     {
            //         toParties.Add(new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", userId) });
            //         var name = m.GetAttributeValue<string>("fullname") ?? m.Id.ToString();
            //         var email = m.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;
            //         if (!string.IsNullOrWhiteSpace(email)) recipients.Add($"{name} <{email}>");
            //         addedUserIds.Add(userId);
            //     }
            // }

            // Example: add Sector Heads (uncomment if required)
            // foreach (var s in sectorHeads)
            // {
            //     var userId = s.Id;
            //     if (!addedUserIds.Contains(userId))
            //     {
            //         toParties.Add(new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", userId) });
            //         var name = s.GetAttributeValue<string>("fullname") ?? s.Id.ToString();
            //         var email = s.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;
            //         if (!string.IsNullOrWhiteSpace(email)) recipients.Add($"{name} <{email}>");
            //         addedUserIds.Add(userId);
            //     }
            // }

            // Add all CEOs if found (only active users with email)
            if (ceos != null && ceos.Count > 0)
            {
                tracing.Trace($"Adding {ceos.Count} CEO(s) to recipients.");
                foreach (var ceo in ceos)
                {
                    var userId = ceo.Id;
                    if (addedUserIds.Contains(userId)) continue;

                    var emailAddr = ceo.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;
                    var fullName = ceo.GetAttributeValue<string>("fullname") ?? userId.ToString();

                    if (string.IsNullOrWhiteSpace(emailAddr))
                    {
                        tracing.Trace($"Skipping CEO {fullName} ({userId}) because email is empty.");
                        continue;
                    }

                    // Add to activityparty
                    toParties.Add(new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", userId) });
                    recipients.Add(!string.IsNullOrWhiteSpace(emailAddr) ? $"{fullName} <{emailAddr}>" : fullName);
                    addedUserIds.Add(userId);
                }
            }
            else
            {
                tracing.Trace("No CEOs found to add as recipients.");
            }

            // Build recipients string and store on incident (non-blocking)
            string recipientsJoined = string.Join("; ", recipients);
            if (recipientsJoined.Length > SafeMaxLength)
                recipientsJoined = recipientsJoined.Substring(0, SafeMaxLength);

            if (!string.IsNullOrWhiteSpace(recipientsJoined))
            {
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
            }

            // Determine sender (from) - prefer crmadmin, else initiating/context user
            Guid fromUserId = crmAdminUser != null ? crmAdminUser.Id : (context.InitiatingUserId != Guid.Empty ? context.InitiatingUserId : context.UserId);
            var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

            // Create and send email
            string subject = $"[Processing SLA Escalation Level 3] Case Breach Alert - {caseTitle}";
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
            tracing.Trace($"Email created for case ID {caseId}. ID: {emailId}");

            var sendRequest = new SendEmailRequest
            {
                EmailId = emailId,
                IssueSend = true,
                TrackingToken = string.Empty
            };
            service.Execute(sendRequest);
            tracing.Trace($"Email sent successfully for case ID {caseId}.");
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
          <condition attribute='name' operator='eq' value='{SecurityElement.Escape(DeptManagerRoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";
            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"GetDepartmentManagerInTeam: fetched {result.Entities.Count} user(s) for team {teamId}.");
            return result.Entities.ToList();
        }

        /// <summary>
        /// Returns all systemuser records that have the CEO role, are active, have an email, and are normal users (accessmode=0).
        /// </summary>
        private List<Entity> GetCEOs(IOrganizationService service, ITracingService tracing)
        {
            var fetchXml = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid' />
    <attribute name='internalemailaddress' />
    <attribute name='fullname' />
    <filter type='and'>
      <condition attribute='internalemailaddress' operator='not-null' />
      <condition attribute='accessmode' operator='eq' value='0' />
     
    </filter>
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
      <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
        <filter>
          <condition attribute='name' operator='eq' value='{SecurityElement.Escape(CEORoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";
            var result = service.RetrieveMultiple(new FetchExpression(fetchXml));
            tracing.Trace($"GetCEOs: fetched {result.Entities.Count} user(s) with role '{CEORoleName}'.");
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
    }
}
