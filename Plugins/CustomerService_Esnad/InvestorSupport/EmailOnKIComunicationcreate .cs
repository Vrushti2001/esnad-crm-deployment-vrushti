using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace InvestorSupport
{
    public class EmailOnKIComunicationcreate : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            tracing.Trace("smsOnKIComunicationcreate plugin started.");

            try
            {
                if (context.MessageName == null || context.MessageName.ToLower() != "create")
                    return;
                if (!context.InputParameters.Contains("Target"))
                    return;

                var KICommunications = (Entity)context.InputParameters["Target"];
                if (KICommunications == null || KICommunications.LogicalName != "new_keyinvestorscommunication")
                    return;

                // Read simple fields
                string ReferenceNumber = KICommunications.GetAttributeValue<string>("new_referencenumber") ?? string.Empty;

                // Try to get a link stored on the record; fallback to a default constructed link
            
                string baseLink = "https://feedback-dev.crm-esnad.com/KICommunication";
                string fullLink = $"{baseLink}?ref={Uri.EscapeDataString(ReferenceNumber ?? string.Empty)}";

                // Get account lookup
                EntityReference accountRef = KICommunications.GetAttributeValue<EntityReference>("new_investor");
                if (accountRef == null)
                {
                    tracing.Trace("No investor account lookup present on the record.");
                    // Log and exit gracefully
                   
                    return;
                }

                // Retrieve Account (name + lookup to RM)
                string investorName = string.Empty;
                string rmFullName = string.Empty;
                string rmEmail = string.Empty;
                string rmPhone = string.Empty;
                string accountEmail = null;

                try
                {
                    var account = service.Retrieve("account", accountRef.Id, new ColumnSet("name", "new_relationshipmanager", "emailaddress1", "telephone1", "telephone2"));
                    investorName = account.GetAttributeValue<string>("name") ?? string.Empty;
                    accountEmail = account.GetAttributeValue<string>("emailaddress1");

                    // Relationship Manager lookup
                    var rmRef = account.GetAttributeValue<EntityReference>("new_relationshipmanager");
                    if (rmRef != null)
                    {
                        var rm = service.Retrieve("systemuser", rmRef.Id, new ColumnSet("fullname", "internalemailaddress", "mobilephone"));
                        rmFullName = rm.GetAttributeValue<string>("fullname") ?? string.Empty;
                        rmEmail = rm.GetAttributeValue<string>("internalemailaddress") ?? string.Empty;
                        rmPhone = rm.GetAttributeValue<string>("mobilephone") ?? string.Empty;

                        tracing.Trace($"Investor Name: {investorName}");
                        tracing.Trace($"RM Name: {rmFullName}");
                        tracing.Trace($"RM Email: {rmEmail}");
                        tracing.Trace($"RM Mobile: {rmPhone}");
                    }
                }
                catch (Exception ex)
                {
                    tracing.Trace("Error retrieving account or RM: " + ex.Message);
                }

                // Determine 'From' user: crmadmin (preferred) or initiating user (fallback)
                var crmAdmin = GetCrmAdminUser(service);
                Guid fromUserId = crmAdmin != null ? crmAdmin.Id : context.InitiatingUserId;
                tracing.Trace($"smsOnKIComunicationcreate: using fromUserId = {fromUserId} (crmadmin present: {crmAdmin != null})");

                // Build email body (Arabic RTL) - the template uses RLE/PDF/RLM
                string emailBody = EmailTemplates.ForForKICommunicationsCreateCreate(ReferenceNumber, investorName, rmFullName, rmEmail, rmPhone, fullLink);

                // Compose subject (Arabic short subject)
                string subject = $"تقييم الخدمة — {ReferenceNumber}";

                // Send Email
                bool emailSent = false;
                string sendResult = string.Empty;

                if (!string.IsNullOrWhiteSpace(accountEmail))
                {
                    try
                    {
                        sendResult = SendEmailToAccount(service, fromUserId, accountRef, subject, emailBody, KICommunications.ToEntityReference(), tracing, accountEmail);
                        emailSent = sendResult.StartsWith("OK:");
                    }
                    catch (Exception ex)
                    {
                        tracing.Trace("SendEmailToAccount error: " + ex.Message);
                        sendResult = "Error sending email: " + ex.Message;
                    }
                }
                else
                {
                    // If no account email found, we still create an email entity addressed to the account (no addressused)
                    try
                    {
                        sendResult = SendEmailToAccount(service, fromUserId, accountRef, subject, emailBody, KICommunications.ToEntityReference(), tracing, null);
                        emailSent = sendResult.StartsWith("OK:");
                    }
                    catch (Exception ex)
                    {
                        tracing.Trace("SendEmailToAccount error (no emailaddress): " + ex.Message);
                        sendResult = "Error sending email (no emailaddress): " + ex.Message;
                    }
                }

               
             

                tracing.Trace($"Email process completed for ReferenceNumber {ReferenceNumber}. Result: {sendResult}");
            }
            catch (Exception ex)
            {
                tracing.Trace("Error in plugin: " + ex.Message);
                // Try to log the error into notification entity
             
            }
        }

        // -------------------- Helper Methods --------------------

        /// <summary>
        /// Attempts to find the system user 'CRM-ESNAD\crmadmin' with accessmode = 0 (full).
        /// Returns the first matching systemuser entity or null.
        /// </summary>
        private Entity GetCrmAdminUser(IOrganizationService service)
        {
            try
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
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// Create & send an Email activity where 'from' is a systemuser (fromUserId) and 'to' is the account
        /// If toAddress is provided it will be set as addressused on the to activityparty.
        /// The body is wrapped as HTML with dir='rtl' so email clients render RTL properly.
        /// </summary>
        private string SendEmailToAccount(IOrganizationService service, Guid fromUserId, EntityReference accountRef, string subject, string body, EntityReference regarding, ITracingService tracing, string toAddress)
        {
            try
            {
                // Build email entity
                var email = new Entity("email");
                email["subject"] = subject;

                // Build HTML body (preserve RTL marks) — replace newlines with <br/>
                string htmlBody = $"<div dir='rtl' style='font-family:Segoe UI, Tahoma, Arial; font-size:14px;'>{body.Replace("\r\n", "<br/>")}</div>";
                email["description"] = htmlBody;

                if (regarding != null)
                    email["regardingobjectid"] = regarding;

                // From party
                var fromParty = new Entity("activityparty");
                fromParty["partyid"] = new EntityReference("systemuser", fromUserId);

                // To party (account)
                var toParty = new Entity("activityparty");
                toParty["partyid"] = accountRef;
                if (!string.IsNullOrWhiteSpace(toAddress))
                {
                    toParty["addressused"] = toAddress;
                }

                email["from"] = new Entity[] { fromParty };
                email["to"] = new Entity[] { toParty };

                // Create email record
                var emailId = service.Create(email);
                tracing.Trace($"Email created: {emailId}");

                // Send the email using SendEmailRequest (IssueSend = true)
                var send = new SendEmailRequest
                {
                    EmailId = emailId,
                    IssueSend = true,
                    TrackingToken = string.Empty
                };

                var resp = (SendEmailResponse)service.Execute(send);

                tracing.Trace($"SendEmailRequest executed for EmailId: {emailId}");
                return $"OK: EmailId={emailId}";
            }
            catch (Exception ex)
            {
                tracing.Trace("Error creating/sending email: " + ex.Message);
                return "ERROR: " + ex.Message;
            }
        }

        

       

        // Small helpers
        private static string FirstNonEmpty(params string[] vals)
        {
            foreach (var v in vals)
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            return null;
        }

        private static string Truncate(string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        // -------------------- Email Template (Arabic, RTL) --------------------
        private static class EmailTemplates
        {
            private const string RLE = "\u202B"; // RTL Embedding
            private const string PDF = "\u202C"; // Pop Directional Formatting
            private const string RLM = "\u200F"; // RTL Mark

            public static string ForForKICommunicationsCreateCreate(string ReferenceNumber, string investorName, string rmFullName, string rmEmail, string rmPhone, string fullLink)
            {
                // Keeps directional marks intact; caller will wrap into HTML div dir='rtl'
                // Anchor text: "Please give your valuable feedback"
                var feedbackAnchor = $"<a href='{fullLink}' target='_blank' rel='noopener noreferrer'>Please give your valuable feedback</a>";

                return
                    $"{RLE}{RLM}شريكنا المستثمر {investorName}،{PDF}\r\n" +
                    $"{RLE}{RLM}حرصاً منا لرفع مستوى جودة الخدمة يسعدنا تقييمكم للخدمة المقدمة:{PDF}\r\n" +
                    $"{RLE}{RLM}{feedbackAnchor}{PDF}\r\n\r\n" +
                    $"{RLE}{RLM}نسعد بخدمتكم،{PDF}\r\n" +
                    $"{RLE}{RLM}مركز دعم كبار المستثمرين – قطاع التعدين{PDF}\r\n" +
                    $"{RLE}{RLM}{rmEmail} - {rmPhone} -{rmFullName}{PDF}";
            }
        }
    }
}
