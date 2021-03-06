﻿using System.Web.Http;
using EPiServer.Framework;
using EPiServer.Framework.Initialization;
using EPiServer.ServiceLocation;

namespace Epinova.InRiverConnector.EpiserverImporter
{
    [InitializableModule]
    [ModuleDependency(typeof(EPiServer.Web.InitializationModule))]
    public class PublisherInitializer : IConfigurableModule
    {
        /// <summary>
        /// Initializate the inRiver Web API.
        /// </summary>
        /// <remarks>
        /// This method is called once after CMS has been initialized
        /// </remarks>
        /// <param name="context"></param>
        public void Initialize(InitializationEngine context)
        {
            var config = GlobalConfiguration.Configuration;
            
            //// For debug purposes, turn this on. This should be done in the web project
            //// and not in a packaged library.
            config.IncludeErrorDetailPolicy = IncludeErrorDetailPolicy.Always;
            config.Routes.MapHttpRoute(
                "inRiverApi",
                "inriverapi/{controller}/{action}/{id}",
                new { id = RouteParameter.Optional });
        }

        public void Preload(string[] parameters)
        {
        }

        public void Uninitialize(InitializationEngine context)
        {
        }

        public void ConfigureContainer(ServiceConfigurationContext context)
        {
            context.StructureMap().Configure(x =>
            {
                x.For<ICatalogImporter>().Use<CatalogImporter>();
                x.For<ICatalogService>().Use<CatalogService>();
            });
        }
    }

}