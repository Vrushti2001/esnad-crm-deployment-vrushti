using Microsoft.Crm.Sdk.Messages;
using Microsoft.Xrm.Sdk.Query;
using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IdentityModel.Metadata;
using System.Net.Sockets;
using System.Numerics;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows;
using System.Xml.Linq;
using System.Net.NetworkInformation;

namespace InvestorSupport
{
    public class SendCaseReplyNotification : IPlugin
    {
        // Email to CST team that Customer had been replied.
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));
            var serviceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));
            var service = serviceFactory.CreateOrganizationService(context.UserId);
            var tracing = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

            tracing.Trace("🔔 Plugin execution started.");

            try
            {
                if (!context.InputParameters.Contains("CaseId") || !(context.InputParameters["CaseId"] is EntityReference caseRef))
                    throw new InvalidPluginExecutionException("Missing or invalid 'CaseId' input parameter.");

                if (!context.InputParameters.Contains("TeamId") || !(context.InputParameters["TeamId"] is EntityReference teamRef))
                    throw new InvalidPluginExecutionException("Missing or invalid 'TeamId' input parameter.");

                var caseId = caseRef.Id;
                var teamId = teamRef.Id;

                // Get case title
                var caseEntity = service.Retrieve("incident", caseId, new ColumnSet("title", "ticketnumber", "new_companeyname"));
                string caseTitle = caseEntity.GetAttributeValue<string>("title") ?? "Unknown";
                string Ticketnumber = caseEntity.GetAttributeValue<string>("ticketnumber") ?? " ";
                string companyName = caseEntity.GetAttributeValue<string>("new_companeyname") ?? "";


                // Get all users in the team (teammembership)
                var teamUsersQuery = new QueryExpression("teammembership")
                {
                    ColumnSet = new ColumnSet("systemuserid"),
                    Criteria = new FilterExpression
                    {
                        Conditions = {
                            new ConditionExpression("teamid", ConditionOperator.Equal, teamId)
                        }
                    }
                };

                var userIds = service.RetrieveMultiple(teamUsersQuery)
                    .Entities
                    .Select(e => e.GetAttributeValue<Guid>("systemuserid"))
                    .Distinct()
                    .ToList();

                if (!userIds.Any())
                {
                    tracing.Trace("❌ No users found in the team.");
                    return;
                }

                // -------------------
                // NEW: retrieve only users that have an internalemailaddress (are emailable)
                // -------------------
                var emailableUsers = new List<Entity>();
                try
                {
                    // Query systemuser for those userIds and include internalemailaddress
                    var usersQuery = new QueryExpression("systemuser")
                    {
                        ColumnSet = new ColumnSet("systemuserid", "internalemailaddress"),
                        Criteria = new FilterExpression
                        {
                            FilterOperator = LogicalOperator.And,
                            Conditions =
                            {
                                // ConditionOperator.In supports array of GUIDs
                                new ConditionExpression("systemuserid", ConditionOperator.In, userIds.Cast<object>().ToArray())
                            }
                        }
                    };

                    var usersResult = service.RetrieveMultiple(usersQuery);
                    emailableUsers = usersResult.Entities
                        .Where(u => !string.IsNullOrEmpty(u.GetAttributeValue<string>("internalemailaddress")))
                        .ToList();
                }
                catch (Exception ex)
                {
                    tracing.Trace("⚠️ Error while retrieving systemusers: " + ex.Message);
                    // proceed — if this fails, we won't have emailable users
                    emailableUsers = new List<Entity>();
                }

                if (!emailableUsers.Any())
                {
                    tracing.Trace("❌ No emailable users found in the team. Aborting send (no recipients).");
                    return;
                }

                // Build 'To' recipients from emailable users only
                var toParties = emailableUsers.Select(u => new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", u.Id)
                }).ToList();

                // Get CRM Admin user
                var crmAdmin = service.RetrieveMultiple(new QueryExpression("systemuser")
                {
                    ColumnSet = new ColumnSet("systemuserid", "internalemailaddress"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression("domainname", ConditionOperator.Equal, "CRM-ESNAD\\crmadmin"),
                            new ConditionExpression("accessmode", ConditionOperator.Equal, 0)
                        }
                    }
                }).Entities.FirstOrDefault();

                if (crmAdmin == null || !crmAdmin.Contains("internalemailaddress"))
                    throw new InvalidPluginExecutionException("CRM Admin user not found or missing email.");

                var fromParty = new Entity("activityparty")
                {
                    ["partyid"] = new EntityReference("systemuser", crmAdmin.Id)
                };

                // Build email   
                string orgUrl = GetOrgURL(service, tracing);
                string caseUrl = $"{orgUrl}{caseId}";
                string caseTitleHtml = $"<a href='{caseUrl}' style='color:#0078d4; font-weight:bold;'>{Ticketnumber}</a>";
                string logoUrl = "https://feedback-dev.crm-esnad.com/Esnad-Logo.jpg";


                string emailBody = $@"
                        <html>
                          <body style='font-family: Segoe UI, Tahoma, sans-serif; font-size:14px;'>

                            <!-- Arabic section -->
                            <div dir='rtl' style='text-align:right; margin-bottom:20px;'>
                              <p>مدير العلاقة،</p>

                              <p>
                                تم الرد على التذكرة 
                                <b>{caseTitleHtml}</b>
                                لشركة 
                                <b>{companyName}</b>.
                              </p>

                              <p>
                                يرجى التحقق من الحل واغلاق التذكرة وفقاً لإتفاقية مستوى الخدمة (SLA) المعتمدة.
                              </p>
                            </div>

                            <p>
                              <img src='{logoUrl}' alt='CRM Logo' style='width:200px; margin-bottom:10px;' />
                            </p>

                          </body>
                        </html>";


                var email = new Entity("email")
                {
                    ["subject"] = $"Customer Response for - {caseTitle}",
                    ["description"] = emailBody,
                    ["directioncode"] = true,
                    ["from"] = new EntityCollection(new[] { fromParty }),
                    ["to"] = new EntityCollection(toParties),
                    ["regardingobjectid"] = new EntityReference("incident", caseId),
                    ["statuscode"] = new OptionSetValue(1) // Draft
                };

                Guid emailId = service.Create(email);
                tracing.Trace("✅ Email created. ID: " + emailId);

                var sendRequest = new SendEmailRequest
                {
                    EmailId = emailId,
                    IssueSend = true,
                    TrackingToken = ""
                };

                service.Execute(sendRequest);
                tracing.Trace("✅ Email sent via SendEmailRequest.");

                // Update case
                var updateCase = new Entity("incident", caseId)
                {
                    ["new_copycaseguid"] = caseId.ToString()
                };
                service.Update(updateCase);
                tracing.Trace("✅ Case updated with new_copycaseguid.");

            }
            catch (Exception ex)
            {
                tracing.Trace("❌ Exception: " + ex.ToString());
                throw new InvalidPluginExecutionException("Error in SendCaseReplyNotificationPlugin.", ex);
            }

            tracing.Trace("🏁 Plugin execution completed.");
        }

        private string GetOrgURL(IOrganizationService service, ITracingService tracing)
        {
            var query = new QueryExpression("new_environmentvariable")
            {
                ColumnSet = new ColumnSet("new_value"),
                Criteria = new FilterExpression
                {
                    Conditions =
                    {
                        new ConditionExpression("new_name", ConditionOperator.Equal, "OrgURL")
                    }
                }
            };

            var result = service.RetrieveMultiple(query);
            if (result.Entities.Count > 0)
            {
                return result.Entities[0].GetAttributeValue<string>("new_value");
            }

            tracing.Trace("❌ OrgURL environment variable not found.");
            throw new InvalidPluginExecutionException("OrgURL environment variable missing.");
        }
    }
}
