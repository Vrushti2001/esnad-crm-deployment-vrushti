using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;


namespace InvestorSupport
{
    public class smsOnKIComunicationcreate : IPlugin
    {
        private const string SmsGatewayUrl = "https://api.oursms.com/api-a/msgs";
        private const string Username = "Taadeen2.0";
        private const string Token = "7sgOnsFhAuYdNgg5a3R4";
        private const string Sender = "Taadeen";

        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);

            tracing.Trace("SendVisitorSMSOnCreate plugin started.");

            try
            {
                if (context.MessageName.ToLower() != "create" || !context.InputParameters.Contains("Target"))
                    return;

                var KICommunications = (Entity)context.InputParameters["Target"];
                if (KICommunications.LogicalName != "new_keyinvestorscommunication") return;

                string ReferenceNumber = KICommunications.GetAttributeValue<string>("new_referencenumber") ?? "";
                EntityReference accountRef = KICommunications.GetAttributeValue<EntityReference>("new_Investor");
                string investorName = "";
                string rmFullName = "";
                string rmEmail = "";
                string rmPhone = "";
                if (accountRef != null)
                {
                    // 2. Retrieve Account fields including Relationship Manager lookup
                    var account = service.Retrieve(
                        "account",
                        accountRef.Id,
                        new ColumnSet("name", "new_relationshipmanager")
                    );

                     investorName = account.GetAttributeValue<string>("name");

                    // 3. Get Relationship Manager Lookup (SystemUser)
                    EntityReference rmRef = account.GetAttributeValue<EntityReference>("new_relationshipmanager");

                    if (rmRef != null)
                    {
                        // 4. Retrieve System User details
                        var rm = service.Retrieve(
                            "systemuser",
                            rmRef.Id,
                            new ColumnSet("fullname", "internalemailaddress", "mobilephone")
                        );

                         rmFullName = rm.GetAttributeValue<string>("fullname");
                         rmEmail = rm.GetAttributeValue<string>("internalemailaddress");
                        rmPhone = rm.GetAttributeValue<string>("mobilephone");

                        tracing.Trace($"Investor Name: {investorName}");
                        tracing.Trace($"RM Name: {rmFullName}");
                        tracing.Trace($"RM Email: {rmEmail}");
                        tracing.Trace($"RM Mobile: {rmPhone}");
                    }
                }
                
                string phone = null;
                
                if (string.IsNullOrWhiteSpace(phone) && accountRef != null)
                    phone = ResolvePhoneForAccount(service, accountRef, tracing);

                string smsBody = SmsTemplates.ForForKICommunicationsCreateCreate(ReferenceNumber, investorName, rmFullName, rmEmail, rmPhone);
                bool smsSent = false;
                string apiResult = "";

                if (!string.IsNullOrWhiteSpace(phone) && IsValidPhone(phone))
                {
                    apiResult = SendSms(phone, smsBody, tracing);
                    smsSent = true;
                }
                else
                {
                    apiResult = "Invalid or missing phone number";
                }

                // ✅ Log SMS result in CRM
                var note = new Entity("new_smsnotification");
                note["new_name"] = "KEY INVESTORS COMMUNICATION Creation";
                note["new_smsbody"] = smsBody;
                note["new_issent"] = smsSent;
                note["new_keyinvestorscommunication"] = KICommunications.ToEntityReference();
                note["new_contact"] = accountRef;

                service.Create(note);

                tracing.Trace($"SMS process completed for visitor {ReferenceNumber}. Result: {apiResult}");
            }
            catch (Exception ex)
            {
                tracing.Trace("Error in plugin: " + ex.Message);
                LogErrorToSmsNotification(serviceProvider, ex);
                // remain silent — no exception thrown
            }
        }

        private string ResolvePhoneForContact(IOrganizationService service, EntityReference contactRef, ITracingService tracing)
        {
            try
            {
                var c = service.Retrieve("contact", contactRef.Id, new ColumnSet("mobilephone", "telephone1", "telephone2"));
                var raw = FirstNonEmpty(
                    c.GetAttributeValue<string>("mobilephone"),
                    c.GetAttributeValue<string>("telephone1"),
                    c.GetAttributeValue<string>("telephone2")
                );
                return CleanPhone(raw);
            }
            catch (Exception ex)
            {
                tracing.Trace("Error resolving contact phone: " + ex.Message);
                return null;
            }
        }

        private string ResolvePhoneForAccount(IOrganizationService service, EntityReference accountRef, ITracingService tracing)
        {
            try
            {
                var a = service.Retrieve("account", accountRef.Id, new ColumnSet("telephone1", "telephone2", "telephone3", "new_companyrepresentativephonenumber"));
                var raw = FirstNonEmpty(
                    a.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                    a.GetAttributeValue<string>("telephone1"),
                    a.GetAttributeValue<string>("telephone2"),
                    a.GetAttributeValue<string>("telephone3")
                );
                return CleanPhone(raw);
            }
            catch (Exception ex)
            {
                tracing.Trace("Error resolving account phone: " + ex.Message);
                return null;
            }
        }

        private string FirstNonEmpty(params string[] vals)
        {
            foreach (var v in vals)
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            return null;
        }

        private string CleanPhone(string raw)
        {
            return Regex.Replace(raw ?? string.Empty, @"[^\d+]", "");
        }

        private bool IsValidPhone(string phone)
        {
            return !string.IsNullOrWhiteSpace(phone) && Regex.IsMatch(phone, @"^\+?\d{8,}$");
        }

        private string SendSms(string phone, string body, ITracingService tracing)
        {
            try
            {
                string enc(string s) => Uri.EscapeDataString(s ?? string.Empty);
                var url = $"{SmsGatewayUrl}?username={enc(Username)}&token={enc(Token)}" +
                          $"&dests={enc(phone)}&body={enc(body)}" +
                          $"&priority=0&delay=0&validity=0&maxParts=0&dlr=0&prevDups=0" +
                          $"&src={enc(Sender)}";

                using (var http = new HttpClient())
                {
                    var resp = http.GetAsync(url).GetAwaiter().GetResult();
                    var content = resp.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return $"{(int)resp.StatusCode} {content}";
                }
            }
            catch (Exception ex)
            {
                tracing.Trace("SMS sending error: " + ex.Message);
                return "Error sending SMS: " + ex.Message;
            }
        }

        // 🧾 Log Errors into the SAME entity (new_smsnotification)
        private void LogErrorToSmsNotification(IServiceProvider serviceProvider, Exception ex)
        {
            try
            {
                var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = serviceFactory.CreateOrganizationService(context.UserId);

                var note = new Entity("new_smsnotification");
                note["new_name"] = "KEY INVESTORS COMMUNICATION SMS Error";
                note["new_issent"] = false;
                note["new_smsbody"] = ex.Message + (ex.InnerException != null ? " | Inner: " + ex.InnerException.Message : "");

                service.Create(note);
            }
            catch
            {
                // remain silent to avoid secondary failure
            }
        }

        private static class SmsTemplates
        {
            private const string RLE = "\u202B"; // RTL Embedding
            private const string PDF = "\u202C"; // Pop Directional Formatting
            private const string RLM = "\u200F"; // RTL Mark

            public static string ForForKICommunicationsCreateCreate(string ReferenceNumber, string investorName, string rmFullName, string rmEmail, string rmPhone)
            {
                string baseLink = "https://feedback-dev.crm-esnad.com/KICommunication";
                string fullLink = $"{baseLink}?ref={Uri.EscapeDataString(ReferenceNumber ?? string.Empty)}";

                return
                    $"{RLE}{RLM}شريكنا المستثمر {investorName}،{PDF}\r\n" +
                    $"{RLE}{RLM}حرصاً منا لرفع مستوى جودة الخدمة يسعدنا تقييمكم للخدمة المقدمة:{PDF}\r\n" +
                    $"{RLE}{RLM}{fullLink}{PDF}\r\n\r\n" +
                    $"{RLE}{RLM}نسعد بخدمتكم،{PDF}\r\n" +
                    $"{RLE}{RLM}مركز دعم كبار المستثمرين – قطاع التعدين{PDF}\r\n" +
                     $"مدير العلاقة:\r\n" +
                    $"{RLE}{RLM}{rmFullName} - {rmPhone} - {rmEmail}{PDF}";

            }
        }
    }
}
