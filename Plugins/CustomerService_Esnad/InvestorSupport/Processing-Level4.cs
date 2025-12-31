using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;

namespace InvestorSupport
{
    public class SLALevel4 : IPlugin
    {
        // Logical name of the incident text field where we'll store recipient info (change if needed)
        private const string RecipientFieldOnIncident = "new_notificationusersprocessingl4";
        // Role name that identifies CEO
        private const string CeoRoleName = "KI: CEO";
        private static readonly Guid RoleIdToFind = new Guid("DC3D719B-B8BF-F011-A42C-F76DBBE58AA4");//Dev
       // private static readonly Guid RoleIdToFind = new Guid("DC3D719B-B8BF-F011-A42C-F76DBBE58AA4");//Prod
       // Environment variable logical name to read Org URL (optional)
        private const string OrgUrlEnvName = "OrgURL";

        public void Execute(IServiceProvider serviceProvider)
        {
            // Context & services
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("SLALevel4: execution started.");

            try
            {
                // Expecting "Target" EntityReference (incident)
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    tracing.Trace("SLALevel4: Target not provided or not an EntityReference; exiting.");
                    return;
                }

                Guid caseId = caseRef.Id;
                tracing.Trace($"SLALevel4: CaseId = {caseId}");

                // Retrieve incident (include rm name if present)
                var incident = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "ownerid", "new_rmname"));
                if (incident == null)
                {
                    tracing.Trace("SLALevel4: Incident not found; exiting.");
                    return;
                }

                string caseTitle = incident.GetAttributeValue<string>("title") ?? "(No Title)";
                string ticketNumber = incident.GetAttributeValue<string>("ticketnumber") ?? "(No Number)";
                string rmName = incident.GetAttributeValue<string>("new_rmname") ?? "";
                EntityReference ownerRef = incident.GetAttributeValue<EntityReference>("ownerid");

                tracing.Trace($"SLALevel4: Case title='{caseTitle}', ticket='{ticketNumber}', rmName='{rmName}'");

                // Find crmadmin user for 'from' party (fallback to initiating user later)
                var crmAdmin = GetCrmAdminUser(service);
                Guid fromUserId = crmAdmin != null ? crmAdmin.Id : context.InitiatingUserId;
                tracing.Trace($"SLALevel4: using fromUserId = {fromUserId} (crmadmin present: {crmAdmin != null})");

                var fromParty = new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", fromUserId) };

                // Build case URL (if env var found)
                string orgUrl = GetOrgURLSafe(service, tracing);
                string caseUrl = string.IsNullOrEmpty(orgUrl) ? "" : $"{orgUrl}{caseId}";

                // Get all CEOs (active, have email, accessmode=0)
                //var ceos = GetCEOs(service, tracing);
                var ceos = GetUsersByRole(service,RoleIdToFind, tracing);
                if (ceos == null || ceos.Count == 0)
                {
                    tracing.Trace("SLALevel4: No CEO users found. Updating incident field and exiting without sending email.");
                    SafeUpdateIncidentRecipientField(service, caseId, "No CEO found", tracing);
                    return;
                }

                // Compose recipient display string for incident field and build 'to' list
                var toParties = new List<Entity>();
                var recipientDisplays = new List<string>();
                var addedUserIds = new HashSet<Guid>();

                foreach (var ceo in ceos)
                {
                    try
                    {
                        var userId = ceo.Id;
                        if (addedUserIds.Contains(userId)) continue;

                        var ceoName = ceo.GetAttributeValue<string>("fullname") ?? userId.ToString();
                        var ceoEmail = ceo.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;

                        if (string.IsNullOrWhiteSpace(ceoEmail))
                        {
                            tracing.Trace($"SLALevel4: Skipping CEO {ceoName} ({userId}) - email empty.");
                            continue;
                        }

                        toParties.Add(new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", userId) });
                        recipientDisplays.Add($"{ceoName} <{ceoEmail}>");
                        addedUserIds.Add(userId);
                    }
                    catch (Exception exInner)
                    {
                        tracing.Trace("SLALevel4: Error processing CEO record: " + exInner.ToString());
                        // continue with other CEOs
                    }
                }

                if (toParties.Count == 0)
                {
                    tracing.Trace("SLALevel4: No CEO recipients with email available after filtering. Updating incident field and exiting.");
                    SafeUpdateIncidentRecipientField(service, caseId, "No CEO with email found", tracing);
                    return;
                }

                // Build recipient display string and store it on incident (truncate to CRM text limits)
                string recipientsJoined = string.Join("; ", recipientDisplays);
                const int maxLen = 3000;
                if (recipientsJoined.Length > maxLen) recipientsJoined = recipientsJoined.Substring(0, maxLen);
                SafeUpdateIncidentRecipientField(service, caseId, recipientsJoined, tracing);

                // Build email entity (EMAIL BODY KEPT EXACTLY AS REQUESTED)
                string subject = $"[Processing SLA Escalation Level 4] Case Breach Alert - {caseTitle}";
                string imageUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";

                var description = $@"
<html>
  <body style='font-family:Segoe UI, Tahoma, sans-serif; font-size:14px;'>
    <div dir='rtl' style='text-align:right; margin-bottom:20px;'>
      <p>مع التحية والتقدير،</p>
      <p>نود إعلامكم بأن التذكرة التالية قد تجاوزت المدة المحددة في اتفاقية مستوى الخدمة (SLA):</p>
      <p>عنوان التذكرة: <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
      <p>المسؤول عنها: {System.Security.SecurityElement.Escape(rmName)}</p>
      <p>رقم التذكرة: {System.Security.SecurityElement.Escape(ticketNumber)}</p>
    </div>
    <hr style='border:0; border-top:1px solid #ccc; margin:20px 0;' />
    <div dir='ltr' style='text-align:left; margin-top:20px;'>
      <p>With Regards and Appreciation,</p>
      <p>Ticket Title: <a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{caseTitle}</a></p>
      <p>Responsible: {System.Security.SecurityElement.Escape(rmName)}</p>
      <p>Ticket Number: {System.Security.SecurityElement.Escape(ticketNumber)}</p>
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
                tracing.Trace($"SLALevel4: Email created. Id = {emailId}");

                // Send email
                var sendRequest = new OrganizationRequest("SendEmail");
                sendRequest["EmailId"] = emailId;
                sendRequest["IssueSend"] = true;
                sendRequest["TrackingToken"] = "";
                service.Execute(sendRequest);

                tracing.Trace("SLALevel4: Email sent successfully to CEOs.");
            }
            catch (Exception ex)
            {
                tracing.Trace("SLALevel4: Exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in SLALevel4 plugin.", ex);
            }
            finally
            {
                tracing.Trace("SLALevel4: execution finished.");
            }
        }

        /// <summary>
        /// Safe update of incident recipient field (text). Truncates to 3000 chars to be safe for single-line fields.
        /// </summary>
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
                tracing.Trace($"SLALevel4: Incident field '{RecipientFieldOnIncident}' updated with recipients.");
            }
            catch (Exception uex)
            {
                tracing.Trace("SLALevel4: Failed to update incident recipient field: " + uex.ToString());
                // Do not throw — we still want to attempt sending email if update fails.
            }
        }

        /// <summary>
        /// Finds the CRM user record for crmadmin (fallback sender).
        /// </summary>
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

        /// <summary>
        /// Returns all users that have the CEO role, are active, are normal users (accessmode=0), and have a non-empty email.
        /// </summary>
        private List<Entity> GetCEOs(IOrganizationService service, ITracingService tracing)
        {
            var fetchXml = $@"
<fetch>
  <entity name='systemuser'>
    <attribute name='systemuserid' />
    <attribute name='fullname' />
    <attribute name='internalemailaddress' />
    <filter type='and'>
      <condition attribute='internalemailaddress' operator='not-null' />
      <condition attribute='accessmode' operator='eq' value='0' />
      
    </filter>
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner'>
      <link-entity name='role' from='roleid' to='roleid' link-type='inner'>
        <filter>
          <condition attribute='name' operator='eq' value='{System.Security.SecurityElement.Escape(CeoRoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";

            try
            {
                var coll = service.RetrieveMultiple(new FetchExpression(fetchXml));
                tracing.Trace($"SLALevel4: GetUsersByRole fetched {coll.Entities.Count} record(s) for role '{CeoRoleName}'.");
                return coll.Entities.ToList();
            }
            catch (Exception ex)
            {
                tracing.Trace("SLALevel4: GetCEOs fetch error: " + ex.ToString());
                return new List<Entity>();
            }
        }
        private List<Entity> GetUsersByRole(
    IOrganizationService service,
    Guid roleId,
    ITracingService tracing)
        {
            var fetch = $@"
                    <fetch distinct='true'>
                      <entity name='systemuser'>
                        <attribute name='systemuserid' />
                        <attribute name='fullname' />
                        <attribute name='internalemailaddress' />
                        <link-entity name='systemuserroles'
                                     from='systemuserid'
                                     to='systemuserid'
                                     link-type='inner'>
                          <filter>
                            <condition attribute='roleid'
                                       operator='eq'
                                       value='{roleId}' />
                          </filter>
                        </link-entity>
                      </entity>
                    </fetch>";

            var res = service.RetrieveMultiple(new FetchExpression(fetch));
            tracing.Trace($"GetUsersByRole: found {res.Entities.Count} user(s) with roleId '{roleId}'.");
            return res.Entities.ToList();
        }

        /// <summary>
        /// Read Org URL env var if exists; return empty string on failure.
        /// </summary>
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
                if (res.Entities.Count > 0)
                {
                    return res.Entities[0].GetAttributeValue<string>("new_value") ?? "";
                }
            }
            catch (Exception ex)
            {
                tracing.Trace("SLALevel4: GetOrgURLSafe error: " + ex.ToString());
            }
            return "";
        }

        /// <summary>
        /// Very small helper to escape single quotes for fetch xml usage.
        /// </summary>
        private static string SecurityEscape(string input)
        {
            if (input == null) return "";
            return input.Replace("'", "''");
        }
    }
}
