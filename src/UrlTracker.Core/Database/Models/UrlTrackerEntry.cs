﻿using System;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using NPoco;
using Umbraco.Core.Persistence.DatabaseAnnotations;
using Umbraco.Core.Persistence.DatabaseModelDefinitions;

namespace UrlTracker.Core.Database.Models
{
    [DebuggerDisplay("{Id} | {OldUrl} | {OldRegex} | {RedirectNodeId} | {RedirectUrl} | {RedirectHttpCode}")]
    [TableName("icUrlTracker")]
    [PrimaryKey("Id", AutoIncrement = true)]
    [ExcludeFromCodeCoverage]
    public class UrlTrackerEntry
    {
        [Column("Id")]
        [PrimaryKeyColumn(AutoIncrement = true, IdentitySeed = 1)]
        public int Id { get; set; }

        [Column("Culture"), Length(10), NullSetting(NullSetting = NullSettings.Null)]
        public string Culture { get; set; }

        [Column("OldUrl"), Length(255), NullSetting(NullSetting = NullSettings.Null)]
        public string OldUrl { get; set; }

        [Column("OldRegex"), Length(255), NullSetting(NullSetting = NullSettings.Null)]
        public string OldRegex { get; set; }

        [Column("RedirectRootNodeId"), NullSetting(NullSetting = NullSettings.Null)]
        public int? RedirectRootNodeId { get; set; }

        [Column("RedirectNodeId"), NullSetting(NullSetting = NullSettings.Null)]
        public int? RedirectNodeId { get; set; }

        [Column("RedirectUrl"), Length(255), NullSetting(NullSetting = NullSettings.Null)]
        public string RedirectUrl { get; set; }

        [Column("RedirectHttpCode"), NullSetting(NullSetting = NullSettings.Null)]
        public int? RedirectHttpCode { get; set; }

        [Column("RedirectPassThroughQueryString")]
        public bool RedirectPassThroughQueryString { get; set; }

        [Column("ForceRedirect")]
        public bool ForceRedirect { get; set; }

        [Column("Notes"), Length(255), NullSetting(NullSetting = NullSettings.Null)]
        public string Notes { get; set; }

        [Column("Is404")]
        public bool Is404 { get; set; }

        [Column("Referrer"), Length(255), NullSetting(NullSetting = NullSettings.Null)]
        public string Referrer { get; set; }

        [Column("Inserted"), Constraint(Default = SystemMethods.CurrentDateTime), ComputedColumn]
        public DateTime Inserted { get; set; }
    }
}
