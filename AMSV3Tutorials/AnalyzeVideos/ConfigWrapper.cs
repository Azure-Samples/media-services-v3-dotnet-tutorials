using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AnalyzeVideos
{
    public class ConfigWrapper
    {
        public string SubscriptionId
        {
            get { return ConfigurationManager.AppSettings["SubscriptionId"]; }
        }

        public string ResourceGroup
        {
            get { return ConfigurationManager.AppSettings["ResourceGroup"]; }
        }

        public string AccountName
        {
            get { return ConfigurationManager.AppSettings["AccountName"]; }
        }

        public string AadTenantId
        {
            get { return ConfigurationManager.AppSettings["AadTenantId"]; }
        }

        public string AadClientId
        {
            get { return ConfigurationManager.AppSettings["AadClientId"]; }
        }

        public string AadSecret
        {
            get { return ConfigurationManager.AppSettings["AadSecret"]; }
        }

        public Uri ArmAadAudience
        {
            get { return new Uri(ConfigurationManager.AppSettings["ArmAadAudience"]); }
        }

        public Uri AadEndpoint
        {
            get { return new Uri(ConfigurationManager.AppSettings["AadEndpoint"]); }
        }

        public Uri ArmEndpoint
        {
            get { return new Uri(ConfigurationManager.AppSettings["ArmEndpoint"]); }
        }

        public string Region
        {
            get { return ConfigurationManager.AppSettings["Region"]; }
        }
    }
}
