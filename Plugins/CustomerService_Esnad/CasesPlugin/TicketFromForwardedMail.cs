
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Metadata;
using Microsoft.Xrm.Sdk.Query;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;


namespace CasesPlugin
{
    public class TicketFromForwardedMail : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            ITracingService tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            IPluginExecutionContext context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            IOrganizationServiceFactory factory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            IOrganizationService service = factory.CreateOrganizationService(context.UserId);

            try
            {
                if (!context.InputParameters.Contains("Target") || !(context.InputParameters["Target"] is Entity))
                {
                    tracingService.Trace("No target entity found.");
                    return;
                }

                Entity email = (Entity)context.InputParameters["Target"];
                if (email.LogicalName != "email")
                {
                    tracingService.Trace("Target is not an email entity.");
                    return;
                }

                if (email.Attributes.Contains("directioncode")) // 1 = Incoming
                {
                    if (email.Attributes.Contains("from"))
                    {
                        EntityCollection fromCollection = email.GetAttributeValue<EntityCollection>("from");
                        if (fromCollection != null && fromCollection.Entities.Count > 0)
                        {
                            foreach (var activityParty in fromCollection.Entities)
                            {
                                string senderEmail = activityParty.GetAttributeValue<string>("addressused");

                                if (!string.IsNullOrEmpty(senderEmail) &&
                                    senderEmail.Equals("no-reply@taadeen.sa", StringComparison.OrdinalIgnoreCase))
                                {
                                    string plainTextBody = email.GetAttributeValue<string>("description") ?? string.Empty;
                                    string body = StripHtml(plainTextBody);

                                    string subject1 = email.GetAttributeValue<string>("subject");

                                    // ---- BOOK AN APPOINTMENT SECTION ----
                                    if (subject1.ToLower() == "book an appointment" || subject1.ToLower() == "حجز موعد")
                                    {
                                        EntityReference customerRef = null;
                                        //string beneficiaryType = MatchValueForAppointment(body, @"Type of Beneficiary\s*\n(.+)");
                                        //string idNumber = MatchValueForAppointment(body, @"ID Number\s*\n(.+)");
                                        //string fullName = MatchValueForAppointment(body, @"Full Name\s*\n(.+)");
                                        //string phone2 = MatchValueForAppointment(body, @"Phone Number\s*\n(.+)");
                                        //string email1 = MatchValueForAppointment(body, @"Email Address\s*\n(.+)");
                                        //string requestType2 = MatchValueForAppointment(body, @"Request Type\s*\n(.+)");
                                        //string department = MatchValueForAppointment(body, @"Sector\s*\n(.+)");
                                        //string reason = MatchValueForAppointment(body, @"Reason of this request\s*\n(.+)");

                                        string beneficiaryType = MatchValueForAppointment(body, @"(?:Type of Beneficiary|مقدم الطلب)\s*\n(.+)");
                                        string idNumber = MatchValueForAppointment(body, @"(?:ID Number|رقم الهوية)\s*\n(.+)");

                                        string companyName = MatchValueForAppointment(body, @"(?:Company Name|اسم الشركة)\s*\n(.+)");
                                        string CRN = MatchValueForAppointment(body, @"(?:Commercial Registration Number|رقم السجل التجاري)\s*\n(.+)");

                                        string fullName = MatchValueForAppointment(body, @"(?:Full Name|الإسم الثلاثي)\s*\n(.+)");
                                        string phone2 = MatchValueForAppointment(body, @"(?:Phone Number|رقم الهاتف)\s*\n(.+)").Replace("&#43;", "+");
                                        string email1 = MatchValueForAppointment(body, @"(?:Email Address|البريد الإلكتروني)\s*\n(.+)");
                                        if (string.IsNullOrWhiteSpace(email1))
                                        {
                                            return;  // stop plugin execution or skip further logic
                                        }
                                        string requestType2 = MatchValueForAppointment(body, @"(?:Request Type|نوع الموعد)\s*\n(.+)");
                                        string department = MatchValueForAppointment(body, @"(?:Sector|القطاع)\s*\n(.+)");
                                        // string complianceType = MatchValueForAppointment(body, @"(?:Compliance|الامتثال)\s*\n(.+)");
                                        string complianceType = MatchValueForAppointment(
                                                     body,
                                                     @"(?<=\r?\n\r?\n)(?:الامتثال|Compliance)\s*\r?\n\s*([^\r\n]+)"
                                                 );
                                        string isKI = "No";
                                        string licenseType = MatchValueForAppointment(
                                                    body,
                                                    @"(?<=\r?\n\r?\n)(?:الرخص|Licenses)\s*\r?\n\s*([^\r\n]+)"
                                                );

                                        // string licenseType = MatchValueForAppointment(body, @"(?:License Type|الرخص)\s*\n(.+)");
                                        string reason = MatchValueForAppointment(body, @"(?:Reason of this request|سبب حجز الموعد)\s*\n([\s\S]+)"); // Supports multiline
                                        ///------12/9/25-----------------------
                                       // requestType2 = "Inquiry";
                                        string normalizedValue = NormalizeInput(requestType2);

                                        if (beneficiaryType == "فرد" || beneficiaryType.ToLower() == "individual")
                                        {
                                            var query = new QueryExpression("contact")
                                            {
                                                ColumnSet = new ColumnSet("contactid"),
                                                Criteria = new FilterExpression
                                                {
                                                    Conditions = {
                                new ConditionExpression("emailaddress1", ConditionOperator.Equal, email1)
                            }
                                                }
                                            };

                                            var result = service.RetrieveMultiple(query);
                                            Guid contactId;

                                            if (result.Entities.Count > 0)
                                            {
                                                contactId = result.Entities[0].Id;
                                            }
                                            else
                                            {
                                                var contact = new Entity("contact")
                                                {
                                                    ["lastname"] = fullName,
                                                    ["emailaddress1"] = email1,
                                                    ["mobilephone"] = phone2,
                                                    ["new_nationalidnumber"] = idNumber,
                                                };
                                                contactId = service.Create(contact);
                                            }

                                            customerRef = new EntityReference("contact", contactId);
                                        }
                                        //investor
                                        else if (beneficiaryType == "شركة" || beneficiaryType.ToLower() == "company")
                                        {
                                            var query = new QueryExpression("account")
                                            {
                                                ColumnSet = new ColumnSet("accountid"),
                                                Criteria = new FilterExpression
                                                {
                                                    Conditions = {
                                new ConditionExpression("name", ConditionOperator.Equal, companyName)
                            }
                                                }
                                            };

                                            var result = service.RetrieveMultiple(query);
                                            Guid accountId;

                                            if (result.Entities.Count > 0)
                                            {
                                                // account was found
                                                accountId = result.Entities[0].Id;

                                                // Retrieve the account with the relationship manager field (in case it wasn't in the original query)
                                                var existingAccount = service.Retrieve("account", accountId, new ColumnSet("new_relationshipmanager"));

                                                // Check whether new_relationshipmanager lookup is present and non-null
                                                if (existingAccount != null &&
                                                    existingAccount.Contains("new_relationshipmanager") &&
                                                    existingAccount.GetAttributeValue<EntityReference>("new_relationshipmanager") != null)
                                                {
                                                    // set a flag you can use later when creating the incident
                                                    // use the correct field logical name on the incident (here: new_iski)
                                                    isKI = "yes";
                                                }
                                            }
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
                                            Console.WriteLine("❌ Unsupported beneficiary type: " + beneficiaryType);
                                            return;
                                        }
                                        ///------12/9/25-----------------------
                                        /// //----beneficiaryType Unification-----------------------
                                        EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);
                                        int beneficiaryValue = -1;
                                        if (beneficiaryType.Equals("فرد", StringComparison.OrdinalIgnoreCase) ||
                                            beneficiaryType.Equals("Individual", StringComparison.OrdinalIgnoreCase))
                                        {
                                            beneficiaryValue = 1; // فرد / Individual
                                        }
                                        else if (beneficiaryType.Equals("شركة", StringComparison.OrdinalIgnoreCase) ||
                                                 beneficiaryType.Equals("Company", StringComparison.OrdinalIgnoreCase))
                                        {
                                            beneficiaryValue = 2; // شركة / Company
                                        }

                                        //----Sector Unification-----------------------
                                        int SectorValue = -1;
                                        if (department.Equals("الرخص", StringComparison.OrdinalIgnoreCase) ||
                                            department.Equals("Licensing", StringComparison.OrdinalIgnoreCase))
                                        {
                                            SectorValue = 1; // Licensing
                                        }
                                        else if (department.Equals("تجربة العميل", StringComparison.OrdinalIgnoreCase) ||
                                                 department.Equals("Customer Experience", StringComparison.OrdinalIgnoreCase))
                                        {
                                            SectorValue = 3; // Customer Experience
                                        }
                                        else if (department.Equals("الإمتثال", StringComparison.OrdinalIgnoreCase) ||
                                                department.Equals("Compliance", StringComparison.OrdinalIgnoreCase))
                                        {
                                            SectorValue = 2; // Compliance
                                        }
                                        else if (department.Equals("أخرى", StringComparison.OrdinalIgnoreCase) ||
                                                department.Equals("Other", StringComparison.OrdinalIgnoreCase))
                                        {
                                            SectorValue = 4; // Other
                                        }


                                        //----complianceType Unification-----------------------
                                        int ComplianceValue = -1;
                                        if (complianceType.Equals("الامتثال المالي", StringComparison.OrdinalIgnoreCase) ||
                                            complianceType.Equals("Financial Compliance", StringComparison.OrdinalIgnoreCase))
                                        {
                                            ComplianceValue = 1; // Licensing
                                        }
                                        else if (complianceType.Equals("الامتثال الرقابي", StringComparison.OrdinalIgnoreCase) ||
                                                 complianceType.Equals("Regulatory Compliance", StringComparison.OrdinalIgnoreCase))
                                        {
                                            ComplianceValue = 2; // Customer Experience
                                        }
                                        else if (complianceType.Equals("الاستدامة", StringComparison.OrdinalIgnoreCase) ||
                                                 complianceType.Equals("الإستدامة", StringComparison.OrdinalIgnoreCase) ||
                                                complianceType.Equals("Sustainability", StringComparison.OrdinalIgnoreCase))
                                        {
                                            ComplianceValue = 3; // Compliance
                                        }

                                        //----licenseType Unification-----------------------
                                        int licenseTypeValue = -1;
                                        if (licenseType.Equals("رخص الكشف والاستطلاع", StringComparison.OrdinalIgnoreCase) ||
                                            licenseType.Equals("Exploration Licenses", StringComparison.OrdinalIgnoreCase))
                                        {
                                            licenseTypeValue = 1; // Licensing
                                        }
                                        else if (licenseType.Equals("رخص محاجر مواد البناء", StringComparison.OrdinalIgnoreCase) ||
                                                 licenseType.Equals("BMQ Licenses", StringComparison.OrdinalIgnoreCase))
                                        {
                                            licenseTypeValue = 2; // Customer Experience
                                        }
                                        else if (licenseType.Equals("رخص التعدين و المنجم الصغير", StringComparison.OrdinalIgnoreCase) ||
                                                licenseType.Equals("Mining and Small Mine Licenses", StringComparison.OrdinalIgnoreCase) ||
                                                licenseType.Equals("رخص التعدين والمنجم الصغير", StringComparison.OrdinalIgnoreCase))
                                        {
                                            licenseTypeValue = 3; // Compliance
                                        }

                                        var incident = new Entity("incident")
                                        {
                                            ["title"] = subject1,
                                            ["new_tickettype"] = new EntityReference(
                                                                    "new_tickettype",
                                                                    new Guid("89ae520c-1c5e-f011-a408-ceff10794b56")),
                                            ["description"] = reason,
                                            // ["new_requesttype"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_requesttype", requestType2)),
                                            ["new_beneficiarytype"] = new OptionSetValue(beneficiaryValue),
                                            //["new_beneficiarytype"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_beneficiarytype", ConvertBeneficiaryTypeToEnglish(beneficiaryType))),
                                            ["new_ticketsubmissionchannel"] = new OptionSetValue(9),

                                            //["new_compliance"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_compliance", complianceType)),
                                            ["customerid"] = customerRef,
                                            ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))

                                        };
                                        //string normalizedLicense = NormalizeLicenseType(licenseType);
                                        //if (!string.IsNullOrEmpty(normalizedLicense))
                                        //{
                                        //    incident["new_licenses"] = new OptionSetValue(
                                        //        GetOptionSetValue(service, "incident", "new_licenses", normalizedLicense)
                                        //    );
                                        //}
                                        //string normalizedCompliance = NormalizeComplianceType(complianceType);

                                        //if (!string.IsNullOrWhiteSpace(normalizedCompliance))
                                        //{
                                        //    // Only try to set OptionSet if a valid value is found
                                        //    try
                                        //    {
                                        //        incident["new_compliance"] = new OptionSetValue(
                                        //            GetOptionSetValue(service, "incident", "new_compliance", normalizedCompliance)
                                        //        );
                                        //    }
                                        //    catch
                                        //    {
                                        //        // Silently skip if OptionSet value is not found in metadata
                                        //        // Or optionally log to a Note or Trace
                                        //    }
                                        //}
                                        // ➕ Set new_licensetype if sector is Licensing
                                        if ((department.Equals("Licensing", StringComparison.OrdinalIgnoreCase) ||
                                             department.Equals("الرخص", StringComparison.OrdinalIgnoreCase)) &&
                                            !string.IsNullOrWhiteSpace(licenseType))
                                        {
                                            incident["new_licenses"] = new OptionSetValue(licenseTypeValue);
                                            //incident["new_licenses"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_licenses", licenseType));
                                        }
                                        // ➕ Set new_compliancetype if sector is Compliance
                                        else if ((department.Equals("Compliance", StringComparison.OrdinalIgnoreCase) ||
                                                  department.Equals("الامتثال", StringComparison.OrdinalIgnoreCase)) &&
                                                 !string.IsNullOrWhiteSpace(complianceType))
                                        {
                                            //incident["new_compliance"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_compliance", complianceType));
                                            incident["new_compliance"] = new OptionSetValue(ComplianceValue);
                                        }
                                        if (isKI == "yes")
                                        {
                                            incident["new_formtype"] = new OptionSetValue(1);
                                            incident["prioritycode"] = new OptionSetValue(2);
                                        }
                                        else
                                        {
                                            incident["new_formtype"] = new OptionSetValue(0);
                                        }

                                        //incident["new_sector"] = new OptionSetValue(GetOptionSetValue(service, "incident", "new_sector", department));
                                        incident["new_sector"] = new OptionSetValue(SectorValue);
                                        Entity email11 = new Entity("email");
                                        email11.Id = email.Id;
                                        email11.Attributes["regardingobjectid"] = new EntityReference("incident", service.Create(incident));
                                        service.Update(email11);
                                    }

                                    // ---- CONTACT US SECTION ----
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
                                        if (requestType == "دعم فني")
                                        {
                                            requestType = "دعم تقني";
                                        }
                                        string normalizedValue = NormalizeInput(requestType);
                                       string isKI = "No";
                                        EntityReference customerRef;

                                        // === Contact or Account ===
                                        if (beneficiaryType == "فرد" || beneficiaryType.ToLower() == "individual" ||
                                            beneficiaryType == "أخرى" || beneficiaryType.ToLower() == "other")
                                        {
                                            var query = new QueryExpression("contact")
                                            {
                                                ColumnSet = new ColumnSet("contactid"),
                                                Criteria = new FilterExpression
                                                {
                                                    Conditions = {
                                                        new ConditionExpression("emailaddress1", ConditionOperator.Equal, emailAddr)
                                                    }
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
                                                    ["mobilephone"] = phone
                                                };
                                                contactId = service.Create(contact);
                                            }
                                            customerRef = new EntityReference("contact", contactId);
                                        }
                                        else
                                        {
                                            var query = new QueryExpression("account")
                                            {
                                                ColumnSet = new ColumnSet("accountid"),
                                                Criteria = new FilterExpression
                                                {
                                                    Conditions = {
                                                        new ConditionExpression("emailaddress1", ConditionOperator.Equal, emailAddr)
                                                    }
                                                }
                                            };
                                            var result = service.RetrieveMultiple(query);
                                            Guid accountId;
                                            if (result.Entities.Count > 0)
                                            {
                                                // account was found
                                                accountId = result.Entities[0].Id;
                                                
                                                // Retrieve the account with the relationship manager field (in case it wasn't in the original query)
                                                var existingAccount = service.Retrieve("account", accountId, new ColumnSet("new_relationshipmanager"));

                                                // Check whether new_relationshipmanager lookup is present and non-null
                                                if (existingAccount != null &&
                                                    existingAccount.Contains("new_relationshipmanager") &&
                                                    existingAccount.GetAttributeValue<EntityReference>("new_relationshipmanager") != null)
                                                {
                                                    // set a flag you can use later when creating the incident
                                                    // use the correct field logical name on the incident (here: new_iski)
                                                    isKI = "yes";
                                                }
                                            }
                                             

                                            else
                                            {
                                                var account = new Entity("account")
                                                {
                                                    ["name"] = company,
                                                    ["emailaddress1"] = emailAddr,
                                                    ["new_crnumber"] = crNumber
                                                };
                                                accountId = service.Create(account);
                                            }
                                            customerRef = new EntityReference("account", accountId);
                                        }

                                        EntityReference ticketTypeRef = GetTicketType(service, normalizedValue);
                                        int beneficiaryValue = -1;
                                        if (beneficiaryType.Equals("فرد", StringComparison.OrdinalIgnoreCase) ||
                                            beneficiaryType.Equals("Individual", StringComparison.OrdinalIgnoreCase))
                                        {
                                            beneficiaryValue = 1; // فرد / Individual
                                        }
                                        else if (beneficiaryType.Equals("شركة", StringComparison.OrdinalIgnoreCase) ||
                                                 beneficiaryType.Equals("Company", StringComparison.OrdinalIgnoreCase))
                                        {
                                            beneficiaryValue = 2; // شركة / Company
                                        }
                                        else if (beneficiaryType.Equals("مستثمر", StringComparison.OrdinalIgnoreCase) ||
                                                 beneficiaryType.Equals("Investor", StringComparison.OrdinalIgnoreCase))
                                        {
                                            beneficiaryValue = 2; // مستثمر / Investor
                                        }

                                        var incident = new Entity("incident")
                                        {
                                            ["title"] = subject,
                                            ["description"] = message,
                                            ["new_tickettype"] = ticketTypeRef,
                                            ["new_beneficiarytype"] = new OptionSetValue(beneficiaryValue),
                                            ["customerid"] = customerRef,
                                            ["new_ticketsubmissionchannel"] = new OptionSetValue(4),
                                            ["transactioncurrencyid"] = new EntityReference("transactioncurrency", new Guid("70FA9BC3-6D4B-F011-A3FE-D4DE6FAB9C57"))
                                        };
                                        if (isKI == "yes") {
                                            incident["new_formtype"] = new OptionSetValue(1);
                                            incident["prioritycode"] = new OptionSetValue(2);
                                        }
                                        else
                                        {
                                            incident["new_formtype"] = new OptionSetValue(0);
                                        }
                                        Entity updateEmail = new Entity("email")
                                        {
                                            Id = email.Id,
                                            ["regardingobjectid"] = new EntityReference("incident", service.Create(incident))
                                        };
                                        service.Update(updateEmail);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace("Error: " + ex.ToString());
                throw;
            }
        }

        // ===== Helper Methods (same as your console) =====

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

        private static string MatchValueForAppointment(string input, string pattern)
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

            if (result.Entities.Count > 0)
            {
                var record = result.Entities[0];
                string name = record.GetAttributeValue<string>("new_tickettype");
                return new EntityReference("new_tickettype", record.Id) { Name = name };
            }

            return null;
        }

        private static readonly Dictionary<string, string> translations = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
           { "استفسار", "استفسار" }, { "Inquiry", "استفسار" },
            { "متابعة طلب", "متابعة طلب" }, { "Follow-up Request", "متابعة طلب" },
            { "دعم تقني", "دعم تقني" }, { "Technical Support", "دعم تقني" },
            { "اقتراح", "اقتراح" }, { "Suggestion", "اقتراح" },
            { "شكوى", "شكوى" }, { "Complaint", "شكوى" },
            { "مقابلة مسؤول", "مقابلة مسؤول" }, { "Meeting request", "مقابلة مسؤول" },
            {"اتصال مرئي","اتصال مرئي"},{ "Video Call","اتصال مرئي"}
        };
    }
}
