﻿using System;
using System.IO;
using System.Reflection;
using Epinova.InRiverConnector.EpiserverAdapter.Communication;
using Epinova.InRiverConnector.EpiserverAdapter.Helpers;
using Epinova.InRiverConnector.EpiserverAdapter.XmlFactories;
using inRiver.Integration.Configuration;
using inRiver.Integration.Export;
using inRiver.Integration.Interface;
using inRiver.Integration.Logging;
using inRiver.Remoting;
using inRiver.Remoting.Connect;
using inRiver.Remoting.Log;
using inRiver.Remoting.Objects;

namespace Epinova.InRiverConnector.EpiserverAdapter
{
    public class EpiserverAdapter : ServerListener, IOutboundConnector, IChannelListener, ICVLListener
    {
        private bool _started;
        private IConfiguration _config;
        private EpiApi _epiApi;
        private CatalogElementFactory _catalogElementFactory;
        private CatalogDocumentFactory _catalogDocumentFactory;
        private ChannelHelper _channelHelper;
        private ResourceElementFactory _resourceElementFactory;
        private EpiMappingHelper _epiMappingHelper;
        private CatalogCodeGenerator _catalogCodeGenerator;
        private ChannelPublisher _publisher;
        private PimFieldAdapter _pimFieldAdapter;
        private IEntityService _entityService;
        private CvlUpdater _cvlUpdater;
        private DocumentFileHelper _documentFileHelper;

        public new void Start()
        {
            var startEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Start, "Connector is starting", 0);

            try
            {
                _config = new Configuration(Id);
                ConnectorEventHelper.CleanupOngoingEvents(_config);
                
                Entity channel = RemoteManager.DataService.GetEntity(_config.ChannelId, LoadLevel.Shallow);
                if (channel == null || channel.EntityType.Id != "Channel")
                {
                    _started = false;
                    ConnectorEventHelper.UpdateEvent(startEvent, "Channel id is not valid: Entity with given ID is not a channel, or doesn't exist. Unable to start", -1, true);
                    return;
                }

                _pimFieldAdapter = new PimFieldAdapter(_config);
                _epiMappingHelper = new EpiMappingHelper(_config, _pimFieldAdapter);
                _entityService = new EntityService(_config, _epiMappingHelper);
                _catalogCodeGenerator = new CatalogCodeGenerator(_config, _entityService);
                _epiApi = new EpiApi(_config, _catalogCodeGenerator, _pimFieldAdapter);
                _catalogElementFactory = new CatalogElementFactory(_config, _epiMappingHelper, _catalogCodeGenerator, _pimFieldAdapter);
                _channelHelper = new ChannelHelper(_config, _epiMappingHelper, _entityService);
                _catalogDocumentFactory = new CatalogDocumentFactory(_config, _epiApi, _catalogElementFactory, _epiMappingHelper, _channelHelper, _catalogCodeGenerator, _entityService);
                _resourceElementFactory = new ResourceElementFactory(_catalogElementFactory, _epiMappingHelper, _catalogCodeGenerator, _config, _entityService);
                
                _documentFileHelper = new DocumentFileHelper(_config, _channelHelper);
                _cvlUpdater = new CvlUpdater(_config, _catalogDocumentFactory, _epiApi, _documentFileHelper);

                _publisher = new ChannelPublisher(_config, 
                                                  _catalogDocumentFactory, 
                                                  _catalogElementFactory,
                                                  _resourceElementFactory, 
                                                  _epiApi,
                                                  _epiMappingHelper, 
                                                  _documentFileHelper,
                                                  _pimFieldAdapter,
                                                  _entityService,
                                                  _catalogCodeGenerator);

                AppDomain.CurrentDomain.AssemblyResolve += CurrentDomainAssemblyResolve;

                InitConnector();

                base.Start();
                _started = true;
                ConnectorEventHelper.UpdateEvent(startEvent, "Connector has started", 100);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Error while starting connector", ex);
                ConnectorEventHelper.UpdateEvent(startEvent, "Issue while starting connector, see log.", 100, true);
                throw;
            }
        }

        public new void Stop()
        {
            base.Stop();
            var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.Stop, "Connector is stopping", 0);
            _started = false;
            _epiApi = null;
            _catalogElementFactory = null;
            _catalogDocumentFactory = null;
            _channelHelper = null;
            _resourceElementFactory = null;
            _epiMappingHelper = null;
            _catalogCodeGenerator = null;
            _publisher = null;
            _config = null;
            _documentFileHelper = null;
            _entityService = null;
            _cvlUpdater = null;

            ConnectorEventHelper.UpdateEvent(connectorEvent, "Connector has stopped.", 100);
        }

        public new void InitConfigurationSettings()
        {
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.PublishFolder, ConfigDefaults.PublishFolder);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ResourcesPublishFolder, ConfigDefaults.ResourcesPublishFolder);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ResourceConfiguration, ConfigDefaults.ResourceConfiguration);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.LanguageMapping, ConfigDefaults.LanguageMapping);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ItemToSkus, ConfigDefaults.ItemToSkus);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.CvlData, ConfigDefaults.CvlData);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.BundleTypes, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.PackageTypes, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.DynamicPackageTypes, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ChannelId, "123");
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiCodeFields, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ExcludeFields, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiNameFields, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.UseThreeLevelsInCommerce, ConfigDefaults.UseThreeLevelsinCommerce);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.HttpPostUrl, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiEndpoint, ConfigDefaults.EpiEndpoint);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiApiKey, ConfigDefaults.EpiApiKey);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.EpiTimeout, ConfigDefaults.EpiTimeout);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ExportEntities, ConfigDefaults.ExportEntities);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.BatchSize, string.Empty);
            ConfigurationManager.Instance.SetConnectorSetting(Id, ConfigKeys.ForceIncludeLinkedContent, ConfigDefaults.ForceIncludeLinkedContent);
        }

        public new bool IsStarted => _started;
        
        public void Publish(int channelId)
        {
            DoWithInitCheck(channelId, ConnectorEventType.Publish, channelEntity => _publisher.Publish(channelEntity));
        }

        public void UnPublish(int channelId)
        {
            if (channelId != _config.ChannelId)
                return;

            IntegrationLogger.Write(LogLevel.Information, $"Unpublish on channel: {channelId} called. No action taken.");
        }

        public void Synchronize(int channelId)
        {
        }

        public void ChannelEntityAdded(int channelId, int entityId)
        {
            DoWithInitCheck(channelId, ConnectorEventType.ChannelEntityAdded, channel => _publisher.ChannelEntityAdded(channel, entityId));
        }
        
        public void ChannelEntityUpdated(int channelId, int entityId, string data)
        {
            DoWithInitCheck(channelId, ConnectorEventType.ChannelEntityAdded, channel =>
            {
                if (channel.Id == entityId)
                {
                    var connectorEvent = ConnectorEventHelper.InitiateEvent(_config, ConnectorEventType.ChannelEntityUpdated, "Updated Entity is the Channel, no action required", 100);
                    return connectorEvent;
                }

                return _publisher.ChannelEntityUpdated(channel, entityId, data);
            });
        }

        public void ChannelEntityDeleted(int channelId, Entity deletedEntity)
        {
            DoWithInitCheck(channelId, ConnectorEventType.ChannelEntityDeleted, channel => _publisher.ChannelEntityDeleted(channel, deletedEntity));
        }

        public void ChannelEntityFieldSetUpdated(int channelId, int entityId, string fieldSetId)
        {
            ChannelEntityUpdated(channelId, entityId, null);
        }

        public void ChannelEntitySpecificationFieldAdded(int channelId, int entityId, string fieldName)
        {
            ChannelEntityUpdated(channelId, entityId, null);
        }

        public void ChannelEntitySpecificationFieldUpdated(int channelId, int entityId, string fieldName)
        {
            ChannelEntityUpdated(channelId, entityId, null);
        }

        public void ChannelLinkAdded(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            DoWithInitCheck(channelId, ConnectorEventType.ChannelLinkAdded, 
                channel => _publisher.ChannelLinkAdded(channel, sourceEntityId, targetEntityId, linkTypeId, linkEntityId));
        }

        public void ChannelLinkDeleted(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            DoWithInitCheck(channelId, ConnectorEventType.ChannelLinkAdded, 
                channel => _publisher.ChannelLinkDeleted(channel, sourceEntityId, targetEntityId, linkTypeId, linkEntityId)
            );
        }

        public void ChannelLinkUpdated(int channelId, int sourceEntityId, int targetEntityId, string linkTypeId, int? linkEntityId)
        {
            DoWithInitCheck(channelId, ConnectorEventType.ChannelLinkAdded, channel =>
                _publisher.ChannelLinkUpdated(channel, sourceEntityId, targetEntityId, linkTypeId, linkEntityId)
            );
        }

        public void AssortmentCopiedInChannel(int channelId, int assortmentId, int targetId, string targetType)
        {

        }

        private void InitConnector()
        {
            try
            {
                var directoryInfo = Directory.CreateDirectory(_config.PublicationsRootPath);

                var accessCheckerFileName = "Access_check";
                var filename = Path.Combine(directoryInfo.FullName, accessCheckerFileName);

                File.WriteAllText(filename, "Access-checker");
                File.Delete(filename);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, $"Attempt to write to {_config.PublicationsRootPath} failed.", ex);
                throw;
            }
        }
        
        private void DoWithInitCheck(int channelId, ConnectorEventType eventType, Func<Entity, ConnectorEvent> thingsToDo)
        {
            if (channelId != _config.ChannelId)
                return;

            var channelEntity = _channelHelper.InitiateChannelConfiguration(channelId);
            if (channelEntity == null)
            {
                ConnectorEventHelper.InitiateEvent(_config, eventType, $"Failed perform {eventType}. Could not find the channel.", -1, true);
                return;
            }

            try
            {
                var connectorEvent = thingsToDo(channelEntity);

                _entityService.FlushCache();
                _resourceElementFactory.FlushCache();

                var message = $"{eventType} done for channel {channelEntity.Id} ({channelEntity.DisplayName})";

                ConnectorEventHelper.UpdateEvent(connectorEvent, message, 100);
            }
            catch (Exception ex)
            {
                IntegrationLogger.Write(LogLevel.Error, "Exception in ChannelEntityAdded", ex);
                ConnectorEventHelper.InitiateEvent(_config, eventType, ex.Message, -1, true);
            }
        }

        public void CVLValueCreated(string cvlId, string cvlValueKey)
        {
        }

        public void CVLValueUpdated(string cvlId, string cvlValueKey)
        {
            DoWithInitCheck(_config.ChannelId, ConnectorEventType.CVLValueUpdated, channelEntity => _cvlUpdater.CVLValueUpdated(channelEntity, cvlId, cvlValueKey));
        }

        public void CVLValueDeleted(string cvlId, string cvlValueKey)
        {
        }

        public void CVLValueDeletedAll(string cvlId)
        {
        }

        private Assembly CurrentDomainAssemblyResolve(object sender, ResolveEventArgs args)
        {
            string folderPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            if (folderPath == null)
                return null;

            int ix = folderPath.LastIndexOf("\\", StringComparison.Ordinal);
            if (ix == -1)
            {
                return null;
            }

            folderPath = folderPath.Substring(0, ix);
            string assemblyPath = Path.Combine(folderPath, new AssemblyName(args.Name).Name + ".dll");

            if (File.Exists(assemblyPath) == false)
            {
                assemblyPath = Path.Combine(folderPath + "\\OutboundConnectors\\", new AssemblyName(args.Name).Name + ".dll");
                if (File.Exists(assemblyPath) == false)
                {
                    return null;
                }
            }

            Assembly assembly = Assembly.LoadFrom(assemblyPath);
            return assembly;
        }
    }
}