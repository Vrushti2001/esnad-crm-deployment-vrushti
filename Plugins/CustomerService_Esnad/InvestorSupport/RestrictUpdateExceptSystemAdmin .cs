
using System;
using System.Linq;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Query;

namespace InvestorSupport
{
    public class RestrictUpdateExceptSystemAdmin : IPlugin
    {
        public void Execute(IServiceProvider serviceProvider)
        {
            var context = (IPluginExecutionContext)
                serviceProvider.GetService(typeof(IPluginExecutionContext));

            // Only for Update message
            if (!context.MessageName.Equals("Update", StringComparison.OrdinalIgnoreCase))
                return;

            var serviceFactory = (IOrganizationServiceFactory)
                serviceProvider.GetService(typeof(IOrganizationServiceFactory));

            var service = serviceFactory.CreateOrganizationService(context.UserId);

            // Check if user is System Administrator
            if (!IsSystemAdministrator(service, context.UserId))
            {
                throw new InvalidPluginExecutionException(
                    "You are not authorized to update this record."
                );
            }
        }

        private bool IsSystemAdministrator(IOrganizationService service, Guid userId)
        {
            var query = new QueryExpression("systemuserroles")
            {
                ColumnSet = new ColumnSet("roleid"),
                Criteria =
                {
                    Conditions =
                    {
                        new ConditionExpression("systemuserid", ConditionOperator.Equal, userId)
                    }
                },
                LinkEntities =
                {
                    new LinkEntity
                    {
                        LinkFromEntityName = "systemuserroles",
                        LinkFromAttributeName = "roleid",
                        LinkToEntityName = "role",
                        LinkToAttributeName = "roleid",
                        Columns = new ColumnSet("name"),
                        EntityAlias = "role"
                    }
                }
            };

            var result = service.RetrieveMultiple(query);

            return result.Entities.Any(e =>
                e.GetAttributeValue<AliasedValue>("role.name")?.Value
                    .ToString()
                    .Equals("System Administrator", StringComparison.OrdinalIgnoreCase) == true
            );
        }
    }
}
