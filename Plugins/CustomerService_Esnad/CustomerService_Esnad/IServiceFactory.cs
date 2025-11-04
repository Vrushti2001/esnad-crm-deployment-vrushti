using Microsoft.Xrm.Sdk;
using System;

namespace Taadeen.Crm.Plugins
{
    internal interface IServiceFactory
    {
        IOrganizationService CreateOrganizationService(Guid userId);
    }
}