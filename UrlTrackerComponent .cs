﻿using InfoCaster.Umbraco.UrlTracker.Helpers;
using InfoCaster.Umbraco.UrlTracker.Models;
using InfoCaster.Umbraco.UrlTracker.Repositories;
using InfoCaster.Umbraco.UrlTracker.Services;
using InfoCaster.Umbraco.UrlTracker.Settings;
using Newtonsoft.Json.Linq;
using System;
using System.Linq;
using Umbraco.Core;
using Umbraco.Core.Composing;
using Umbraco.Core.Events;
using Umbraco.Core.Logging;
using Umbraco.Core.Migrations;
using Umbraco.Core.Migrations.Upgrade;
using Umbraco.Core.Models;
using Umbraco.Core.Models.PublishedContent;
using Umbraco.Core.Scoping;
using Umbraco.Core.Services;
using Umbraco.Core.Services.Implement;
using Umbraco.Web;

namespace InfoCaster.Umbraco.UrlTracker
{
    [RuntimeLevel(MinLevel = RuntimeLevel.Run)]
    public class UrlTrackerComposer : IUserComposer
    {
        public void Compose(Composition composition)
        {
            composition.Components().Append<UrlTrackerComponent>();

            composition.Register<IUrlTrackerHelper, UrlTrackerHelper>();
            composition.Register<IUrlTrackerLoggingHelper, UrlTrackerLoggingHelper>();
            composition.Register<IUrlTrackerRepository, UrlTrackerRepository>();
            composition.Register<IUrlTrackerCacheService, UrlTrackerCacheService>();
            composition.Register<IUrlTrackerService, UrlTrackerService>();

            composition.Register<IUrlTrackerSettings, UrlTrackerSettings>(Lifetime.Singleton);
        }
    }

    public class UrlTrackerComponent : IComponent
    {
        private readonly IScopeProvider _scopeProvider;
        private readonly IMigrationBuilder _migrationBuilder;
        private readonly IKeyValueService _keyValueService;
        private readonly ILogger _logger;
        private readonly IUrlTrackerService _urlTrackerService;
        private readonly IUrlTrackerSettings _urlTrackerSettings;

        public UrlTrackerComponent(
            IUrlTrackerSettings urlTrackerSettings,
            IUrlTrackerService urlTrackerService,
            IScopeProvider scopeProvider,
            IMigrationBuilder migrationBuilder,
            IKeyValueService keyValueService,
            ILogger logger)
        {
            _urlTrackerSettings = urlTrackerSettings;
            _scopeProvider = scopeProvider;
            _migrationBuilder = migrationBuilder;
            _keyValueService = keyValueService;
            _logger = logger;
            _urlTrackerService = urlTrackerService;
        }

        public void Initialize()
        {
            var migrationPlan = new MigrationPlan("UrlTracker");
            migrationPlan.From(string.Empty).To<AddUrlTrackerTables>("urlTracker");

            var upgrader = new Upgrader(migrationPlan);
            upgrader.Execute(_scopeProvider, _migrationBuilder, _keyValueService, _logger);

            if (!_urlTrackerSettings.IsDisabled() && !_urlTrackerSettings.IsTrackingDisabled())
            {
                //todo: resolve check from migration and execute this

                ContentService.Moving += ContentService_Moving;
                ContentService.Publishing += ContentService_Publishing;
                ContentService.Published += ContentService_Published;
                ContentService.Trashed += ContentService_Trashed;

                DomainService.Deleted += DomainService_Deleted;
                DomainService.Saved += DomainService_Saved;
            }
        }

        public void Terminate() { }

        private void DomainService_Saved(IDomainService sender, SaveEventArgs<IDomain> e)
        {
            _urlTrackerService.ClearDomains();
        }

        private void DomainService_Deleted(IDomainService sender, DeleteEventArgs<IDomain> e)
        {
            _urlTrackerService.ClearDomains();
        }

        private void ContentService_Trashed(IContentService sender, MoveEventArgs<IContent> e)
        {
#if !DEBUG
            try
            {
#endif
                foreach (var moved in e.MoveInfoCollection)
                {
                    IContent movedContent = moved.Entity;

                    if (movedContent == null)
                        return;

                    _urlTrackerService.ConvertRedirectTo410ByNodeId(movedContent.Id);
                }
#if !DEBUG
            }
            catch (Exception ex)
            {
                _logger.Error<UrlTrackerComponent>(ex);
            }
#endif
        }

        private void ContentService_Publishing(IContentService sender, ContentPublishingEventArgs e)
        {
            // When content is renamed or 'umbracoUrlName' property value is added/updated
            foreach (IContent newContent in e.PublishedEntities)
            {
#if !DEBUG
                try
#endif
                {
                    var oldContent = _urlTrackerService.GetNodeById(newContent.Id);

                    if (oldContent != null) // If old content is null, it's a new document
                    {
                        if (newContent.AvailableCultures.Any())
                        {
                            foreach (var culture in newContent.PublishedCultures)
                                MatchNodes(newContent, oldContent, culture);
                        }
                        else
                        {
                            MatchNodes(newContent, oldContent);
                        }
                    }
                }
#if !DEBUG
                catch (Exception ex)
                {
                    _logger.Error<UrlTrackerComponent>(ex);
                }
#endif
            }
        }

        private void ContentService_Published(IContentService sender, ContentPublishedEventArgs e)
        {
            foreach (IContent content in e.PublishedEntities)
            {
                _urlTrackerService.Convert410To301ByNodeId(content.Id);
            }
        }

        private void ContentService_Moving(IContentService sender, MoveEventArgs<IContent> e)
        {
#if !DEBUG
            try
            {
#endif
                foreach (var moved in e.MoveInfoCollection)
                {
                    IContent newContent = moved.Entity;

                    if (newContent == null)
                        return;

                    var oldContent = _urlTrackerService.GetNodeById(newContent.Id);

                    if (oldContent != null && !string.IsNullOrEmpty(oldContent.Url) && oldContent.Parent.Id != moved.NewParentId)
                    {
                        if (newContent.AvailableCultures.Any())
                        {
                            foreach (var culture in newContent.AvailableCultures)
                                _urlTrackerService.AddRedirect(newContent, oldContent, UrlTrackerHttpCode.MovedPermanently, UrlTrackerReason.Moved, culture);
                        }
                        else
                        {
                            _urlTrackerService.AddRedirect(newContent, oldContent, UrlTrackerHttpCode.MovedPermanently, UrlTrackerReason.Moved);
                        }
                    }
                }
#if !DEBUG
            }
            catch (Exception ex)
            {
                 _logger.Error<UrlTrackerComponent>(ex);
            }
#endif
        }

        private void MatchNodes(IContent newContent, IPublishedContent oldContent, string culture = "")
        {
            var newContentName = string.IsNullOrEmpty(culture) ? newContent.Name : newContent.CultureInfos[culture].Name;
            var oldContentName = oldContent.Name(culture);

            var newContentUmbracoUrlName = newContent.GetValue("umbracoUrlName", culture)?.ToString() ?? "";
            var oldContentUmbracoUrlName = oldContent.Value("umbracoUrlName", culture)?.ToString() ?? "";

            if (newContentUmbracoUrlName != oldContentUmbracoUrlName)
                _urlTrackerService.AddRedirect(newContent, oldContent, UrlTrackerHttpCode.MovedPermanently, UrlTrackerReason.UrlOverwritten, culture);
            else if (!string.IsNullOrEmpty(oldContentName) && newContentName != oldContentName)
                _urlTrackerService.AddRedirect(newContent, oldContent, UrlTrackerHttpCode.MovedPermanently, UrlTrackerReason.Renamed, culture);
            else if (_urlTrackerSettings.IsSEOMetadataInstalled() && newContent.HasProperty(_urlTrackerSettings.GetSEOMetadataPropertyName()))
            {
                var newContentSEOMetadata = newContent.GetValue(_urlTrackerSettings.GetSEOMetadataPropertyName(), culture)?.ToString() ?? "";
                var oldContentSEOMetadata = oldContent.Value(_urlTrackerSettings.GetSEOMetadataPropertyName(), culture)?.ToString() ?? "";

                if (!newContentSEOMetadata.Equals(oldContentSEOMetadata))
                {
                    dynamic contentJson = JObject.Parse(newContentSEOMetadata);
                    string newContentUrlName = contentJson.urlName;

                    dynamic nodeJson = JObject.Parse(oldContentSEOMetadata);
                    string oldContentUrlName = nodeJson.urlName;

                    if (newContentUrlName != oldContentUrlName) // SEOMetadata UrlName property value added/changed
                        _urlTrackerService.AddRedirect(newContent, oldContent, UrlTrackerHttpCode.MovedPermanently, UrlTrackerReason.UrlOverwrittenSEOMetadata, culture);
                }
            }
        }
    }

    public class AddUrlTrackerTables : MigrationBase
    {
        public AddUrlTrackerTables(IMigrationContext context) : base(context) { }

        public override void Migrate()
        {
            Logger.Debug<AddUrlTrackerTables>("Running migration {MigrationStep}", "AddUrlTrackerTables");

            if (!TableExists("icUrlTracker"))
                Create.Table<UrlTrackerSchema>().Do();
            else
                Logger.Debug<AddUrlTrackerTables>("The database table {DbTable} already exists, skipping", "icUrlTracker");

            if (!TableExists("icUrlTrackerIgnore404"))
                Create.Table<UrlTrackerIgnore404Schema>().Do();
            else
                Logger.Debug<AddUrlTrackerTables>("The database table {DbTable} already exists, skipping", "icUrlTrackerIgnore404");
        }

    }
}