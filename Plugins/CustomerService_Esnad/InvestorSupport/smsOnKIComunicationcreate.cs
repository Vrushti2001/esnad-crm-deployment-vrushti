using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Net.Http;
using System.Text.RegularExpressions;

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
            var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = factory.CreateOrganizationService(context.UserId);

            tracing.Trace("🚀 SMS KI Communication Plugin START");

            try
            {
                if (context.MessageName != "Create" ||
                    !context.InputParameters.Contains("Target") ||
                    !(context.InputParameters["Target"] is Entity target))
                    return;

                if (target.LogicalName != "new_keyinvestorscommunication")
                    return;

                string referenceNo = target.GetAttributeValue<string>("new_referencenumber") ?? "N/A";
                tracing.Trace("Reference No: " + referenceNo);

                EntityReference accountRef =
                    target.GetAttributeValue<EntityReference>("new_investor");

                if (accountRef == null)
                {
                    tracing.Trace("❌ Investor Account not found.");
                    return;
                }

                // ================= ACCOUNT =================
                Entity account = service.Retrieve(
                    "account",
                    accountRef.Id,
                    new ColumnSet(
                        "name",
                        "telephone1",
                        "telephone2",
                        "telephone3",
                        "new_companyrepresentativephonenumber",
                        "new_relationshipmanager"
                    )
                );

                string investorName = account.GetAttributeValue<string>("name") ?? "";
                tracing.Trace("Investor: " + investorName);

                // ================= PHONE =================
                string phone = CleanPhone(
                    FirstNonEmpty(
                        account.GetAttributeValue<string>("new_companyrepresentativephonenumber"),
                        account.GetAttributeValue<string>("telephone1"),
                        account.GetAttributeValue<string>("telephone2"),
                        account.GetAttributeValue<string>("telephone3")
                    )
                );

                tracing.Trace("Resolved Phone: " + phone);

                // ================= RM =================
                string rmName = "", rmEmail = "", rmPhone = "";

                if (account.Attributes.Contains("new_relationshipmanager"))
                {
                    var rmRef = account.GetAttributeValue<EntityReference>("new_relationshipmanager");
                    tracing.Trace("RM Found: " + rmRef.Id);

                    var rm = service.Retrieve(
                        "systemuser",
                        rmRef.Id,
                        new ColumnSet("fullname", "internalemailaddress", "mobilephone")
                    );

                    rmName = rm.GetAttributeValue<string>("fullname") ?? "";
                    rmEmail = rm.GetAttributeValue<string>("internalemailaddress") ?? "";
                    rmPhone = rm.GetAttributeValue<string>("mobilephone") ?? "";

                    tracing.Trace($"RM → {rmName} | {rmEmail} | {rmPhone}");
                }
                else
                {
                    tracing.Trace("⚠ Account has NO Relationship Manager.");
                }

                // ================= SMS =================
                string smsBody = SmsTemplate.Build(
                    referenceNo,
                    investorName,
                    rmName,
                    rmEmail,
                    rmPhone
                );

                bool sent = false;
                string result = "Not sent";

                if (IsValidPhone(phone))
                {
                    result = SendSms(phone, smsBody, tracing);
                    sent = true;
                }
                else
                {
                    tracing.Trace("❌ Invalid phone number. SMS skipped.");
                }

                // ================= LOG =================
                Entity log = new Entity("new_smsnotification");
                log["new_name"] = "KI Communication SMS";
                log["new_smsbody"] = smsBody;
                log["new_issent"] = sent;
                log["new_keyinvestorscommunication"] = target.ToEntityReference();
                log["new_contact"] = accountRef;

                service.Create(log);

                tracing.Trace("✅ SMS Plugin END. Result: " + result);
            }
            catch (Exception ex)
            {
                tracing.Trace("🔥 Plugin Error: " + ex.Message);
                LogError(serviceProvider, ex);
            }
        }

        // ================= HELPERS =================

        private static string FirstNonEmpty(params string[] values)
        {
            foreach (var v in values)
                if (!string.IsNullOrWhiteSpace(v))
                    return v;
            return null;
        }

        private static string CleanPhone(string phone)
        {
            return Regex.Replace(phone ?? "", @"[^\d+]", "");
        }

        private static bool IsValidPhone(string phone)
        {
            return !string.IsNullOrWhiteSpace(phone) &&
                   Regex.IsMatch(phone, @"^\+?\d{8,}$");
        }

        private static string SendSms(string phone, string body, ITracingService tracing)
        {
            try
            {
                string enc(string s) => Uri.EscapeDataString(s ?? "");

                var url =
                    $"{SmsGatewayUrl}?username={enc(Username)}&token={enc(Token)}" +
                    $"&dests={enc(phone)}&body={enc(body)}&src={enc(Sender)}";

                using (var http = new HttpClient())
                {
                    var res = http.GetAsync(url).GetAwaiter().GetResult();
                    var txt = res.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                    return $"{(int)res.StatusCode} {txt}";
                }
            }
            catch (Exception ex)
            {
                tracing.Trace("SMS Error: " + ex.Message);
                return ex.Message;
            }
        }

        private static void LogError(IServiceProvider sp, Exception ex)
        {
            try
            {
                var ctx = (IPluginExecutionContext)sp.GetService(typeof(IPluginExecutionContext));
                var fac = (IOrganizationServiceFactory)sp.GetService(typeof(IOrganizationServiceFactory));
                var svc = fac.CreateOrganizationService(ctx.UserId);

                Entity log = new Entity("new_smsnotification");
                log["new_name"] = "KI SMS ERROR";
                log["new_issent"] = false;
                log["new_smsbody"] = ex.Message;

                svc.Create(log);
            }
            catch { }
        }

        // ================= TEMPLATE =================

        private static class SmsTemplate
        {
            private const string RLE = "\u202B";
            private const string PDF = "\u202C";
            private const string RLM = "\u200F";

            public static string Build(
                string refNo,
                string investor,
                string rmName,
                string rmEmail,
                string rmPhone)
            {
                string link = $"https://feedback-dev.crm-esnad.com/KICommunication?ref={Uri.EscapeDataString(refNo)}";

                return
                    $"{RLE}{RLM}شريكنا المستثمر {investor}،{PDF}\r\n" +
                    $"{RLE}{RLM}يسعدنا تقييمكم للخدمة المقدمة:{PDF}\r\n" +
                    $"{RLE}{RLM}{link}{PDF}\r\n\r\n" +
                    $"{RLE}{RLM}مدير العلاقة:{PDF}\r\n" +
                    $"{RLE}{RLM}{rmName} {rmPhone} {rmEmail}{PDF}";
            }
        }
    }
}
