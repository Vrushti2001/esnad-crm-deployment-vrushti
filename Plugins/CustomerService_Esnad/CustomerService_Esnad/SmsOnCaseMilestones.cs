using System;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace Taadeen.Crm.Plugins
{
    public class SmsOnCaseMilestones : IPlugin
    {
        private const string BaseUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        private const int STATUS_TICKET_CREATION = 100000000;
        private const int STATUS_RETURN_TO_CUSTOMER = 100000001;
        private const int STATUS_SOLUTION_VERIFICATION = 100000002;

        public SmsOnCaseMilestones(string unsecureConfig, string secureConfig) { }

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            tracing.Trace("=== SmsOnCaseMilestones START ===");

            try
            {
                if (context.PrimaryEntityName != "incident")
                    return;

                if (context.MessageName.Equals("Create", StringComparison.OrdinalIgnoreCase))
                {
                    var id = (Guid)context.OutputParameters["id"];
                    var incident = service.Retrieve("incident", id, new ColumnSet("ticketnumber", "customerid", "statuscode"));
                    SendForCreate(incident, tracing, service);
                }
                else if (context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
                {
                    if (!(context.InputParameters.Contains("Target") && context.InputParameters["Target"] is Entity target))
                        return;

                    if (!target.Attributes.Contains("statuscode"))
                        return;

                    var id = target.Id;
                    var incident = service.Retrieve("incident", id, new ColumnSet("ticketnumber", "customerid", "statuscode"));

                    int? oldStatus = null;
                    if (context.PreEntityImages.Contains("PreImage") && context.PreEntityImages["PreImage"].Contains("statuscode"))
                        oldStatus = ((OptionSetValue)context.PreEntityImages["PreImage"]["statuscode"]).Value;

                    var newStatus = incident.Contains("statuscode") ? ((OptionSetValue)incident["statuscode"]).Value : (int?)null;

                    if (newStatus == null || (oldStatus.HasValue && oldStatus.Value == newStatus.Value))
                        return;

                    SendForStatus(incident, newStatus.Value, tracing, service);
                }
            }
            catch (Exception ex)
            {
                try
                {
                    tracing.Trace("Exception caught: " + ex.ToString());
                    CreateErrorLog(service, "Plugin Execution", ex.Message, null, null);
                }
                catch { /* Do nothing further */ }
                return;
            }
            finally
            {
                tracing.Trace("=== SmsOnCaseMilestones END ===");
            }
        }

        private void SendForCreate(Entity incident, ITracingService tracing, IOrganizationService service)
        {
            try
            {
                var ticket = incident.GetAttributeValue<string>("ticketnumber");
                var phone = ResolvePhone(incident, tracing, service);
                var customerRef = incident.GetAttributeValue<EntityReference>("customerid");

                if (string.IsNullOrWhiteSpace(ticket))
                    return;

                if (string.IsNullOrWhiteSpace(phone))
                {
                    CreateSmsNote(service, "Ticket Creation", "Invalid or missing phone number", incident.Id, customerRef, false);
                    return;
                }

                var message = SmsTemplates.ForTicketCreation(ticket);
                var sent = SendSms(service, tracing, phone, message, "Ticket Creation", incident.Id, customerRef);
                CreateSmsNote(service, "Ticket Creation", message, incident.Id, customerRef, sent);
            }
            catch (Exception ex)
            {
                CreateErrorLog(service, "Ticket Creation", ex.Message, incident.Id, incident.GetAttributeValue<EntityReference>("customerid"));
            }
        }

        private void SendForStatus(Entity incident, int newStatus, ITracingService tracing, IOrganizationService service)
        {
            try
            {
                var ticket = incident.GetAttributeValue<string>("ticketnumber");
                var phone = ResolvePhone(incident, tracing, service);
                var customerRef = incident.GetAttributeValue<EntityReference>("customerid");

                if (string.IsNullOrWhiteSpace(ticket))
                    return;

                if (string.IsNullOrWhiteSpace(phone))
                {
                    CreateSmsNote(service, "Status Update", "Invalid or missing phone number", incident.Id, customerRef, false);
                    return;
                }
                var nameofstage = "Status Update";
                string message = null;
                if (newStatus == STATUS_RETURN_TO_CUSTOMER) {
                    message = SmsTemplates.ForReturnToCustomer(ticket);
                    nameofstage = "Return to Customer";
                }   
                else if (newStatus == STATUS_SOLUTION_VERIFICATION)
                {
                    message = SmsTemplates.ForSolutionVerification(ticket);
                    nameofstage = "Solution verification";
                }
                    

                if (string.IsNullOrWhiteSpace(message))
                    return;

                var sent = SendSms(service, tracing, phone, message, nameofstage, incident.Id, customerRef);
                CreateSmsNote(service, nameofstage, message, incident.Id, customerRef, sent);
            }
            catch (Exception ex)
            {
                CreateErrorLog(service, "Status Update", ex.Message, incident.Id, incident.GetAttributeValue<EntityReference>("customerid"));
            }
        }

        private string ResolvePhone(Entity incident, ITracingService tracing, IOrganizationService service)
        {
            try
            {
                var cust = incident.GetAttributeValue<EntityReference>("customerid");
                if (cust == null) return null;

                Entity record;
                string raw = null;

                if (cust.LogicalName == "contact")
                {
                    record = service.Retrieve("contact", cust.Id, new ColumnSet("mobilephone", "telephone1", "telephone2"));
                    raw = FirstNonEmpty(record.GetAttributeValue<string>("mobilephone"),
                                        record.GetAttributeValue<string>("telephone1"),
                                        record.GetAttributeValue<string>("telephone2"));
                }
                else if (cust.LogicalName == "account")
                {
                    record = service.Retrieve("account", cust.Id, new ColumnSet("new_companyrepresentativephonenumber", "telephone1", "telephone2", "telephone3"));
                    raw = FirstNonEmpty(record.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                                        record.GetAttributeValue<string>("telephone1"),
                                        record.GetAttributeValue<string>("telephone2"),
                                        record.GetAttributeValue<string>("telephone3"));
                }
                else return null;

                if (string.IsNullOrWhiteSpace(raw))
                    return null;

                return Regex.Replace(raw, @"[^\d+]", "");
            }
            catch (Exception ex)
            {
                CreateErrorLog(service, "Phone Resolution", ex.Message, incident.Id, incident.GetAttributeValue<EntityReference>("customerid"));
                return null;
            }
        }

        private static string FirstNonEmpty(params string[] items) =>
            items?.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s));

        private bool SendSms(IOrganizationService service, ITracingService tracing, string phone, string body,
                             string stageName, Guid incidentId, EntityReference customerRef)
        {
            try
            {
                tracing.Trace("SendSms called");

                string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);
                var url = $"{BaseUrl}?username={enc(Username)}&token={enc(Token)}" +
                          $"&dests={enc(phone)}&body={enc(body)}&src={enc(Sender)}";

                using (var http = new HttpClient { Timeout = TimeSpan.FromSeconds(15) })
                {
                    var resp = http.GetAsync(url).GetAwaiter().GetResult();
                    var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();

                    tracing.Trace($"SMS response: {(int)resp.StatusCode}, {content}");

                    if (resp.IsSuccessStatusCode)
                        return true;
                    else
                    {
                        CreateSmsNote(service, stageName, $"Failed: {content}", incidentId, customerRef, false);
                        return false;
                    }
                }
            }
            catch (Exception ex)
            {
                CreateErrorLog(service, stageName, $"SMS Error: {ex.Message}", incidentId, customerRef);
                return false;
            }
        }

        private void CreateSmsNote(IOrganizationService service, string stageName, string message,
                                   Guid incidentId, EntityReference customerRef, bool sent)
        {
            try
            {
                var note = new Entity("new_smsnotification");
                note["new_name"] = $"{stageName}";
                note["new_smsbody"] = message;
                note["new_ticket"] = new EntityReference("incident", incidentId);
                if (customerRef != null)
                    note["new_contact"] = customerRef;
                note["new_issent"] = sent;
                service.Create(note);
            }
            catch { /* silently fail */ }
        }

        private void CreateErrorLog(IOrganizationService service, string stage, string error, Guid? incidentId, EntityReference customerRef)
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
            catch { /* ignore completely */ }
        }

        private static class SmsTemplates
        {
            private const string RLE = "\u202B"; // RTL Embedding
            private const string PDF = "\u202C"; // Pop Directional Formatting
            private const string RLM = "\u200F"; // RTL Mark

            public static string ForTicketCreation(string ticket) =>
                $"{RLE}عزيزنا المستثمر,\r\nنشكر لكم تواصلكم معنا، تم إنشاء تذكرة جديدة برقم {RLM}{ticket}.{PDF}";

            public static string ForReturnToCustomer(string ticket) =>
                $"{RLE}عزيزنا المستثمر,\r\nتم إعادة التذكرة رقم {RLM}{ticket} لاستكمال بعض المتطلبات، يرجى استكمالها عبر البريد الإلكتروني.{PDF}";

            public static string ForSolutionVerification(string ticket) =>
                $"{RLE}عزيزنا المستثمر،\r\nتم معالجة التذكرة رقم {RLM}{ticket}. في حال استمرار المشكلة يرجى الرد عبر البريد الإلكتروني.{PDF}";
        }
    }
}
