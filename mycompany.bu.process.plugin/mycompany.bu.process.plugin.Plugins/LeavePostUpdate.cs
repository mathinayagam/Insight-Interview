using System;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using mycompany.bu.process.plugin.Plugins.BusinessLayer;
namespace mycompany.bu.process.plugin.Plugins
{
    public partial class LeavePostUpdate : BasePlugin
    {
        public LeavePostUpdate(string unsecureConfig, string secureConfig) : base(unsecureConfig, secureConfig)
        {
            // Register for any specific events by instantiating a new instance of the 'PluginEvent' class and registering it
            base.RegisteredEvents.Add(new PluginEvent()
            {
                Stage = eStage.PostOperation,
                MessageName = MessageNames.Update,
                EntityName = EntityNames.new_leaverequests,
                PluginAction = ExecutePluginLogic
            });
        }
        public void ExecutePluginLogic(IServiceProvider serviceProvider)
        {
            // Use a 'using' statement to dispose of the service context properly
            // To use a specific early bound entity replace the 'Entity' below with the appropriate class type
            using (var localContext = new LocalPluginContext<Entity>(serviceProvider))
            {
                // Todo: Place your logic here for the plugin
                IOrganizationService orgser = localContext.OrganizationService;
                OrganizationServiceContext orgsercon = localContext.CrmContext;
                try
                {
                    var LeaveLogic = new LeaveLogic(orgser, orgsercon);
                    Entity leaverequest = (Entity)localContext.TargetEntity;
                    LeaveLogic.UpdateApprovedLeave(leaverequest);
                }
                catch (Exception ex)
                {
                    throw ex;
                }

            }
        }
        
    }
}
