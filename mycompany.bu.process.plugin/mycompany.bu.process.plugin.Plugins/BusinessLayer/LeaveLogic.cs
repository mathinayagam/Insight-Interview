using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Xrm.Sdk;
using Microsoft.Xrm.Sdk.Client;
using Microsoft.Xrm.Sdk.Query;
using mycompany.bu.process.plugin.Plugins.ProxyClasses;
namespace mycompany.bu.process.plugin.Plugins.BusinessLayer
{
    class LeaveLogic
    {
        #region consstructors
        private readonly IOrganizationService organizationService;
        private readonly OrganizationServiceContext organizationServiceContext;

        public LeaveLogic(IOrganizationService organizationService, OrganizationServiceContext organizationServiceContext)
        {
            this.organizationService = organizationService;
            this.organizationServiceContext = organizationServiceContext;
        }
        #endregion

        #region Methods
        public void UpdateApprovedLeave(Entity target)
        {
            try
            {
                var entityobj = organizationService.Retrieve("new_leaverequests", target.Id, new ColumnSet(true));
                var leaverequest = new LeaveRequests(entityobj);
                EntityCollection results = null;
                if(leaverequest.LeaveStatus==eLeaveRequests_LeaveStatus.Approved)
                { 
                QueryExpression _LeaveBalanceQuery = new QueryExpression
                {
                    EntityName = "new_leavebalance",
                    ColumnSet = new ColumnSet("new_leavebalance"),
                    Criteria = new FilterExpression
                    {
                        Conditions =
                        {
                            new ConditionExpression
                            {
                                AttributeName = "new_employeeid",
                                Operator = ConditionOperator.Equal,
                                Values = { leaverequest.CreatedBy.Id}
                            },
                            new ConditionExpression
                            {
                                AttributeName = "new_leavetype",
                                Operator = ConditionOperator.Equal,
                                Values = { leaverequest.LeaveType_OptionSetValue.Value}

                            }
                        }

                    }
                };
                results = organizationService.RetrieveMultiple(_LeaveBalanceQuery);
                if(results.Entities.Count==1)
                {
                    var leavebalance = new LeaveBalance(results.Entities[0]);
                    leavebalance.LeaveBalanceAttribute = leavebalance.LeaveBalanceAttribute - leaverequest.NumberOfDays;
                    leavebalance.Update(organizationService);
                }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }
        #endregion
    }
}
