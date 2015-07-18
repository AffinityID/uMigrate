﻿using System;
using System.Collections.Generic;
using JetBrains.Annotations;
using Umbraco.Core.Persistence;
using Umbraco.Core.Persistence.DatabaseAnnotations;

namespace uMigrate.Internal {
    [TableName(DefaultTableName)]
    public class MigrationRecord {
        public const string DefaultTableName = "migrationRecord";
        
        // ReSharper disable once NotNullMemberIsNotInitialized
        [NotNull, Length(50), PrimaryKeyColumn(AutoIncrement = false, Name = "PK_MigrationRecord")]
        public string Version { get; set; }

        // ReSharper disable once NotNullMemberIsNotInitialized
        [NotNull, Length(200)]
        public string Name { get; set; }

        public DateTime DateExecuted { get; set; }

        [CanBeNull, NullSetting, Length(2048)]
        public string Log { get; set; }
    }
}