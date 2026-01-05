using System;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace InvestorSupport
    {
        public class PreventDeleteAnyEntity : IPlugin
        {
            // Logical name of System Administrator role
            private const string SystemAdministratorRoleName = "System Administrator";

            public void Execute(IServiceProvider serviceProvider)
            {
                var context = (IPluginExecutionContext)
                    serviceProvider.GetService(typeof(IPluginExecutionContext));

                // Only for Delete
                if (!context.MessageName.Equals("Delete", StringComparison.OrdinalIgnoreCase))
                    return;

                // Safety for internal recursion
                if (context.Depth > 1)
                    return;

                var factory = (IOrganizationServiceFactory)
                    serviceProvider.GetService(typeof(IOrganizationServiceFactory));

                var service = factory.CreateOrganizationService(context.UserId);

                Guid userId = context.InitiatingUserId;

                // ✔ Check if user is System Administrator
                if (IsSystemAdministrator(service, userId))
                    return; // ✅ allow delete

                // ❌ block everyone else
                throw new InvalidPluginExecutionException(
                    "You are not allowed to delete this record. Only System Administrators can delete records."
                );
            }

            private bool IsSystemAdministrator(IOrganizationService service, Guid userId)
            {
                var query = new QueryExpression("role")
                {
                    ColumnSet = new ColumnSet("name")
                };

                query.LinkEntities.Add(new LinkEntity(
                    "role",
                    "systemuserroles",
                    "roleid",
                    "roleid",
                    JoinOperator.Inner)
                {
                    LinkEntities =
                {
                    new LinkEntity(
                        "systemuserroles",
                        "systemuser",
                        "systemuserid",
                        "systemuserid",
                        JoinOperator.Inner)
                    {
                        LinkCriteria =
                        {
                            Conditions =
                            {
                                new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                            }
                        }
                    }
                }
                });

                query.Criteria.AddCondition("name", ConditionOperator.Equal, SystemAdministratorRoleName);

                var result = service.RetrieveMultiple(query);
                return result.Entities.Count > 0;
            }
        }
    }

