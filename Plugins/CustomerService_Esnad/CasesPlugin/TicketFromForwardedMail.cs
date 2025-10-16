
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace CasesPlugin
    {
        public class TicketFromForwardedMail : IPlugin
        {
            public void Execute(IServiceProvider serviceProvider)
            {
                var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
                var factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
                var service = factory.CreateOrganizationService(context.UserId);
                var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

                tracing.Trace("=== EmailToCasePlugin START ===");

                try
                {
                    if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is EntityReference emailRef))
                    {
                        tracing.Trace("Target not found or invalid.");
                        return;
                    }

                    Guid emailId = emailRef.Id;

                    var email = service.Retrieve("email", emailId, new ColumnSet(true));

                    if (email.Attributes.Contains("directioncode"))
                    {
                        if (email.Attributes.Contains("from"))
                        {
                            EntityCollection fromCollection = email.GetAttributeValue<EntityCollection>("from");
                            if (fromCollection != null && fromCollection.Entities.Count > 0)
                            {
                                foreach (var activityParty in fromCollection.Entities)
                                {
                                    Entity party = activityParty;
                                    string senderEmail = party.GetAttributeValue<string>("addressused");

                                    if (!string.IsNullOrEmpty(senderEmail) &&
                                        senderEmail.Equals("no-reply@taadeen.sa", StringComparison.OrdinalIgnoreCase))
                                    {
                                        string plainTextBody = email.GetAttributeValue<string>("description") ?? string.Empty;
                                        string body = StripHtml(plainTextBody);
                                        string subject1 = email.GetAttributeValue<string>("subject");

                                        if (subject1.ToLower() == "book an appointment" || subject1.ToLower() == "حجز موعد")
                                        {
                                            EntityReference customerRef = null;
                                            string beneficiaryType = MatchValueForAppointment(body, @"(?:Type of Beneficiary|مقدم الطلب)\s*\n(.+)");
                                            string idNumber = MatchValueForAppointment(body, @"(?:ID Number|رقم الهوية)\s*\n(.+)");
                                            string companyName = MatchValueForAppointment(body, @"(?:Company Name|اسم الشركة)\s*\n(.+)");
                                            string CRN = MatchValueForAppointment(body, @"(?:Commercial Registration Number|رقم السجل التجاري)\s*\n(.+)");
                                            string fullName = MatchValueForAppointment(body, @"(?:Full Name|الإسم الثلاثي)\s*\n(.+)");
                                            string phone2 = MatchValueForAppointment(body, @"(?:Phone Number|رقم الهاتف)\s*\n(.+)").Replace("&#43;", "+");
                                            string email1 = MatchValueForAppointment(body, @"(?:Email Address|البريد الإلكتروني)\s*\n(.+)");
                                            if (string.IsNullOrWhiteSpace(email1))
                                                return;
                                            string requestType2 = MatchValueForAppointment(body, @"(?:Request Type|نوع الموعد)\s*\n(.+)");
                                            string department = MatchValueForAppointment(body, @"(?:Sector|القطاع)\s*\n(.+)");
                                            string complianceType = MatchValueForAppointment(body, @"(?<=\r?\n\r?\n)(?:الامتثال|Compliance)\s*\r?\n\s*([^\r\n]+)");
                                            string licenseType = MatchValueForAppointment(body, @"(?<=\r?\n\r?\n)(?:الرخص|Licenses)\s*\r?\n\s*([^\r\n]+)");
                                            string reason = MatchValueForAppointment(body, @"(?:Reason of this request|سبب حجز الموعد)\s*\n([\s\S]+)");
                                            string normalizedValue = NormalizeInput(requestType2);

                                            if (beneficiaryType == "فرد" || beneficiaryType.ToLower() == "individual")
                                            {
                                                var query = new QueryExpression("contact")
                                                {
                                                    ColumnSet = new ColumnSet("contactid"),
                                                    Criteria = new FilterExpression
                                                    {
                                                        Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.Equal, email1) }
                                                    }
                                                };

                                                var result = service.RetrieveMultiple(query);
                                                Guid contactId;

                                                if (result.Entities.Count > 0)
                                                    contactId = result.Entities[0].Id;
                                                else
                                                {
                                                    var contact = new Entity("contact")
                                                    {
                                                        ["lastname"] = fullName,
                                                        ["emailaddress1"] = email1,
                                                        ["mobilephone"] = phone2,
                                                        ["new_nationalidnumber"] = idNumber
                                                    };
                                                    contactId = service.Create(contact);
                                                }

                                                customerRef = new EntityReference("contact", contactId);
                                            }
                                            else if (beneficiaryType == "شركة" || beneficiaryType.ToLower() == "company")
                                            {
                                                var query = new QueryExpression("account")
                                                {
                                                    ColumnSet = new ColumnSet("accountid"),
                                                    Criteria = new FilterExpression
                                                    {
                                                        Conditions = { new ConditionExpression("name", ConditionOperator.Equal, companyName) }
                                                    }
                                                };

                                                var result = service.RetrieveMultiple(query);
                                                Guid accountId;

                                                if (result.Entities.Count > 0)
                                                    accountId = result.Entities[0].Id;
                                                else
                                                {
                                                    var account = new Entity("account")
                                                    {
                                                        ["name"] = companyName,
                                                        ["emailaddress1"] = email1,
                                                        ["new_companyrepresentativephonenumber"] = phone2,
                                                        ["new_crnumber"] = CRN,
                                                        ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                                                    };
                                                    accountId = service.Create(account);
                                                }

                                                customerRef = new EntityReference("account", accountId);
                                            }
                                            else
                                            {
                                                tracing.Trace("Unsupported beneficiary type: " + beneficiaryType);
                                                return;
                                            }

                                            EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);
                                            int beneficiaryValue = -1;
                                            if (beneficiaryType.Equals("فرد", StringComparison.OrdinalIgnoreCase) ||
                                                beneficiaryType.Equals("Individual", StringComparison.OrdinalIgnoreCase))
                                                beneficiaryValue = 1;
                                            else if (beneficiaryType.Equals("شركة", StringComparison.OrdinalIgnoreCase) ||
                                                     beneficiaryType.Equals("Company", StringComparison.OrdinalIgnoreCase))
                                                beneficiaryValue = 2;

                                            int SectorValue = -1;
                                            if (department.Equals("الرخص", StringComparison.OrdinalIgnoreCase) ||
                                                department.Equals("Licensing", StringComparison.OrdinalIgnoreCase))
                                                SectorValue = 1;
                                            else if (department.Equals("تجربة العميل", StringComparison.OrdinalIgnoreCase) ||
                                                     department.Equals("Customer Experience", StringComparison.OrdinalIgnoreCase))
                                                SectorValue = 3;
                                            else if (department.Equals("الإمتثال", StringComparison.OrdinalIgnoreCase) ||
                                                    department.Equals("Compliance", StringComparison.OrdinalIgnoreCase))
                                                SectorValue = 2;
                                            else if (department.Equals("أخرى", StringComparison.OrdinalIgnoreCase) ||
                                                    department.Equals("Other", StringComparison.OrdinalIgnoreCase))
                                                SectorValue = 4;

                                            int ComplianceValue = -1;
                                            if (complianceType.Equals("الامتثال المالي", StringComparison.OrdinalIgnoreCase) ||
                                                complianceType.Equals("Financial Compliance", StringComparison.OrdinalIgnoreCase))
                                                ComplianceValue = 1;
                                            else if (complianceType.Equals("الامتثال الرقابي", StringComparison.OrdinalIgnoreCase) ||
                                                     complianceType.Equals("Regulatory Compliance", StringComparison.OrdinalIgnoreCase))
                                                ComplianceValue = 2;
                                            else if (complianceType.Equals("الاستدامة", StringComparison.OrdinalIgnoreCase) ||
                                                    complianceType.Equals("Sustainability", StringComparison.OrdinalIgnoreCase))
                                                ComplianceValue = 3;

                                            int licenseTypeValue = -1;
                                            if (licenseType.Equals("رخص الكشف والاستطلاع", StringComparison.OrdinalIgnoreCase) ||
                                                licenseType.Equals("Exploration Licenses", StringComparison.OrdinalIgnoreCase))
                                                licenseTypeValue = 1;
                                            else if (licenseType.Equals("رخص محاجر مواد البناء", StringComparison.OrdinalIgnoreCase) ||
                                                     licenseType.Equals("BMQ Licenses", StringComparison.OrdinalIgnoreCase))
                                                licenseTypeValue = 2;
                                            else if (licenseType.Equals("رخص التعدين و المنجم الصغير", StringComparison.OrdinalIgnoreCase) ||
                                                    licenseType.Equals("Mining and Small Mine Licenses", StringComparison.OrdinalIgnoreCase))
                                                licenseTypeValue = 3;

                                            var incident = new Entity("incident")
                                            {
                                                ["title"] = "book an appointment",
                                                ["new_tickettype"] = ticketTypeRef,
                                                ["description"] = reason,
                                                ["new_beneficiarytype"] = new OptionSetValue(beneficiaryValue),
                                                ["new_ticketsubmissionchannel"] = new OptionSetValue(9),
                                                ["customerid"] = customerRef,
                                                ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                                            };

                                            if ((department.Equals("Licensing", StringComparison.OrdinalIgnoreCase) ||
                                                 department.Equals("الرخص", StringComparison.OrdinalIgnoreCase)) &&
                                                !string.IsNullOrWhiteSpace(licenseType))
                                                incident["new_licenses"] = new OptionSetValue(licenseTypeValue);
                                            else if ((department.Equals("Compliance", StringComparison.OrdinalIgnoreCase) ||
                                                      department.Equals("الامتثال", StringComparison.OrdinalIgnoreCase)) &&
                                                     !string.IsNullOrWhiteSpace(complianceType))
                                                incident["new_compliance"] = new OptionSetValue(ComplianceValue);

                                            incident["new_sector"] = new OptionSetValue(SectorValue);

                                            Entity email11 = new Entity("email");
                                            email11.Id = email.Id;
                                            email11.Attributes["regardingobjectid"] = new EntityReference("incident", service.Create(incident));
                                            service.Update(email11);
                                        }

                                        else if (subject1.ToLower() == "contact us" || subject1.ToLower() == "اتصل بنا")
                                        {
                                            string beneficiaryType = MatchValue(body, @"(?:نوع المستفيد|Type of Beneficiary)[:\-]?\s*(مستثمر|فرد|Investor|Individual|وكيل لمستثمر|Investor Representative|أخرى|Other)");
                                            string company = MatchValue(body, @"(?:اسم الشركة|Company Name)[:\-]?\s*([^\r\n]+?)\s*(?=رقم السجل التجاري|Commercial Registration Number)");
                                            string crNumber = MatchValue(body, @"(?:رقم السجل التجاري|Commercial Registration Number(?:\s*\(CR\))?)[:\-]?\s*(\d{5,})");
                                            string phone = MatchValue(body, @"(?:رقم الهاتف|Mobile Number)[:\-]?\s*(\d+)");
                                            string emailAddr = MatchValue(body, @"(?:عنوان البريد الإلكتروني|Email Address)[:\-]?\s*([a-zA-Z0-9._%+-]+@[a-zA-Z0-9.-]+\.[a-zA-Z]{2,})");
                                            if (string.IsNullOrWhiteSpace(emailAddr))
                                                return;
                                            string requestType = MatchValue(body, @"(?:نوع الطلب|Request Type)[:\-]?\s*(.+?)\s*(?=الموضوع|Subject)");
                                            string subject = MatchValue(body, @"(?:الموضوع|Subject)[:\-]?\s*(.+?)\s*(?=نص الرسالة|Message Text)");
                                            string message = MatchValue(body, @"(?:نص الرسالة|Message Text)[:\-]?\s*([\s\S]+?)(?=تحميل ملفات|Attachments|$)");
                                            string nationalId = MatchValue(body, @"(?:رقم الهوية|National ID Number)[:\-]?\s*(\d{10,15})");
                                            string normalizedValue = NormalizeInput(requestType);

                                            EntityReference customerRef;
                                            if (beneficiaryType == "فرد" || beneficiaryType.ToLower() == "individual" ||
                                                beneficiaryType == "أخرى" || beneficiaryType.ToLower() == "other")
                                            {
                                                var query = new QueryExpression("contact")
                                                {
                                                    ColumnSet = new ColumnSet("contactid"),
                                                    Criteria = new FilterExpression
                                                    {
                                                        Conditions = { new ConditionExpression("emailaddress1", ConditionOperator.Equal, emailAddr) }
                                                    }
                                                };
                                                var result = service.RetrieveMultiple(query);
                                                Guid contactId;

                                                if (result.Entities.Count > 0)
                                                    contactId = result.Entities[0].Id;
                                                else
                                                {
                                                    var contact = new Entity("contact")
                                                    {
                                                        ["lastname"] = emailAddr,
                                                        ["emailaddress1"] = emailAddr,
                                                        ["mobilephone"] = phone,
                                                        ["new_nationalidnumber"] = nationalId,
                                                        ["new_beneficiarytype"] = new OptionSetValue(GetOptionSetValue(service, "contact", "new_beneficiarytype", beneficiaryType))
                                                    };
                                                    contactId = service.Create(contact);
                                                }
                                                customerRef = new EntityReference("contact", contactId);
                                            }
                                            else if (beneficiaryType == "مستثمر" || beneficiaryType.ToLower() == "investor" ||
                                                     beneficiaryType == "وكيل لمستثمر" || beneficiaryType.ToLower() == "investor representative")
                                            {
                                                var query = new QueryExpression("account")
                                                {
                                                    ColumnSet = new ColumnSet("accountid"),
                                                    Criteria = new FilterExpression
                                                    {
                                                        Conditions = { new ConditionExpression("name", ConditionOperator.Equal, company) }
                                                    }
                                                };
                                                var result = service.RetrieveMultiple(query);
                                                Guid accountId;

                                                if (result.Entities.Count > 0)
                                                    accountId = result.Entities[0].Id;
                                                else
                                                {
                                                    var account = new Entity("account")
                                                    {
                                                        ["name"] = company,
                                                        ["emailaddress1"] = emailAddr,
                                                        ["new_companyrepresentativephonenumber"] = phone,
                                                        ["new_crnumber"] = crNumber,
                                                        ["new_beneficiarytype"] = new OptionSetValue(GetOptionSetValue(service, "account", "new_beneficiarytype", beneficiaryType)),
                                                        ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                                                    };
                                                    accountId = service.Create(account);
                                                }
                                                customerRef = new EntityReference("account", accountId);
                                            }
                                            else
                                            {
                                                tracing.Trace("Unsupported beneficiary type: " + beneficiaryType);
                                                return;
                                            }

                                            int beneficiaryValue = -1;
                                            if (beneficiaryType.Equals("فرد", StringComparison.OrdinalIgnoreCase) ||
                                                beneficiaryType.Equals("Individual", StringComparison.OrdinalIgnoreCase))
                                                beneficiaryValue = 1;
                                            else if (beneficiaryType.Equals("مستثمر", StringComparison.OrdinalIgnoreCase) ||
                                                     beneficiaryType.Equals("Investor", StringComparison.OrdinalIgnoreCase))
                                                beneficiaryValue = 3;
                                            else if (beneficiaryType.Equals("وكيل لمستثمر", StringComparison.OrdinalIgnoreCase) ||
                                                     beneficiaryType.Equals("Investor Representative", StringComparison.OrdinalIgnoreCase))
                                                beneficiaryValue = 4;
                                            else if (beneficiaryType.Equals("أخرى", StringComparison.OrdinalIgnoreCase) ||
                                                     beneficiaryType.Equals("Other", StringComparison.OrdinalIgnoreCase))
                                                beneficiaryValue = 5;

                                            EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);
                                            var incident = new Entity("incident")
                                            {
                                                ["title"] = subject,
                                                ["description"] = message,
                                                ["new_tickettype"] = ticketTypeRef,
                                                ["new_beneficiarytype"] = new OptionSetValue(beneficiaryValue),
                                                ["new_ticketsubmissionchannel"] = new OptionSetValue(8),
                                                ["customerid"] = customerRef,
                                                ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                                            };

                                            Entity email11 = new Entity("email");
                                            email11.Id = email.Id;
                                            email11.Attributes["regardingobjectid"] = new EntityReference("incident", service.Create(incident));
                                            service.Update(email11);
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
                catch (Exception ex)
                {
                    tracing.Trace("❌ Error: " + ex.Message + " | " + ex.StackTrace);
                    throw;
                }

                tracing.Trace("=== EmailToCasePlugin END ===");
            }

            // ---------------- HELPER FUNCTIONS -------------------

            public static string StripHtml(string html)
            {
                try
                {
                    var doc = System.Xml.Linq.XDocument.Parse($"<root>{html}</root>");
                    return string.Concat(doc.DescendantNodes().OfType<System.Xml.Linq.XText>().Select(t => t.Value));
                }
                catch
                {
                    return Regex.Replace(html, "<.*?>", string.Empty);
                }
            }

            public static string NormalizeInput(string input)
            {
                if (string.IsNullOrWhiteSpace(input))
                    return null;
                input = input.Trim();
                return translations.ContainsKey(input) ? translations[input] : null;
            }

            public static EntityReference GetTicketType(IOrganizationService service, string value)
            {
                var query = new QueryExpression("new_tickettype")
                {
                    ColumnSet = new ColumnSet("new_tickettypeid", "new_tickettype")
                };
                if (string.IsNullOrEmpty(value))
                    query.Criteria.AddCondition("new_tickettype", ConditionOperator.Null);
                else
                    query.Criteria.AddCondition("new_tickettype", ConditionOperator.Equal, value);
                EntityCollection result = service.RetrieveMultiple(query);
                return null;
            }

           private static readonly Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                { "استفسار", "استفسار" }, { "Inquiry", "استفسار" },
                { "متابعة طلب", "متابعة طلب" }, { "Follow-up Request", "متابعة طلب" },
                { "دعم تقني", "دعم تقني" }, { "Technical Support", "دعم تقني" },
                { "اقتراح", "اقتراح" }, { "Suggestion", "اقتراح" },
                { "شكوى", "شكوى" }, { "Complaint", "شكوى" },
                { "حجز موعد", "حجز موعد" }, { "Meeting request", "حجز موعد" },
                {"اتصال مرئي","اتصال مرئي"},{ "Video Call","اتصال مرئي"}
            };

            private static int GetOptionSetValue(IOrganizationService service, string entityName, string fieldName, string label)
            {
                var response = (RetrieveAttributeResponse)service.Execute(new RetrieveAttributeRequest
                {
                    EntityLogicalName = entityName,
                    LogicalName = fieldName,
                    RetrieveAsIfPublished = true
                });

                var metadata = (PicklistAttributeMetadata)response.AttributeMetadata;
                foreach (var opt in metadata.OptionSet.Options)
                {
                    if (opt.Label.UserLocalizedLabel.Label == label)
                        return opt.Value.Value;
                }

                throw new InvalidPluginExecutionException($"Option '{label}' not found in '{fieldName}' on '{entityName}'");
            }

            static string MatchValueForAppointment(string input, string pattern)
            {
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase);
                return match.Success ? match.Groups[1].Value.Trim() : "Not Found";
            }

            private static string MatchValue(string input, string pattern)
            {
                input = System.Net.WebUtility.HtmlDecode(input);
                input = input.Replace("\u00A0", " ");
                input = Regex.Replace(input, @"\s+", " ");
                var match = Regex.Match(input, pattern, RegexOptions.IgnoreCase | RegexOptions.Multiline);
                return match.Success ? match.Groups[1].Value.Trim() : string.Empty;
            }
        }
    }
