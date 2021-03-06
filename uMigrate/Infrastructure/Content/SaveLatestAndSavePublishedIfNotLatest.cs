﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Annotations;
using Umbraco.Core.Models;

namespace uMigrate.Infrastructure.Content {
    [PublicAPI]
    public class SaveLatestAndSavePublishedIfNotLatest : IContentSavePublish {
        public IEnumerable<IContent> GetVersionsToChange(IContent content, IMigrationContext context) {
            if (!content.Published) {
                var published = context.Services.ContentService.GetPublishedVersion(content.Id);
                if (published != null)
                    yield return published;
            }
            yield return content;
        }

        public void SaveAndOrPublish(IContent content, IMigrationContext context) {
            var contentService = context.Services.ContentService;
            if (content.Published) {
                contentService.SaveAndPublish(content);
            }
            else {
                contentService.Save(content);
            }
        }
    }
}
