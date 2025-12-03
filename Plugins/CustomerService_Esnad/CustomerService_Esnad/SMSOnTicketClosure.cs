using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace CustomerService_Esnad
{
    public class SMSOnTicketClosure : IPlugin
    {
        // 🔹 SMS Gateway configuration
        private const string SmsBaseUrl = "https://api.oursms.com/api-a/msgs";
        private const string SmsUsername = "Taadeen2.0";
        private const string SmsToken = "7sgOnsFhAuYdNgg5a3R4";
        private const string SmsSender = "Taadeen";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            tracing.Trace("=== SMSOnTicketClosure START ===");

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference caseRef))
                {
                    CreateErrorLog(service, "Ticket Closure", "Target parameter missing or invalid.", null, null);
                    return;
                }

                Guid caseId = caseRef.Id;

                // Retrieve the incident record
                var incident = service.Retrieve("incident", caseId, new ColumnSet("ticketnumber", "customerid", "new_formtype"));
                if (incident == null)
                {
                    CreateErrorLog(service, "Ticket Closure", "Incident not found with provided ID.", caseId, null);
                    return;
                }
                if (incident.Contains("new_formtype") && ((OptionSetValue)incident["new_formtype"]).Value == 1)
                    return;

                string ticket = incident.GetAttributeValue<string>("ticketnumber");
                var customerRef = incident.GetAttributeValue<EntityReference>("customerid");

                if (string.IsNullOrWhiteSpace(ticket))
                {
                    CreateErrorLog(service, "Ticket Closure", "Ticket number missing. Cannot send SMS.", caseId, customerRef);
                    return;
                }

                if (customerRef == null)
                {
                    CreateErrorLog(service, "Ticket Closure", "Customer reference missing on Incident.", caseId, null);
                    return;
                }

                // 🔹 Resolve phone number
                string phone = ResolvePhone(customerRef, service);
                if (string.IsNullOrWhiteSpace(phone))
                {
                    CreateErrorLog(service, "Ticket Closure", "No valid phone number found for customer.", caseId, customerRef);
                    return;
                }

                // 🔹 Get Feedback URL
                string feedbackBaseUrl = GetConfigValue(service, "FeedbackBaseUrl") ?? "https://feedback.crm-esnad.com";

                // 🔹 Prepare SMS message
                string smsBody = SmsTemplates.ForTicketClosure(ticket, feedbackBaseUrl);

                // 🔹 Send SMS
                bool sent = SendSms(service, tracing, phone, smsBody, caseId, customerRef);

                // 🔹 Log SMS notification
                CreateSmsNote(service, "Ticket Closure", smsBody, caseId, customerRef, sent);
            }
            catch (Exception ex)
            {
                // Final catch: no exception thrown outside
                CreateErrorLog(service, "Ticket Closure", ex.Message, null, null);
            }

            tracing.Trace("=== SMSOnTicketClosure END ===");
        }

        private static string GetConfigValue(IOrganizationService service, string name)
        {
            try
            {
                var query = new QueryExpression("new_environmentvariable")
                {
                    ColumnSet = new ColumnSet("new_value"),
                    Criteria =
                    {
                        Conditions =
                        {
                            new ConditionExpression("new_name", ConditionOperator.Equal, name)
                        }
                    }
                };

                var result = service.RetrieveMultiple(query).Entities.FirstOrDefault();
                return result?.GetAttributeValue<string>("new_value");
            }
            catch
            {
                return null;
            }
        }

        private static string ResolvePhone(EntityReference customerRef, IOrganizationService service)
        {
            try
            {
                Entity customer = null;
                string phone = null;

                if (customerRef.LogicalName == "contact")
                {
                    customer = service.Retrieve("contact", customerRef.Id, new ColumnSet("mobilephone", "telephone1", "telephone2"));
                    phone = FirstNonEmpty(
                        customer.GetAttributeValue<string>("mobilephone"),
                        customer.GetAttributeValue<string>("telephone1"),
                        customer.GetAttributeValue<string>("telephone2"));
                }
                else if (customerRef.LogicalName == "account")
                {
                    customer = service.Retrieve("account", customerRef.Id, new ColumnSet("telephone1", "telephone2", "telephone3", "new_companyrepresentativephonenumber"));
                    phone = FirstNonEmpty(
                        customer.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                        customer.GetAttributeValue<string>("telephone1"),
                        customer.GetAttributeValue<string>("telephone2"),
                        customer.GetAttributeValue<string>("telephone3"));
                }

                if (string.IsNullOrWhiteSpace(phone))
                    return null;

                return Regex.Replace(phone, @"[^\d+]", "");
            }
            catch
            {
                return null;
            }
        }

        private static string FirstNonEmpty(params string[] items)
            => items?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        private static bool SendSms(IOrganizationService service, ITracingService tracing, string phone, string body, Guid incidentId, EntityReference customerRef)
        {
            try
            {
                string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);

                var url = $"{SmsBaseUrl}?username={enc(SmsUsername)}&token={enc(SmsToken)}" +
                          $"&dests={enc(phone)}&body={enc(body)}" +
                          $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0" +
                          $"&src={enc(SmsSender)}";

                using (var http = new HttpClient())
                {
                    http.Timeout = TimeSpan.FromSeconds(15);
                    var resp = http.GetAsync(url).GetAwaiter().GetResult();
                    var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    tracing.Trace($"SMS Response: {(int)resp.StatusCode}, {content}");

                    if (resp.IsSuccessStatusCode)
                        return true;
                    else
                    {
                        CreateErrorLog(service, "Ticket Closure ", $"Failed response: {content}", incidentId, customerRef);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                CreateErrorLog(service, "Ticket Closure", $"Exception: {ex.Message}", incidentId, customerRef);
                return false;
            }
        }

        private static void CreateSmsNote(IOrganizationService service, string stage, string message, Guid incidentId, EntityReference customerRef, bool sent)
        {
            try
            {
                var note = new Entity("new_smsnotification");
                note["new_name"] = $"{stage}";
                note["new_smsbody"] = message;
                note["new_ticket"] = new EntityReference("incident", incidentId);
                if (customerRef != null)
                    note["new_contact"] = customerRef;
                note["new_issent"] = sent;
                service.Create(note);
            }
            catch
            {
                // Do not throw — silent fail
            }
        }

        private static void CreateErrorLog(IOrganizationService service, string stage, string error, Guid? incidentId, EntityReference customerRef)
        {
            try
            {
                var note = new Entity("new_smsnotification");
                note["new_name"] = $"{stage} - Exception";
                note["new_smsbody"] = error;
                if (incidentId.HasValue)
                    note["new_ticket"] = new EntityReference("incident", incidentId.Value);
                if (customerRef != null)
                    note["new_contact"] = customerRef;
                note["new_issent"] = false;
                service.Create(note);
            }
            catch
            {
                // Silent fail
            }
        }

        // 🔹 Arabic SMS Template
        private static class SmsTemplates
        {
            private const string RLE = "\u202B"; // Right-to-Left Embedding
            private const string PDF = "\u202C"; // Pop Directional Formatting
            private const string RLM = "\u200F"; // Right-to-Left Mark

            public static string ForTicketClosure(string ticket, string baseUrl) =>
                $"{RLE}عزيزنا المستثمر,\r\n" +
                $"تم اغلاق التذكرة رقم {RLM}{ticket} وحرصاً منا لرفع مستوى الجودة يسعدنا تقييمكم للخدمة المقدمة:\r\n" +
                $"{PDF}{baseUrl}?ticketNumber={ticket}";
        }
    }
}
