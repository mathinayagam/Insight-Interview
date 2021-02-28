using Microsoft.Xrm.Sdk;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.ServiceModel;
using System.Text;
using System.Threading.Tasks;

namespace mycompany.bu.process.plugin.Plugins
{
    public abstract partial class BasePlugin : IPlugin
    {
        protected class LocalPluginContext<T> : IDisposable where T : Entity
        {
            internal Microsoft.Xrm.Sdk.Client.OrganizationServiceContext CrmContext { get; private set; }
            internal IServiceProvider ServiceProvider { get; private set; }
            internal IOrganizationServiceFactory ServiceFactory { get; private set; }
            internal IOrganizationService OrganizationService { get; private set; }
            internal IPluginExecutionContext PluginExecutionContext { get; private set; }
            internal ITracingService TracingService { get; private set; }
            internal eStage Stage { get { return (eStage)this.PluginExecutionContext.Stage; } }
            internal int Depth { get { return this.PluginExecutionContext.Depth; } }
            internal string MessageName { get { return this.PluginExecutionContext.MessageName; } }
            internal LocalPluginContext(IServiceProvider serviceProvider)
            {
                if (serviceProvider == null)
                    throw new ArgumentNullException("serviceProvider");

                // Obtain the tracing service from the service provider.
                this.TracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));

                // Obtain the execution context service from the service provider.
                this.PluginExecutionContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

                // Obtain the Organization Service factory service from the service provider
                this.ServiceFactory = (IOrganizationServiceFactory)serviceProvider.GetService(typeof(IOrganizationServiceFactory));

                // Use the factory to generate the Organization Service.
                this.OrganizationService = this.ServiceFactory.CreateOrganizationService(this.PluginExecutionContext.UserId);

                // Generate the CrmContext to use with LINQ etc
                this.CrmContext = new Microsoft.Xrm.Sdk.Client.OrganizationServiceContext(this.OrganizationService);
            }

            internal void Trace(string message)
            {
                if (string.IsNullOrWhiteSpace(message) || this.TracingService == null) return;

                if (this.PluginExecutionContext == null)
                    this.TracingService.Trace(message);
                else
                {
                    this.TracingService.Trace(
                        "{0}, Correlation Id: {1}, Initiating User: {2}",
                        message,
                        this.PluginExecutionContext.CorrelationId,
                        this.PluginExecutionContext.InitiatingUserId);
                }
            }

            public void Dispose()
            {
                if (this.CrmContext != null)
                    this.CrmContext.Dispose();
            }
            /// <summary>
            /// Returns the first registered 'Pre' image for the pipeline execution
            /// </summary>
            internal T PreImage
            {
                get
                {
                    if (this.PluginExecutionContext.PreEntityImages.Any())
                        return GetEntityAsType(this.PluginExecutionContext.PreEntityImages[this.PluginExecutionContext.PreEntityImages.FirstOrDefault().Key]);
                    return null;
                }
            }
            /// <summary>
            /// Returns the first registered 'Post' image for the pipeline execution
            /// </summary>
            internal T PostImage
            {
                get
                {
                    if (this.PluginExecutionContext.PostEntityImages.Any())
                        return GetEntityAsType(this.PluginExecutionContext.PostEntityImages[this.PluginExecutionContext.PostEntityImages.FirstOrDefault().Key]);
                    return null;
                }
            }
            /// <summary>
            /// Returns the 'Target' of the message if available
            /// This is an 'Entity' instead of the specified type in order to retain the same instance of the 'Entity' object. This allows for updates to the target in a 'Pre' stage that
            /// will get persisted during the transaction.
            /// </summary>
            internal Entity TargetEntity
            {
                get
                {
                    if (this.PluginExecutionContext.InputParameters.Contains("Target"))
                        return this.PluginExecutionContext.InputParameters["Target"] as Entity;
                    return null;
                }
            }
            /// <summary>
            /// Returns the 'Target' of the message as an EntityReference if available
            /// </summary>
            internal EntityReference TargetEntityReference
            {
                get
                {
                    if (this.PluginExecutionContext.InputParameters.Contains("Target"))
                        return this.PluginExecutionContext.InputParameters["Target"] as EntityReference;
                    return null;
                }
            }
            private T GetEntityAsType(Entity entity)
            {
                if (typeof(T) == entity.GetType())
                    return entity as T;
                else
                    return entity.ToEntity<T>();
            }
        }
        protected enum eStage
        {
            PreValidation = 10,
            PreOperation = 20,
            PostOperation = 40
        }
        protected class PluginEvent
        {
            /// <summary>
            /// Execution pipeline stage that the plugin should be registered against.
            /// </summary>
            public eStage Stage { get; set; }
            /// <summary>
            /// Logical name of the entity that the plugin should be registered against. Leave 'null' to register against all entities.
            /// </summary>
            public string EntityName { get; set; }
            /// <summary>
            /// Name of the message that the plugin should be triggered off of.
            /// </summary>
            public string MessageName { get; set; }
            /// <summary>
            /// Method that should be executed when the conditions of the Plugin Event have been met.
            /// </summary>
            public Action<IServiceProvider> PluginAction { get; set; }
        }

        private Collection<PluginEvent> registeredEvents;

        /// <summary>
        /// Gets the List of events that the plug-in should fire for. Each List
        /// </summary>
        protected Collection<PluginEvent> RegisteredEvents
        {
            get
            {
                if (this.registeredEvents == null)
                    this.registeredEvents = new Collection<PluginEvent>();
                return this.registeredEvents;
            }
        }

        /// <summary>
        /// Initializes a new instance of the BasePlugin class.
        /// </summary>
        internal BasePlugin(string unsecureConfig, string secureConfig)
        {
            this.UnsecureConfig = unsecureConfig;
            this.SecureConfig = secureConfig;
        }
        /// <summary>
        /// Un secure configuration specified during the registration of the plugin step
        /// </summary>
        public string UnsecureConfig { get; private set; }

        /// <summary>
        /// Secure configuration specified during the registration of the plugin step
        /// </summary>
        public string SecureConfig { get; private set; }

        /// <summary>
        /// Executes the plug-in.
        /// </summary>
        /// <param name="serviceProvider">The service provider.</param>
        /// <remarks>
        /// For improved performance, Microsoft Dynamics CRM caches plug-in instances. 
        /// The plug-in's Execute method should be written to be stateless as the constructor 
        /// is not called for every invocation of the plug-in. Also, multiple system threads 
        /// could execute the plug-in at the same time. All per invocation state information 
        /// is stored in the context. This means that you should not use global variables in plug-ins.
        /// </remarks>
        public void Execute(IServiceProvider serviceProvider)
        {
            if (serviceProvider == null)
                throw new ArgumentNullException("serviceProvider");

            var tracingService = (ITracingService)serviceProvider.GetService(typeof(ITracingService));
            var pluginContext = (IPluginExecutionContext)serviceProvider.GetService(typeof(IPluginExecutionContext));

            tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Entered {0}.Execute()", this.GetType().ToString()));
            try
            {
                // Iterate over all of the expected registered events to ensure that the plugin
                // has been invoked by an expected event
                var entityActions =
                    (from a in this.RegisteredEvents
                     where (
                        (int)a.Stage == pluginContext.Stage &&
                         (string.IsNullOrWhiteSpace(a.MessageName) ? true : a.MessageName.ToLowerInvariant() == pluginContext.MessageName.ToLowerInvariant()) &&
                         (string.IsNullOrWhiteSpace(a.EntityName) ? true : a.EntityName.ToLowerInvariant() == pluginContext.PrimaryEntityName.ToLowerInvariant())
                     )
                     select a.PluginAction);

                if (entityActions.Any())
                {
                    foreach (var entityAction in entityActions)
                    {
                        tracingService.Trace(string.Format(
                            CultureInfo.InvariantCulture,
                            "{0} is firing for Entity: {1}, Message: {2}, Method: {3}",
                            this.GetType().ToString(),
                            pluginContext.PrimaryEntityName,
                            pluginContext.MessageName,
                            entityAction.Method.Name));

                        entityAction.Invoke(serviceProvider);
                    }
                }
            }
            catch (Exception ex)
            {
                tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Exception: {0}", ex.ToString()));
                throw;
            }
            finally
            {
                tracingService.Trace(string.Format(CultureInfo.InvariantCulture, "Exiting {0}.Execute()", this.GetType().ToString()));
            }
        }
    }
    public struct EntityNames
    {
        public static readonly string account = "account";
  public static readonly string aciviewmapper = "aciviewmapper";
  public static readonly string actioncard = "actioncard";
  public static readonly string actioncardusersettings = "actioncardusersettings";
  public static readonly string actioncarduserstate = "actioncarduserstate";
  public static readonly string activitymimeattachment = "activitymimeattachment";
  public static readonly string activityparty = "activityparty";
  public static readonly string activitypointer = "activitypointer";
  public static readonly string advancedsimilarityrule = "advancedsimilarityrule";
  public static readonly string annotation = "annotation";
  public static readonly string annualfiscalcalendar = "annualfiscalcalendar";
  public static readonly string appconfig = "appconfig";
  public static readonly string appconfiginstance = "appconfiginstance";
  public static readonly string appconfigmaster = "appconfigmaster";
  public static readonly string appelement = "appelement";
  public static readonly string applicationfile = "applicationfile";
  public static readonly string applicationuser = "applicationuser";
  public static readonly string applicationuserprofile = "applicationuserprofile";
  public static readonly string applicationuserrole = "applicationuserrole";
  public static readonly string appmodule = "appmodule";
  public static readonly string appmodulecomponent = "appmodulecomponent";
  public static readonly string appmodulecomponentedge = "appmodulecomponentedge";
  public static readonly string appmodulecomponentnode = "appmodulecomponentnode";
  public static readonly string appmodulemetadata = "appmodulemetadata";
  public static readonly string appmodulemetadatadependency = "appmodulemetadatadependency";
  public static readonly string appmodulemetadataoperationlog = "appmodulemetadataoperationlog";
  public static readonly string appmoduleroles = "appmoduleroles";
  public static readonly string appointment = "appointment";
  public static readonly string appsetting = "appsetting";
  public static readonly string appusersetting = "appusersetting";
  public static readonly string asyncoperation = "asyncoperation";
  public static readonly string attachment = "attachment";
  public static readonly string attribute = "attribute";
  public static readonly string attributeimageconfig = "attributeimageconfig";
  public static readonly string attributemap = "attributemap";
  public static readonly string audit = "audit";
  public static readonly string authorizationserver = "authorizationserver";
  public static readonly string azureserviceconnection = "azureserviceconnection";
  public static readonly string bot = "bot";
  public static readonly string bot_botcomponent = "bot_botcomponent";
  public static readonly string botcomponent = "botcomponent";
  public static readonly string botcomponent_botcomponent = "botcomponent_botcomponent";
  public static readonly string botcomponent_workflow = "botcomponent_workflow";
  public static readonly string bulkdeletefailure = "bulkdeletefailure";
  public static readonly string bulkdeleteoperation = "bulkdeleteoperation";
  public static readonly string businessdatalocalizedlabel = "businessdatalocalizedlabel";
  public static readonly string businessprocessflowinstance = "businessprocessflowinstance";
  public static readonly string businessunit = "businessunit";
  public static readonly string businessunitmap = "businessunitmap";
  public static readonly string businessunitnewsarticle = "businessunitnewsarticle";
  public static readonly string c360_cidatasource = "c360_cidatasource";
  public static readonly string c360_configuration = "c360_configuration";
  public static readonly string calendar = "calendar";
  public static readonly string calendarrule = "calendarrule";
  public static readonly string callbackregistration = "callbackregistration";
  public static readonly string canvasapp = "canvasapp";
  public static readonly string canvasappextendedmetadata = "canvasappextendedmetadata";
  public static readonly string cardtype = "cardtype";
  public static readonly string cascadegrantrevokeaccessrecordstracker = "cascadegrantrevokeaccessrecordstracker";
  public static readonly string cascadegrantrevokeaccessversiontracker = "cascadegrantrevokeaccessversiontracker";
  public static readonly string catalog = "catalog";
  public static readonly string catalogassignment = "catalogassignment";
  public static readonly string category = "category";
  public static readonly string channelaccessprofile = "channelaccessprofile";
  public static readonly string channelaccessprofileentityaccesslevel = "channelaccessprofileentityaccesslevel";
  public static readonly string channelaccessprofilerule = "channelaccessprofilerule";
  public static readonly string channelaccessprofileruleitem = "channelaccessprofileruleitem";
  public static readonly string channelproperty = "channelproperty";
  public static readonly string channelpropertygroup = "channelpropertygroup";
  public static readonly string clientupdate = "clientupdate";
  public static readonly string columnmapping = "columnmapping";
  public static readonly string complexcontrol = "complexcontrol";
  public static readonly string connection = "connection";
  public static readonly string connectionreference = "connectionreference";
  public static readonly string connectionrole = "connectionrole";
  public static readonly string connectionroleassociation = "connectionroleassociation";
  public static readonly string connectionroleobjecttypecode = "connectionroleobjecttypecode";
  public static readonly string connector = "connector";
  public static readonly string contact = "contact";
  public static readonly string conversationtranscript = "conversationtranscript";
  public static readonly string convertrule = "convertrule";
  public static readonly string convertruleitem = "convertruleitem";
  public static readonly string customapi = "customapi";
  public static readonly string customapirequestparameter = "customapirequestparameter";
  public static readonly string customapiresponseproperty = "customapiresponseproperty";
  public static readonly string customcontrol = "customcontrol";
  public static readonly string customcontroldefaultconfig = "customcontroldefaultconfig";
  public static readonly string customcontrolresource = "customcontrolresource";
  public static readonly string customeraddress = "customeraddress";
  public static readonly string customerrelationship = "customerrelationship";
  public static readonly string datalakefolder = "datalakefolder";
  public static readonly string datalakefolderpermission = "datalakefolderpermission";
  public static readonly string datalakeworkspace = "datalakeworkspace";
  public static readonly string datalakeworkspacepermission = "datalakeworkspacepermission";
  public static readonly string dataperformance = "dataperformance";
  public static readonly string delveactionhub = "delveactionhub";
  public static readonly string dependency = "dependency";
  public static readonly string dependencyfeature = "dependencyfeature";
  public static readonly string dependencynode = "dependencynode";
  public static readonly string displaystring = "displaystring";
  public static readonly string displaystringmap = "displaystringmap";
  public static readonly string documentindex = "documentindex";
  public static readonly string documenttemplate = "documenttemplate";
  public static readonly string duplicaterecord = "duplicaterecord";
  public static readonly string duplicaterule = "duplicaterule";
  public static readonly string duplicaterulecondition = "duplicaterulecondition";
  public static readonly string email = "email";
  public static readonly string emailhash = "emailhash";
  public static readonly string emailsearch = "emailsearch";
  public static readonly string emailserverprofile = "emailserverprofile";
  public static readonly string emailsignature = "emailsignature";
  public static readonly string entity = "entity";
  public static readonly string entityanalyticsconfig = "entityanalyticsconfig";
  public static readonly string entitydataprovider = "entitydataprovider";
  public static readonly string entitydatasource = "entitydatasource";
  public static readonly string entityimageconfig = "entityimageconfig";
  public static readonly string entitykey = "entitykey";
  public static readonly string entitymap = "entitymap";
  public static readonly string entityrelationship = "entityrelationship";
  public static readonly string environmentvariabledefinition = "environmentvariabledefinition";
  public static readonly string environmentvariablevalue = "environmentvariablevalue";
  public static readonly string exchangesyncidmapping = "exchangesyncidmapping";
  public static readonly string expanderevent = "expanderevent";
  public static readonly string expiredprocess = "expiredprocess";
  public static readonly string exportsolutionupload = "exportsolutionupload";
  public static readonly string externalparty = "externalparty";
  public static readonly string externalpartyitem = "externalpartyitem";
  public static readonly string fax = "fax";
  public static readonly string feedback = "feedback";
  public static readonly string fieldpermission = "fieldpermission";
  public static readonly string fieldsecurityprofile = "fieldsecurityprofile";
  public static readonly string fileattachment = "fileattachment";
  public static readonly string filtertemplate = "filtertemplate";
  public static readonly string fixedmonthlyfiscalcalendar = "fixedmonthlyfiscalcalendar";
  public static readonly string flowsession = "flowsession";
  public static readonly string globalsearchconfiguration = "globalsearchconfiguration";
  public static readonly string goal = "goal";
  public static readonly string goalrollupquery = "goalrollupquery";
  public static readonly string hierarchyrule = "hierarchyrule";
  public static readonly string hierarchysecurityconfiguration = "hierarchysecurityconfiguration";
  public static readonly string holidaywrapper = "holidaywrapper";
  public static readonly string imagedescriptor = "imagedescriptor";
  public static readonly string import = "import";
  public static readonly string importdata = "importdata";
  public static readonly string importentitymapping = "importentitymapping";
  public static readonly string importfile = "importfile";
  public static readonly string importjob = "importjob";
  public static readonly string importlog = "importlog";
  public static readonly string importmap = "importmap";
  public static readonly string integrationstatus = "integrationstatus";
  public static readonly string interactionforemail = "interactionforemail";
  public static readonly string internaladdress = "internaladdress";
  public static readonly string internalcatalogassignment = "internalcatalogassignment";
  public static readonly string interprocesslock = "interprocesslock";
  public static readonly string invaliddependency = "invaliddependency";
  public static readonly string isvconfig = "isvconfig";
  public static readonly string kbarticle = "kbarticle";
  public static readonly string kbarticlecomment = "kbarticlecomment";
  public static readonly string kbarticletemplate = "kbarticletemplate";
  public static readonly string knowledgearticle = "knowledgearticle";
  public static readonly string knowledgearticlescategories = "knowledgearticlescategories";
  public static readonly string knowledgearticleviews = "knowledgearticleviews";
  public static readonly string knowledgebaserecord = "knowledgebaserecord";
  public static readonly string knowledgesearchmodel = "knowledgesearchmodel";
  public static readonly string languagelocale = "languagelocale";
  public static readonly string languageprovisioningstate = "languageprovisioningstate";
  public static readonly string letter = "letter";
  public static readonly string license = "license";
  public static readonly string localconfigstore = "localconfigstore";
  public static readonly string lookupmapping = "lookupmapping";
  public static readonly string mailbox = "mailbox";
  public static readonly string mailboxstatistics = "mailboxstatistics";
  public static readonly string mailboxtrackingcategory = "mailboxtrackingcategory";
  public static readonly string mailboxtrackingfolder = "mailboxtrackingfolder";
  public static readonly string mailmergetemplate = "mailmergetemplate";
  public static readonly string managedproperty = "managedproperty";
  public static readonly string metadatadifference = "metadatadifference";
  public static readonly string metric = "metric";
  public static readonly string mobileofflineprofile = "mobileofflineprofile";
  public static readonly string mobileofflineprofileitem = "mobileofflineprofileitem";
  public static readonly string mobileofflineprofileitemassociation = "mobileofflineprofileitemassociation";
  public static readonly string monthlyfiscalcalendar = "monthlyfiscalcalendar";
  public static readonly string msdyn_aibdataset = "msdyn_aibdataset";
  public static readonly string msdyn_aibdatasetfile = "msdyn_aibdatasetfile";
  public static readonly string msdyn_aibdatasetrecord = "msdyn_aibdatasetrecord";
  public static readonly string msdyn_aibdatasetscontainer = "msdyn_aibdatasetscontainer";
  public static readonly string msdyn_aibfile = "msdyn_aibfile";
  public static readonly string msdyn_aibfileattacheddata = "msdyn_aibfileattacheddata";
  public static readonly string msdyn_aiconfiguration = "msdyn_aiconfiguration";
  public static readonly string msdyn_aifptrainingdocument = "msdyn_aifptrainingdocument";
  public static readonly string msdyn_aimodel = "msdyn_aimodel";
  public static readonly string msdyn_aiodimage = "msdyn_aiodimage";
  public static readonly string msdyn_aiodlabel = "msdyn_aiodlabel";
  public static readonly string msdyn_aiodlabel_msdyn_aiconfiguration = "msdyn_aiodlabel_msdyn_aiconfiguration";
  public static readonly string msdyn_aiodtrainingboundingbox = "msdyn_aiodtrainingboundingbox";
  public static readonly string msdyn_aiodtrainingimage = "msdyn_aiodtrainingimage";
  public static readonly string msdyn_aitemplate = "msdyn_aitemplate";
  public static readonly string msdyn_analysiscomponent = "msdyn_analysiscomponent";
  public static readonly string msdyn_analysisjob = "msdyn_analysisjob";
  public static readonly string msdyn_analysisresult = "msdyn_analysisresult";
  public static readonly string msdyn_analysisresultdetail = "msdyn_analysisresultdetail";
  public static readonly string msdyn_componentlayer = "msdyn_componentlayer";
  public static readonly string msdyn_componentlayerdatasource = "msdyn_componentlayerdatasource";
  public static readonly string msdyn_dataflow = "msdyn_dataflow";
  public static readonly string msdyn_federatedarticle = "msdyn_federatedarticle";
  public static readonly string msdyn_federatedarticleincident = "msdyn_federatedarticleincident";
  public static readonly string msdyn_helppage = "msdyn_helppage";
  public static readonly string msdyn_kmfederatedsearchconfig = "msdyn_kmfederatedsearchconfig";
  public static readonly string msdyn_knowledgearticleimage = "msdyn_knowledgearticleimage";
  public static readonly string msdyn_knowledgearticletemplate = "msdyn_knowledgearticletemplate";
  public static readonly string msdyn_knowledgeinteractioninsight = "msdyn_knowledgeinteractioninsight";
  public static readonly string msdyn_knowledgesearchinsight = "msdyn_knowledgesearchinsight";
  public static readonly string msdyn_nonrelationalds = "msdyn_nonrelationalds";
  public static readonly string msdyn_odatav4ds = "msdyn_odatav4ds";
  public static readonly string msdyn_richtextfile = "msdyn_richtextfile";
  public static readonly string msdyn_serviceconfiguration = "msdyn_serviceconfiguration";
  public static readonly string msdyn_slakpi = "msdyn_slakpi";
  public static readonly string msdyn_solutioncomponentdatasource = "msdyn_solutioncomponentdatasource";
  public static readonly string msdyn_solutioncomponentsummary = "msdyn_solutioncomponentsummary";
  public static readonly string msdyn_solutionhealthrule = "msdyn_solutionhealthrule";
  public static readonly string msdyn_solutionhealthruleargument = "msdyn_solutionhealthruleargument";
  public static readonly string msdyn_solutionhealthruleset = "msdyn_solutionhealthruleset";
  public static readonly string msdyn_solutionhistory = "msdyn_solutionhistory";
  public static readonly string msdyn_solutionhistorydatasource = "msdyn_solutionhistorydatasource";
  public static readonly string msdynce_botcontent = "msdynce_botcontent";
  public static readonly string msfp_alert = "msfp_alert";
  public static readonly string msfp_alertrule = "msfp_alertrule";
  public static readonly string msfp_emailtemplate = "msfp_emailtemplate";
  public static readonly string msfp_localizedemailtemplate = "msfp_localizedemailtemplate";
  public static readonly string msfp_project = "msfp_project";
  public static readonly string msfp_question = "msfp_question";
  public static readonly string msfp_questionresponse = "msfp_questionresponse";
  public static readonly string msfp_satisfactionmetric = "msfp_satisfactionmetric";
  public static readonly string msfp_survey = "msfp_survey";
  public static readonly string msfp_surveyinvite = "msfp_surveyinvite";
  public static readonly string msfp_surveyreminder = "msfp_surveyreminder";
  public static readonly string msfp_surveyresponse = "msfp_surveyresponse";
  public static readonly string msfp_unsubscribedrecipient = "msfp_unsubscribedrecipient";
  public static readonly string multientitysearch = "multientitysearch";
  public static readonly string multientitysearchentities = "multientitysearchentities";
  public static readonly string multiselectattributeoptionvalues = "multiselectattributeoptionvalues";
  public static readonly string navigationsetting = "navigationsetting";
  public static readonly string new_leaveapprovalprocess = "new_leaveapprovalprocess";
  public static readonly string new_leaverequests = "new_leaverequests";
  public static readonly string newprocess = "newprocess";
  public static readonly string notification = "notification";
  public static readonly string officedocument = "officedocument";
  public static readonly string officegraphdocument = "officegraphdocument";
  public static readonly string offlinecommanddefinition = "offlinecommanddefinition";
  public static readonly string optionset = "optionset";
  public static readonly string organization = "organization";
  public static readonly string organizationdatasyncsubscription = "organizationdatasyncsubscription";
  public static readonly string organizationdatasyncsubscriptionentity = "organizationdatasyncsubscriptionentity";
  public static readonly string organizationsetting = "organizationsetting";
  public static readonly string organizationstatistic = "organizationstatistic";
  public static readonly string organizationui = "organizationui";
  public static readonly string orginsightsmetric = "orginsightsmetric";
  public static readonly string orginsightsnotification = "orginsightsnotification";
  public static readonly string owner = "owner";
  public static readonly string ownermapping = "ownermapping";
  public static readonly string package = "package";
  public static readonly string package_solution = "package_solution";
  public static readonly string partnerapplication = "partnerapplication";
  public static readonly string pdfsetting = "pdfsetting";
  public static readonly string personaldocumenttemplate = "personaldocumenttemplate";
  public static readonly string phonecall = "phonecall";
  public static readonly string picklistmapping = "picklistmapping";
  public static readonly string pluginassembly = "pluginassembly";
  public static readonly string plugintracelog = "plugintracelog";
  public static readonly string plugintype = "plugintype";
  public static readonly string plugintypestatistic = "plugintypestatistic";
  public static readonly string position = "position";
  public static readonly string post = "post";
  public static readonly string postcomment = "postcomment";
  public static readonly string postfollow = "postfollow";
  public static readonly string postlike = "postlike";
  public static readonly string postregarding = "postregarding";
  public static readonly string postrole = "postrole";
  public static readonly string principalattributeaccessmap = "principalattributeaccessmap";
  public static readonly string principalentitymap = "principalentitymap";
  public static readonly string principalobjectaccess = "principalobjectaccess";
  public static readonly string principalobjectaccessreadsnapshot = "principalobjectaccessreadsnapshot";
  public static readonly string principalobjectattributeaccess = "principalobjectattributeaccess";
  public static readonly string principalsyncattributemap = "principalsyncattributemap";
  public static readonly string privilege = "privilege";
  public static readonly string privilegeobjecttypecodes = "privilegeobjecttypecodes";
  public static readonly string processsession = "processsession";
  public static readonly string processstage = "processstage";
  public static readonly string processstageparameter = "processstageparameter";
  public static readonly string processtrigger = "processtrigger";
  public static readonly string provisionlanguageforuser = "provisionlanguageforuser";
  public static readonly string publisher = "publisher";
  public static readonly string publisheraddress = "publisheraddress";
  public static readonly string quarterlyfiscalcalendar = "quarterlyfiscalcalendar";
  public static readonly string queue = "queue";
  public static readonly string queueitem = "queueitem";
  public static readonly string queueitemcount = "queueitemcount";
  public static readonly string queuemembercount = "queuemembercount";
  public static readonly string queuemembership = "queuemembership";
  public static readonly string recommendeddocument = "recommendeddocument";
  public static readonly string recordcountsnapshot = "recordcountsnapshot";
  public static readonly string recurrencerule = "recurrencerule";
  public static readonly string recurringappointmentmaster = "recurringappointmentmaster";
  public static readonly string relationship = "relationship";
  public static readonly string relationshipattribute = "relationshipattribute";
  public static readonly string relationshiprole = "relationshiprole";
  public static readonly string relationshiprolemap = "relationshiprolemap";
  public static readonly string replicationbacklog = "replicationbacklog";
  public static readonly string report = "report";
  public static readonly string reportcategory = "reportcategory";
  public static readonly string reportentity = "reportentity";
  public static readonly string reportlink = "reportlink";
  public static readonly string reportvisibility = "reportvisibility";
  public static readonly string revokeinheritedaccessrecordstracker = "revokeinheritedaccessrecordstracker";
  public static readonly string ribbonclientmetadata = "ribbonclientmetadata";
  public static readonly string ribboncommand = "ribboncommand";
  public static readonly string ribboncontextgroup = "ribboncontextgroup";
  public static readonly string ribboncustomization = "ribboncustomization";
  public static readonly string ribbondiff = "ribbondiff";
  public static readonly string ribbonmetadatatoprocess = "ribbonmetadatatoprocess";
  public static readonly string ribbonrule = "ribbonrule";
  public static readonly string ribbontabtocommandmap = "ribbontabtocommandmap";
  public static readonly string role = "role";
  public static readonly string roleprivileges = "roleprivileges";
  public static readonly string roletemplate = "roletemplate";
  public static readonly string roletemplateprivileges = "roletemplateprivileges";
  public static readonly string rollupfield = "rollupfield";
  public static readonly string rollupjob = "rollupjob";
  public static readonly string rollupproperties = "rollupproperties";
  public static readonly string routingrule = "routingrule";
  public static readonly string routingruleitem = "routingruleitem";
  public static readonly string runtimedependency = "runtimedependency";
  public static readonly string savedorginsightsconfiguration = "savedorginsightsconfiguration";
  public static readonly string savedquery = "savedquery";
  public static readonly string savedqueryvisualization = "savedqueryvisualization";
  public static readonly string sdkmessage = "sdkmessage";
  public static readonly string sdkmessagefilter = "sdkmessagefilter";
  public static readonly string sdkmessagepair = "sdkmessagepair";
  public static readonly string sdkmessageprocessingstep = "sdkmessageprocessingstep";
  public static readonly string sdkmessageprocessingstepimage = "sdkmessageprocessingstepimage";
  public static readonly string sdkmessageprocessingstepsecureconfig = "sdkmessageprocessingstepsecureconfig";
  public static readonly string sdkmessagerequest = "sdkmessagerequest";
  public static readonly string sdkmessagerequestfield = "sdkmessagerequestfield";
  public static readonly string sdkmessageresponse = "sdkmessageresponse";
  public static readonly string sdkmessageresponsefield = "sdkmessageresponsefield";
  public static readonly string searchtelemetry = "searchtelemetry";
  public static readonly string semiannualfiscalcalendar = "semiannualfiscalcalendar";
  public static readonly string serviceendpoint = "serviceendpoint";
  public static readonly string serviceplan = "serviceplan";
  public static readonly string serviceplanappmodules = "serviceplanappmodules";
  public static readonly string settingdefinition = "settingdefinition";
  public static readonly string sharedobjectsforread = "sharedobjectsforread";
  public static readonly string sharepointdata = "sharepointdata";
  public static readonly string sharepointdocument = "sharepointdocument";
  public static readonly string sharepointdocumentlocation = "sharepointdocumentlocation";
  public static readonly string sharepointsite = "sharepointsite";
  public static readonly string similarityrule = "similarityrule";
  public static readonly string sitemap = "sitemap";
  public static readonly string sla = "sla";
  public static readonly string slaitem = "slaitem";
  public static readonly string slakpiinstance = "slakpiinstance";
  public static readonly string socialactivity = "socialactivity";
  public static readonly string socialinsightsconfiguration = "socialinsightsconfiguration";
  public static readonly string socialprofile = "socialprofile";
  public static readonly string solution = "solution";
  public static readonly string solutioncomponent = "solutioncomponent";
  public static readonly string solutioncomponentattributeconfiguration = "solutioncomponentattributeconfiguration";
  public static readonly string solutioncomponentconfiguration = "solutioncomponentconfiguration";
  public static readonly string solutioncomponentdefinition = "solutioncomponentdefinition";
  public static readonly string solutioncomponentrelationshipconfiguration = "solutioncomponentrelationshipconfiguration";
  public static readonly string solutionhistorydata = "solutionhistorydata";
  public static readonly string sqlencryptionaudit = "sqlencryptionaudit";
  public static readonly string stagesolutionupload = "stagesolutionupload";
  public static readonly string statusmap = "statusmap";
  public static readonly string stringmap = "stringmap";
  public static readonly string subject = "subject";
  public static readonly string subscription = "subscription";
  public static readonly string subscriptionclients = "subscriptionclients";
  public static readonly string subscriptionmanuallytrackedobject = "subscriptionmanuallytrackedobject";
  public static readonly string subscriptionstatisticsoffline = "subscriptionstatisticsoffline";
  public static readonly string subscriptionstatisticsoutlook = "subscriptionstatisticsoutlook";
  public static readonly string subscriptionsyncentryoffline = "subscriptionsyncentryoffline";
  public static readonly string subscriptionsyncentryoutlook = "subscriptionsyncentryoutlook";
  public static readonly string subscriptionsyncinfo = "subscriptionsyncinfo";
  public static readonly string subscriptiontrackingdeletedobject = "subscriptiontrackingdeletedobject";
  public static readonly string suggestioncardtemplate = "suggestioncardtemplate";
  public static readonly string syncattributemapping = "syncattributemapping";
  public static readonly string syncattributemappingprofile = "syncattributemappingprofile";
  public static readonly string syncerror = "syncerror";
  public static readonly string systemapplicationmetadata = "systemapplicationmetadata";
  public static readonly string systemform = "systemform";
  public static readonly string systemuser = "systemuser";
  public static readonly string systemuserauthorizationchangetracker = "systemuserauthorizationchangetracker";
  public static readonly string systemuserbusinessunitentitymap = "systemuserbusinessunitentitymap";
  public static readonly string systemuserlicenses = "systemuserlicenses";
  public static readonly string systemusermanagermap = "systemusermanagermap";
  public static readonly string systemuserprincipals = "systemuserprincipals";
  public static readonly string systemuserprofiles = "systemuserprofiles";
  public static readonly string systemuserroles = "systemuserroles";
  public static readonly string systemusersyncmappingprofiles = "systemusersyncmappingprofiles";
  public static readonly string task = "task";
  public static readonly string team = "team";
  public static readonly string teammembership = "teammembership";
  public static readonly string teammobileofflineprofilemembership = "teammobileofflineprofilemembership";
  public static readonly string teamprofiles = "teamprofiles";
  public static readonly string teamroles = "teamroles";
  public static readonly string teamsyncattributemappingprofiles = "teamsyncattributemappingprofiles";
  public static readonly string teamtemplate = "teamtemplate";
  public static readonly string template = "template";
  public static readonly string territory = "territory";
  public static readonly string textanalyticsentitymapping = "textanalyticsentitymapping";
  public static readonly string theme = "theme";
  public static readonly string timestampdatemapping = "timestampdatemapping";
  public static readonly string timezonedefinition = "timezonedefinition";
  public static readonly string timezonelocalizedname = "timezonelocalizedname";
  public static readonly string timezonerule = "timezonerule";
  public static readonly string traceassociation = "traceassociation";
  public static readonly string tracelog = "tracelog";
  public static readonly string traceregarding = "traceregarding";
  public static readonly string transactioncurrency = "transactioncurrency";
  public static readonly string transformationmapping = "transformationmapping";
  public static readonly string transformationparametermapping = "transformationparametermapping";
  public static readonly string translationprocess = "translationprocess";
  public static readonly string unresolvedaddress = "unresolvedaddress";
  public static readonly string untrackedemail = "untrackedemail";
  public static readonly string userapplicationmetadata = "userapplicationmetadata";
  public static readonly string userentityinstancedata = "userentityinstancedata";
  public static readonly string userentityuisettings = "userentityuisettings";
  public static readonly string userfiscalcalendar = "userfiscalcalendar";
  public static readonly string userform = "userform";
  public static readonly string usermapping = "usermapping";
  public static readonly string usermobileofflineprofilemembership = "usermobileofflineprofilemembership";
  public static readonly string userquery = "userquery";
  public static readonly string userqueryvisualization = "userqueryvisualization";
  public static readonly string usersearchfacet = "usersearchfacet";
  public static readonly string usersettings = "usersettings";
  public static readonly string webresource = "webresource";
  public static readonly string webwizard = "webwizard";
  public static readonly string wizardaccessprivilege = "wizardaccessprivilege";
  public static readonly string wizardpage = "wizardpage";
  public static readonly string workflow = "workflow";
  public static readonly string workflowbinary = "workflowbinary";
  public static readonly string workflowdependency = "workflowdependency";
  public static readonly string workflowlog = "workflowlog";
  public static readonly string workflowwaitsubscription = "workflowwaitsubscription";

    }
    public struct MessageNames
    {
        public static readonly string AddAppComponents = "AddAppComponents";
  public static readonly string AddChannelAccessProfilePrivileges = "AddChannelAccessProfilePrivileges";
  public static readonly string AddMembers = "AddMembers";
  public static readonly string AddPrincipalToQueue = "AddPrincipalToQueue";
  public static readonly string AddPrivileges = "AddPrivileges";
  public static readonly string AddRecurrence = "AddRecurrence";
  public static readonly string AddSolutionComponent = "AddSolutionComponent";
  public static readonly string AddToQueue = "AddToQueue";
  public static readonly string AddUserToRecordTeam = "AddUserToRecordTeam";
  public static readonly string AlmHandler = "AlmHandler";
  public static readonly string AnalyzeSentiment = "AnalyzeSentiment";
  public static readonly string ApplyRecordCreationAndUpdateRule = "ApplyRecordCreationAndUpdateRule";
  public static readonly string Assign = "Assign";
  public static readonly string AssignUserRoles = "AssignUserRoles";
  public static readonly string Associate = "Associate";
  public static readonly string AssociateEntities = "AssociateEntities";
  public static readonly string AutoMapEntity = "AutoMapEntity";
  public static readonly string BackgroundSend = "BackgroundSend";
  public static readonly string BatchPrediction = "BatchPrediction";
  public static readonly string Book = "Book";
  public static readonly string BulkDelete = "BulkDelete";
  public static readonly string BulkDelete2 = "BulkDelete2";
  public static readonly string BulkDetectDuplicates = "BulkDetectDuplicates";
  public static readonly string BulkMail = "BulkMail";
  public static readonly string CalculatePrice = "CalculatePrice";
  public static readonly string CalculateRollupField = "CalculateRollupField";
  public static readonly string CanBeReferenced = "CanBeReferenced";
  public static readonly string CanBeReferencing = "CanBeReferencing";
  public static readonly string CancelTraining = "CancelTraining";
  public static readonly string CanManyToMany = "CanManyToMany";
  public static readonly string CategorizeText = "CategorizeText";
  public static readonly string CheckIncoming = "CheckIncoming";
  public static readonly string CheckPromote = "CheckPromote";
  public static readonly string CloneAsPatch = "CloneAsPatch";
  public static readonly string CloneAsSolution = "CloneAsSolution";
  public static readonly string CloneMobileOfflineProfile = "CloneMobileOfflineProfile";
  public static readonly string CommitAnnotationBlocksUpload = "CommitAnnotationBlocksUpload";
  public static readonly string CommitAttachmentBlocksUpload = "CommitAttachmentBlocksUpload";
  public static readonly string CommitFileBlocksUpload = "CommitFileBlocksUpload";
  public static readonly string CompoundCreate = "CompoundCreate";
  public static readonly string CompoundUpdate = "CompoundUpdate";
  public static readonly string CompoundUpdateDuplicateDetectionRule = "CompoundUpdateDuplicateDetectionRule";
  public static readonly string ConvertDateAndTimeBehavior = "ConvertDateAndTimeBehavior";
  public static readonly string ConvertOwnerTeamToAccessTeam = "ConvertOwnerTeamToAccessTeam";
  public static readonly string CopySystemForm = "CopySystemForm";
  public static readonly string Create = "Create";
  public static readonly string CreateAsyncJobToRevokeInheritedAccess = "CreateAsyncJobToRevokeInheritedAccess";
  public static readonly string CreateAttribute = "CreateAttribute";
  public static readonly string CreateCustomerRelationships = "CreateCustomerRelationships";
  public static readonly string CreateEntity = "CreateEntity";
  public static readonly string CreateEntityKey = "CreateEntityKey";
  public static readonly string CreateException = "CreateException";
  public static readonly string CreateInstance = "CreateInstance";
  public static readonly string CreateKnowledgeArticleTranslation = "CreateKnowledgeArticleTranslation";
  public static readonly string CreateKnowledgeArticleVersion = "CreateKnowledgeArticleVersion";
  public static readonly string CreateManyToMany = "CreateManyToMany";
  public static readonly string CreateOneToMany = "CreateOneToMany";
  public static readonly string CreateOptionSet = "CreateOptionSet";
  public static readonly string CreatePolymorphicLookupAttribute = "CreatePolymorphicLookupAttribute";
  public static readonly string CreateWorkflowFromTemplate = "CreateWorkflowFromTemplate";
  public static readonly string Delete = "Delete";
  public static readonly string DeleteAndPromote = "DeleteAndPromote";
  public static readonly string DeleteAttribute = "DeleteAttribute";
  public static readonly string DeleteAuditData = "DeleteAuditData";
  public static readonly string DeleteEntity = "DeleteEntity";
  public static readonly string DeleteEntityKey = "DeleteEntityKey";
  public static readonly string DeleteFile = "DeleteFile";
  public static readonly string DeleteOpenInstances = "DeleteOpenInstances";
  public static readonly string DeleteOptionSet = "DeleteOptionSet";
  public static readonly string DeleteOptionValue = "DeleteOptionValue";
  public static readonly string DeleteRecordChangeHistory = "DeleteRecordChangeHistory";
  public static readonly string DeleteRelationship = "DeleteRelationship";
  public static readonly string DeliverImmediatePromote = "DeliverImmediatePromote";
  public static readonly string DeliverIncoming = "DeliverIncoming";
  public static readonly string DeliverPromote = "DeliverPromote";
  public static readonly string DeprovisionLanguage = "DeprovisionLanguage";
  public static readonly string DetachFromQueue = "DetachFromQueue";
  public static readonly string DetectLanguage = "DetectLanguage";
  public static readonly string Disassociate = "Disassociate";
  public static readonly string DisassociateEntities = "DisassociateEntities";
  public static readonly string DownloadBlock = "DownloadBlock";
  public static readonly string DownloadReportDefinition = "DownloadReportDefinition";
  public static readonly string DownloadSolutionExportData = "DownloadSolutionExportData";
  public static readonly string EntityExpressionToFetchXml = "EntityExpressionToFetchXml";
  public static readonly string Execute = "Execute";
  public static readonly string ExecuteAsync = "ExecuteAsync";
  public static readonly string ExecuteById = "ExecuteById";
  public static readonly string ExecuteCosmosSqlQuery = "ExecuteCosmosSqlQuery";
  public static readonly string ExecuteMultiple = "ExecuteMultiple";
  public static readonly string ExecuteTransaction = "ExecuteTransaction";
  public static readonly string ExecuteWorkflow = "ExecuteWorkflow";
  public static readonly string Expand = "Expand";
  public static readonly string Export = "Export";
  public static readonly string ExportAll = "ExportAll";
  public static readonly string ExportCompressed = "ExportCompressed";
  public static readonly string ExportCompressedAll = "ExportCompressedAll";
  public static readonly string ExportCompressedTranslations = "ExportCompressedTranslations";
  public static readonly string ExportFieldTranslation = "ExportFieldTranslation";
  public static readonly string ExportMappings = "ExportMappings";
  public static readonly string ExportSolution = "ExportSolution";
  public static readonly string ExportSolutionAsync = "ExportSolutionAsync";
  public static readonly string ExportTranslation = "ExportTranslation";
  public static readonly string ExportTranslations = "ExportTranslations";
  public static readonly string ExtractKeyPhrases = "ExtractKeyPhrases";
  public static readonly string ExtractTextEntities = "ExtractTextEntities";
  public static readonly string FetchXmlToEntityExpression = "FetchXmlToEntityExpression";
  public static readonly string FormatAddress = "FormatAddress";
  public static readonly string FullTextSearchKnowledgeArticle = "FullTextSearchKnowledgeArticle";
  public static readonly string GenerateSocialProfile = "GenerateSocialProfile";
  public static readonly string GetAllTimeZonesWithDisplayName = "GetAllTimeZonesWithDisplayName";
  public static readonly string GetAutoNumberSeed = "GetAutoNumberSeed";
  public static readonly string GetDecryptionKey = "GetDecryptionKey";
  public static readonly string GetDistinctValues = "GetDistinctValues";
  public static readonly string GetFileSasUrl = "GetFileSasUrl";
  public static readonly string GetHeaderColumns = "GetHeaderColumns";
  public static readonly string GetJobStatus = "GetJobStatus";
  public static readonly string GetNextAutoNumberValue = "GetNextAutoNumberValue";
  public static readonly string GetQuantityDecimal = "GetQuantityDecimal";
  public static readonly string GetReportHistoryLimit = "GetReportHistoryLimit";
  public static readonly string GetTimeZoneCodeByLocalizedName = "GetTimeZoneCodeByLocalizedName";
  public static readonly string GetTrackingToken = "GetTrackingToken";
  public static readonly string GetValidManyToMany = "GetValidManyToMany";
  public static readonly string GetValidReferencedEntities = "GetValidReferencedEntities";
  public static readonly string GetValidReferencingEntities = "GetValidReferencingEntities";
  public static readonly string GrantAccess = "GrantAccess";
  public static readonly string Handle = "Handle";
  public static readonly string ImmediateBook = "ImmediateBook";
  public static readonly string Import = "Import";
  public static readonly string ImportAll = "ImportAll";
  public static readonly string ImportCardTypeSchema = "ImportCardTypeSchema";
  public static readonly string ImportCompressedAll = "ImportCompressedAll";
  public static readonly string ImportCompressedTranslationsWithProgress = "ImportCompressedTranslationsWithProgress";
  public static readonly string ImportCompressedWithProgress = "ImportCompressedWithProgress";
  public static readonly string ImportFieldTranslation = "ImportFieldTranslation";
  public static readonly string ImportMappings = "ImportMappings";
  public static readonly string ImportRecords = "ImportRecords";
  public static readonly string ImportSolution = "ImportSolution";
  public static readonly string ImportSolutionAsync = "ImportSolutionAsync";
  public static readonly string ImportSolutions = "ImportSolutions";
  public static readonly string ImportTranslation = "ImportTranslation";
  public static readonly string ImportTranslationsWithProgress = "ImportTranslationsWithProgress";
  public static readonly string ImportWithProgress = "ImportWithProgress";
  public static readonly string IncrementKnowledgeArticleViewCount = "IncrementKnowledgeArticleViewCount";
  public static readonly string InitializeAnnotationBlocksDownload = "InitializeAnnotationBlocksDownload";
  public static readonly string InitializeAnnotationBlocksUpload = "InitializeAnnotationBlocksUpload";
  public static readonly string InitializeAttachmentBlocksDownload = "InitializeAttachmentBlocksDownload";
  public static readonly string InitializeAttachmentBlocksUpload = "InitializeAttachmentBlocksUpload";
  public static readonly string InitializeFileBlocksDownload = "InitializeFileBlocksDownload";
  public static readonly string InitializeFileBlocksUpload = "InitializeFileBlocksUpload";
  public static readonly string InitializeFrom = "InitializeFrom";
  public static readonly string InitializeModernFlowFromAsyncWorkflow = "InitializeModernFlowFromAsyncWorkflow";
  public static readonly string InsertOptionValue = "InsertOptionValue";
  public static readonly string InsertStatusValue = "InsertStatusValue";
  public static readonly string install = "install";
  public static readonly string InstallSampleData = "InstallSampleData";
  public static readonly string Instantiate = "Instantiate";
  public static readonly string InstantiateFilters = "InstantiateFilters";
  public static readonly string IsBackOfficeInstalled = "IsBackOfficeInstalled";
  public static readonly string IsComponentCustomizable = "IsComponentCustomizable";
  public static readonly string IsDataEncryptionActive = "IsDataEncryptionActive";
  public static readonly string IsPaiEnabled = "IsPaiEnabled";
  public static readonly string IsValidStateTransition = "IsValidStateTransition";
  public static readonly string LocalTimeFromUtcTime = "LocalTimeFromUtcTime";
  public static readonly string MakeAvailableToOrganization = "MakeAvailableToOrganization";
  public static readonly string MakeUnavailableToOrganization = "MakeUnavailableToOrganization";
  public static readonly string Merge = "Merge";
  public static readonly string ModifyAccess = "ModifyAccess";
  public static readonly string msdyn_ActivateProcesses = "msdyn_ActivateProcesses";
  public static readonly string msdyn_ActivateSdkMessageProcessingSteps = "msdyn_ActivateSdkMessageProcessingSteps";
  public static readonly string msdyn_CheckForCustomizedOptionSet = "msdyn_CheckForCustomizedOptionSet";
  public static readonly string msdyn_CheckForCustomizedSitemap = "msdyn_CheckForCustomizedSitemap";
  public static readonly string msdyn_CheckForCustomizedWebResources = "msdyn_CheckForCustomizedWebResources";
  public static readonly string msdyn_CheckForDeletedProcess = "msdyn_CheckForDeletedProcess";
  public static readonly string msdyn_CheckForDeletedSDKMessageProcessingSteps = "msdyn_CheckForDeletedSDKMessageProcessingSteps";
  public static readonly string msdyn_CheckForDeletedWebResources = "msdyn_CheckForDeletedWebResources";
  public static readonly string msdyn_CheckForPendingProcesses = "msdyn_CheckForPendingProcesses";
  public static readonly string msdyn_CheckIfProcessesAreActive = "msdyn_CheckIfProcessesAreActive";
  public static readonly string msdyn_CheckIfProcessesOwnedByDisabledUsers = "msdyn_CheckIfProcessesOwnedByDisabledUsers";
  public static readonly string msdyn_CheckIfSDKMessageProcessingStepsAreActive = "msdyn_CheckIfSDKMessageProcessingStepsAreActive";
  public static readonly string msdyn_ConditionXmlConversion = "msdyn_ConditionXmlConversion";
  public static readonly string msdyn_CreateActionFlow = "msdyn_CreateActionFlow";
  public static readonly string msdyn_CreateNewAnalysisJobForRuleSet = "msdyn_CreateNewAnalysisJobForRuleSet";
  public static readonly string msdyn_DeleteCalendar = "msdyn_DeleteCalendar";
  public static readonly string msdyn_GetKAObjectFromTemplate = "msdyn_GetKAObjectFromTemplate";
  public static readonly string msdyn_ManageSLAInstances = "msdyn_ManageSLAInstances";
  public static readonly string msdyn_RegisterSolutionHealthRule = "msdyn_RegisterSolutionHealthRule";
  public static readonly string msdyn_ResolveSolutionHealthRuleFailure = "msdyn_ResolveSolutionHealthRuleFailure";
  public static readonly string msdyn_RetrieveKnowledgeSuggestions = "msdyn_RetrieveKnowledgeSuggestions";
  public static readonly string msdyn_RetrieveSearchProviders = "msdyn_RetrieveSearchProviders";
  public static readonly string msdyn_RunSolutionCheckerRules = "msdyn_RunSolutionCheckerRules";
  public static readonly string msdyn_SaveCalendar = "msdyn_SaveCalendar";
  public static readonly string msdyn_SendEmailFromTemplate = "msdyn_SendEmailFromTemplate";
  public static readonly string OrderOption = "OrderOption";
  public static readonly string Parse = "Parse";
  public static readonly string PickFromQueue = "PickFromQueue";
  public static readonly string PowerAutomateProxy = "PowerAutomateProxy";
  public static readonly string Predict = "Predict";
  public static readonly string PredictByReference = "PredictByReference";
  public static readonly string PredictionSchema = "PredictionSchema";
  public static readonly string ProvisionLanguage = "ProvisionLanguage";
  public static readonly string ProvisionLanguageAsync = "ProvisionLanguageAsync";
  public static readonly string Publish = "Publish";
  public static readonly string PublishAIConfiguration = "PublishAIConfiguration";
  public static readonly string PublishAll = "PublishAll";
  public static readonly string PublishTheme = "PublishTheme";
  public static readonly string PvaAuthorize = "PvaAuthorize";
  public static readonly string PvaCreateBotComponents = "PvaCreateBotComponents";
  public static readonly string PvaCreateContentSnapshot = "PvaCreateContentSnapshot";
  public static readonly string PvaDeleteBot = "PvaDeleteBot";
  public static readonly string PvaGetDirectLineEndpoint = "PvaGetDirectLineEndpoint";
  public static readonly string PvaGetFeatureControlSet = "PvaGetFeatureControlSet";
  public static readonly string PvaGetUserBots = "PvaGetUserBots";
  public static readonly string PvaPublish = "PvaPublish";
  public static readonly string Query = "Query";
  public static readonly string QueryMultiple = "QueryMultiple";
  public static readonly string QueueUpdateRibbonClientMetadata = "QueueUpdateRibbonClientMetadata";
  public static readonly string QuickTest = "QuickTest";
  public static readonly string ReactivateEntityKey = "ReactivateEntityKey";
  public static readonly string ReassignObjects = "ReassignObjects";
  public static readonly string ReassignObjectsEx = "ReassignObjectsEx";
  public static readonly string Recalculate = "Recalculate";
  public static readonly string RecognizeText = "RecognizeText";
  public static readonly string ReleaseToQueue = "ReleaseToQueue";
  public static readonly string RemoveActiveCustomizations = "RemoveActiveCustomizations";
  public static readonly string RemoveAppComponents = "RemoveAppComponents";
  public static readonly string RemoveFromQueue = "RemoveFromQueue";
  public static readonly string RemoveMembers = "RemoveMembers";
  public static readonly string RemoveParent = "RemoveParent";
  public static readonly string RemovePrivilege = "RemovePrivilege";
  public static readonly string RemoveRelated = "RemoveRelated";
  public static readonly string RemoveSolutionComponent = "RemoveSolutionComponent";
  public static readonly string RemoveUserFromRecordTeam = "RemoveUserFromRecordTeam";
  public static readonly string RemoveUserRoles = "RemoveUserRoles";
  public static readonly string ReplacePrivileges = "ReplacePrivileges";
  public static readonly string Reschedule = "Reschedule";
  public static readonly string ResetOfflineFilters = "ResetOfflineFilters";
  public static readonly string ResetUserFilters = "ResetUserFilters";
  public static readonly string Retrieve = "Retrieve";
  public static readonly string RetrieveAadUserPrivileges = "RetrieveAadUserPrivileges";
  public static readonly string RetrieveAbsoluteAndSiteCollectionUrl = "RetrieveAbsoluteAndSiteCollectionUrl";
  public static readonly string RetrieveAccessOrigin = "RetrieveAccessOrigin";
  public static readonly string RetrieveActivePath = "RetrieveActivePath";
  public static readonly string RetrieveAllChildUsers = "RetrieveAllChildUsers";
  public static readonly string RetrieveAllCompositeDataSources = "RetrieveAllCompositeDataSources";
  public static readonly string RetrieveAllEntities = "RetrieveAllEntities";
  public static readonly string RetrieveAllManagedProperties = "RetrieveAllManagedProperties";
  public static readonly string RetrieveAllOptionSets = "RetrieveAllOptionSets";
  public static readonly string RetrieveAnalyticsStoreDetails = "RetrieveAnalyticsStoreDetails";
  public static readonly string RetrieveAppComponents = "RetrieveAppComponents";
  public static readonly string RetrieveApplicationRibbon = "RetrieveApplicationRibbon";
  public static readonly string RetrieveAttribute = "RetrieveAttribute";
  public static readonly string RetrieveAttributeChangeHistory = "RetrieveAttributeChangeHistory";
  public static readonly string RetrieveAuditDetails = "RetrieveAuditDetails";
  public static readonly string RetrieveAuditPartitionList = "RetrieveAuditPartitionList";
  public static readonly string RetrieveAvailableLanguages = "RetrieveAvailableLanguages";
  public static readonly string RetrieveBusinessHierarchy = "RetrieveBusinessHierarchy";
  public static readonly string RetrieveByTopIncidentProduct = "RetrieveByTopIncidentProduct";
  public static readonly string RetrieveByTopIncidentSubject = "RetrieveByTopIncidentSubject";
  public static readonly string RetrieveCascadeAssignAsyncJobId = "RetrieveCascadeAssignAsyncJobId";
  public static readonly string RetrieveCascadeDeleteAsyncJobId = "RetrieveCascadeDeleteAsyncJobId";
  public static readonly string RetrieveChannelAccessProfilePrivileges = "RetrieveChannelAccessProfilePrivileges";
  public static readonly string RetrieveCompositeDataSource = "RetrieveCompositeDataSource";
  public static readonly string RetrieveCurrentOrganization = "RetrieveCurrentOrganization";
  public static readonly string RetrieveDataEncryptionKey = "RetrieveDataEncryptionKey";
  public static readonly string RetrieveDependenciesForDelete = "RetrieveDependenciesForDelete";
  public static readonly string RetrieveDependenciesForUninstall = "RetrieveDependenciesForUninstall";
  public static readonly string RetrieveDependentComponents = "RetrieveDependentComponents";
  public static readonly string RetrieveDeploymentLicenseType = "RetrieveDeploymentLicenseType";
  public static readonly string RetrieveDeprovisionedLanguages = "RetrieveDeprovisionedLanguages";
  public static readonly string RetrieveDuplicates = "RetrieveDuplicates";
  public static readonly string RetrieveEntity = "RetrieveEntity";
  public static readonly string RetrieveEntityChanges = "RetrieveEntityChanges";
  public static readonly string RetrieveEntityKey = "RetrieveEntityKey";
  public static readonly string RetrieveEntityRibbon = "RetrieveEntityRibbon";
  public static readonly string RetrieveEnvironmentVariables = "RetrieveEnvironmentVariables";
  public static readonly string RetrieveEnvironmentVariableValue = "RetrieveEnvironmentVariableValue";
  public static readonly string RetrieveExchangeAppointments = "RetrieveExchangeAppointments";
  public static readonly string RetrieveExchangeRate = "RetrieveExchangeRate";
  public static readonly string RetrieveFilteredForms = "RetrieveFilteredForms";
  public static readonly string RetrieveFormattedImportJobResults = "RetrieveFormattedImportJobResults";
  public static readonly string RetrieveFormXml = "RetrieveFormXml";
  public static readonly string RetrieveInstalledLanguagePacks = "RetrieveInstalledLanguagePacks";
  public static readonly string RetrieveInstalledLanguagePackVersion = "RetrieveInstalledLanguagePackVersion";
  public static readonly string RetrieveLicenseInfo = "RetrieveLicenseInfo";
  public static readonly string RetrieveLocLabels = "RetrieveLocLabels";
  public static readonly string RetrieveMailboxTrackingFolders = "RetrieveMailboxTrackingFolders";
  public static readonly string RetrieveManagedProperty = "RetrieveManagedProperty";
  public static readonly string RetrieveMembers = "RetrieveMembers";
  public static readonly string RetrieveMetadataChanges = "RetrieveMetadataChanges";
  public static readonly string RetrieveMissingComponents = "RetrieveMissingComponents";
  public static readonly string RetrieveMissingDependencies = "RetrieveMissingDependencies";
  public static readonly string RetrieveMultiple = "RetrieveMultiple";
  public static readonly string RetrieveOptionSet = "RetrieveOptionSet";
  public static readonly string RetrieveOrganizationInfo = "RetrieveOrganizationInfo";
  public static readonly string RetrieveOrganizationResources = "RetrieveOrganizationResources";
  public static readonly string RetrieveParsedData = "RetrieveParsedData";
  public static readonly string RetrievePersonalWall = "RetrievePersonalWall";
  public static readonly string RetrievePrincipalAccess = "RetrievePrincipalAccess";
  public static readonly string RetrievePrincipalAccessInfo = "RetrievePrincipalAccessInfo";
  public static readonly string RetrievePrincipalAttributePrivileges = "RetrievePrincipalAttributePrivileges";
  public static readonly string RetrievePrincipalSyncAttributeMappings = "RetrievePrincipalSyncAttributeMappings";
  public static readonly string RetrievePrivilegeSet = "RetrievePrivilegeSet";
  public static readonly string RetrieveProcessInstances = "RetrieveProcessInstances";
  public static readonly string RetrieveProvisionedLanguagePackVersion = "RetrieveProvisionedLanguagePackVersion";
  public static readonly string RetrieveProvisionedLanguages = "RetrieveProvisionedLanguages";
  public static readonly string RetrieveRecordChangeHistory = "RetrieveRecordChangeHistory";
  public static readonly string RetrieveRecordWall = "RetrieveRecordWall";
  public static readonly string RetrieveRelationship = "RetrieveRelationship";
  public static readonly string RetrieveRequiredComponents = "RetrieveRequiredComponents";
  public static readonly string RetrieveRolePrivileges = "RetrieveRolePrivileges";
  public static readonly string RetrieveSharedPrincipalsAndAccess = "RetrieveSharedPrincipalsAndAccess";
  public static readonly string RetrieveSubsidiaryTeams = "RetrieveSubsidiaryTeams";
  public static readonly string RetrieveSubsidiaryUsers = "RetrieveSubsidiaryUsers";
  public static readonly string RetrieveTeamPrivileges = "RetrieveTeamPrivileges";
  public static readonly string RetrieveTeams = "RetrieveTeams";
  public static readonly string RetrieveTimelineWallRecords = "RetrieveTimelineWallRecords";
  public static readonly string RetrieveTimestamp = "RetrieveTimestamp";
  public static readonly string RetrieveTotalRecordCount = "RetrieveTotalRecordCount";
  public static readonly string RetrieveUnpublished = "RetrieveUnpublished";
  public static readonly string RetrieveUnpublishedMultiple = "RetrieveUnpublishedMultiple";
  public static readonly string RetrieveUserLicenseInfo = "RetrieveUserLicenseInfo";
  public static readonly string RetrieveUserPrivilegeByPrivilegeId = "RetrieveUserPrivilegeByPrivilegeId";
  public static readonly string RetrieveUserPrivilegeByPrivilegeName = "RetrieveUserPrivilegeByPrivilegeName";
  public static readonly string RetrieveUserPrivileges = "RetrieveUserPrivileges";
  public static readonly string RetrieveUserQueues = "RetrieveUserQueues";
  public static readonly string RetrieveUserSetOfPrivilegesByIds = "RetrieveUserSetOfPrivilegesByIds";
  public static readonly string RetrieveUserSetOfPrivilegesByNames = "RetrieveUserSetOfPrivilegesByNames";
  public static readonly string RetrieveUserSettings = "RetrieveUserSettings";
  public static readonly string RetrieveUsersPrivilegesThroughTeams = "RetrieveUsersPrivilegesThroughTeams";
  public static readonly string RetrieveVersion = "RetrieveVersion";
  public static readonly string RevokeAccess = "RevokeAccess";
  public static readonly string Rollup = "Rollup";
  public static readonly string Route = "Route";
  public static readonly string RouteTo = "RouteTo";
  public static readonly string SchedulePrediction = "SchedulePrediction";
  public static readonly string ScheduleRetrain = "ScheduleRetrain";
  public static readonly string ScheduleTraining = "ScheduleTraining";
  public static readonly string Search = "Search";
  public static readonly string SearchByBody = "SearchByBody";
  public static readonly string SearchByBodyLegacy = "SearchByBodyLegacy";
  public static readonly string SearchByKeywords = "SearchByKeywords";
  public static readonly string SearchByKeywordsLegacy = "SearchByKeywordsLegacy";
  public static readonly string SearchByTitle = "SearchByTitle";
  public static readonly string SearchByTitleLegacy = "SearchByTitleLegacy";
  public static readonly string Send = "Send";
  public static readonly string SendFromTemplate = "SendFromTemplate";
  public static readonly string SetAutoNumberSeed = "SetAutoNumberSeed";
  public static readonly string SetBusiness = "SetBusiness";
  public static readonly string SetDataEncryptionKey = "SetDataEncryptionKey";
  public static readonly string SetFeatureStatus = "SetFeatureStatus";
  public static readonly string SetLocLabels = "SetLocLabels";
  public static readonly string SetParent = "SetParent";
  public static readonly string SetProcess = "SetProcess";
  public static readonly string SetRelated = "SetRelated";
  public static readonly string SetReportRelated = "SetReportRelated";
  public static readonly string SetState = "SetState";
  public static readonly string SetStateDynamicEntity = "SetStateDynamicEntity";
  public static readonly string StageAndUpgrade = "StageAndUpgrade";
  public static readonly string StageSolution = "StageSolution";
  public static readonly string SyncBulkOperation = "SyncBulkOperation";
  public static readonly string Train = "Train";
  public static readonly string Transform = "Transform";
  public static readonly string TriggerServiceEndpointCheck = "TriggerServiceEndpointCheck";
  public static readonly string UninstallSampleData = "UninstallSampleData";
  public static readonly string Unpublish = "Unpublish";
  public static readonly string UnpublishAIConfiguration = "UnpublishAIConfiguration";
  public static readonly string UnschedulePrediction = "UnschedulePrediction";
  public static readonly string UnscheduleTraining = "UnscheduleTraining";
  public static readonly string Update = "Update";
  public static readonly string UpdateAttribute = "UpdateAttribute";
  public static readonly string UpdateEntity = "UpdateEntity";
  public static readonly string UpdateFeatureConfig = "UpdateFeatureConfig";
  public static readonly string UpdateOptionSet = "UpdateOptionSet";
  public static readonly string UpdateOptionValue = "UpdateOptionValue";
  public static readonly string UpdateRelationship = "UpdateRelationship";
  public static readonly string UpdateRibbonClientMetadata = "UpdateRibbonClientMetadata";
  public static readonly string UpdateSolutionComponent = "UpdateSolutionComponent";
  public static readonly string UpdateStateValue = "UpdateStateValue";
  public static readonly string UpdateUserSettings = "UpdateUserSettings";
  public static readonly string UploadBlock = "UploadBlock";
  public static readonly string Upsert = "Upsert";
  public static readonly string UpsertCompositeDataSource = "UpsertCompositeDataSource";
  public static readonly string UpsertEnvironmentVariable = "UpsertEnvironmentVariable";
  public static readonly string UtcTimeFromLocalTime = "UtcTimeFromLocalTime";
  public static readonly string Validate = "Validate";
  public static readonly string ValidateAIConfiguration = "ValidateAIConfiguration";
  public static readonly string ValidateApp = "ValidateApp";
  public static readonly string ValidateFetchXmlExpression = "ValidateFetchXmlExpression";
  public static readonly string ValidateRecurrenceRule = "ValidateRecurrenceRule";
  public static readonly string WhoAmI = "WhoAmI";

    }
}

