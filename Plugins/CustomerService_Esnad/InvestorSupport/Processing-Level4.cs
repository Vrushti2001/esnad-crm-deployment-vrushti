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
        private const string CeoRoleName = "Esnad: CEO";
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

                // Get the CEO
                var ceo = GetCEO(service, tracing);
                if (ceo == null)
                {
                    tracing.Trace("SLALevel4: CEO not found. Updating incident field and exiting without sending email.");
                    SafeUpdateIncidentRecipientField(service, caseId, "No CEO found", tracing);
                    return;
                }

                // Compose recipient display string
                string ceoName = ceo.GetAttributeValue<string>("fullname") ?? ceo.Id.ToString();
                string ceoEmail = ceo.GetAttributeValue<string>("internalemailaddress") ?? "";
                string recipientDisplay = string.IsNullOrWhiteSpace(ceoEmail) ? ceoName : $"{ceoName} <{ceoEmail}>";

                tracing.Trace($"SLALevel4: CEO found: {recipientDisplay}");

                // Store recipient info on incident (truncate to CRM text limits)
                SafeUpdateIncidentRecipientField(service, caseId, recipientDisplay, tracing);

                // Build 'to' activityparty list — only CEO
                var toParties = new List<Entity>
                {
                    new Entity("activityparty") { ["partyid"] = new EntityReference("systemuser", ceo.Id) }
                };

                // Build email entity
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

                tracing.Trace("SLALevel4: Email sent successfully to CEO.");
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
        /// Returns the first user that has the CEO role (by role name constant). Null if not found.
        /// </summary>
        private Entity GetCEO(IOrganizationService service, ITracingService tracing)
        {
            // Fetch CEO by role name
            string fetch = $@"
<fetch top='1'>
  <entity name='systemuser'>
    <attribute name='systemuserid' />
    <attribute name='fullname' />
    <attribute name='internalemailaddress' />
    <link-entity name='systemuserroles' from='systemuserid' to='systemuserid' link-type='inner' alias='sur'>
      <link-entity name='role' from='roleid' to='roleid' link-type='inner' alias='r'>
        <filter>
          <condition attribute='name' operator='eq' value='{SecurityEscape(CeoRoleName)}' />
        </filter>
      </link-entity>
    </link-entity>
  </entity>
</fetch>";
            try
            {
                var coll = service.RetrieveMultiple(new FetchExpression(fetch));
                if (coll.Entities.Count > 0)
                {
                    tracing.Trace($"SLALevel4: GetCEO found {coll.Entities.Count} record(s).");
                    return coll.Entities.First();
                }
                tracing.Trace("SLALevel4: GetCEO found 0 records.");
                return null;
            }
            catch (Exception ex)
            {
                tracing.Trace("SLALevel4: GetCEO fetch error: " + ex.ToString());
                return null;
            }
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
