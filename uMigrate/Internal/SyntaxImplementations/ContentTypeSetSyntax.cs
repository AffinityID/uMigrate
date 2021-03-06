﻿using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using JetBrains.Annotations;
using Umbraco.Core;
using Umbraco.Core.Models;
using uMigrate.Fluent;

namespace uMigrate.Internal.SyntaxImplementations {
    public class ContentTypeSetSyntax : SetSyntaxBase<IContentType, IContentTypeSetSyntax, IContentTypeFilteredSetSyntax>, IContentTypePropertyGroupSetSyntax {
        private string _currentPropertyGroupName;

        public ContentTypeSetSyntax(IMigrationContext context, [CanBeNull] IReadOnlyList<IContentType> contentTypes = null)
            : base(context, () => contentTypes ?? context.Services.ContentTypeService.GetAllContentTypes().AsReadOnlyList())
        {
        }

        protected override IContentTypeSetSyntax NewSet(IEnumerable<IContentType> items) {
            return new ContentTypeSetSyntax(Context, items.ToArray());
        }

        protected override string GetName(IContentType item) {
            return item.Name;
        }

        [Obsolete(ObsoleteMessages.UseOverloadThatTakesName)]
        public IContentTypeSetSyntax Add(string alias, params Action<IContentType>[] setups) {
            return Add(alias, alias, setups.InvokeAll);
        }

        public IContentTypeSetSyntax Add(string alias, string name, Action<IContentType> setup = null) {
            Argument.NotNullOrEmpty(nameof(alias), alias);
            Argument.NotNullOrEmpty(nameof(name), name);
            return AddInternal(null, null, alias, name, setup);
        }

        [Obsolete(ObsoleteMessages.UseOverloadThatTakesName)]
        public IContentTypeSetSyntax AddChild(string alias, params Action<IContentType>[] setups) {
            return AddChild(alias, alias, setups.InvokeAll);
        }
        
        public IContentTypeSetSyntax AddChild(string alias, string name, Action<IContentType> setup = null, Action<IContentTypeFilteredSetSyntax> and = null) {
            Argument.NotNullOrEmpty(nameof(alias), alias);
            Argument.NotNullOrEmpty(nameof(name), name);

            return ChangeWithManualSave(c => {
                var child = AddInternal(c, c.Name, alias, name, setup);
                and?.Invoke(child);
            });
        }

        private IContentTypeSetSyntax AddInternal(IContentType parent, [CanBeNull] string parentName, string alias, string name, Action<IContentType> setup) {
            var contentTypes = Services.ContentTypeService.GetContentTypeChildren(parent?.Id ?? -1);
            var contentType = contentTypes.FirstOrDefault(t => t.Alias == alias);
            var isNew = false;
            if (contentType == null) {
                contentType = parent != null
                    ? new ContentType(parent)
                    : new ContentType(-1);
                contentType.Alias = alias;
                contentType.Name = name;
                isNew = true;
            }
            else {
                contentType.Name = name;
            }
            setup?.Invoke(contentType);

            Services.ContentTypeService.Save(contentType);
            Logger.Log("ContentType: {0} '{1}'{2}.", isNew ? "added" : "updated", contentType.Name, parentName != null ? " under " + parentName : "");
            return NewSet(contentType);
        }

        public IContentTypeSetSyntax AllowUnder(IContentTypeFilteredSetSyntax otherContentTypes) {
            otherContentTypes.Change(other => {
                var allowed = other.AllowedContentTypes.AsWriteableCollection();
                ChangeWithManualSave(c => {
                    if (allowed.Any(a => a.Id.Value == c.Id))
                        return;

                    allowed.Add(new ContentTypeSort {
                        Id = new Lazy<int>(() => c.Id),
                        Alias = c.Alias,
                        SortOrder = 0
                    });

                    Logger.Log("ContentType: '{0}', allowed under '{1}'.", c.Name, other.Name);
                });
                other.AllowedContentTypes = allowed;
            });

            return this;
        }

        public IContentTypeSetSyntax AllowUnder(string otherContentTypeAlias) {
            var otherContentType = Services.ContentTypeService.GetContentType(otherContentTypeAlias);
            Ensure.That(otherContentType != null, "Content type '{0}' was not found.", otherContentTypeAlias);

            return AllowUnder(NewSet(otherContentType));
        }

        public IContentTypeSetSyntax SetParent(string parentContentTypeAlias, bool removeOldParentFromCompositions = false) {
            if (parentContentTypeAlias == null)
                return SetParent((IContentType) null, removeOldParentFromCompositions);

            var parentContentType = Services.ContentTypeService.GetContentType(parentContentTypeAlias);
            Ensure.That(parentContentType != null, "Content type '{0}' was not found.", parentContentTypeAlias);
            return SetParent(parentContentType, removeOldParentFromCompositions);
        }

        public IContentTypeSetSyntax SetParent(IContentType parentContentType, bool removeOldParentFromCompositions = false) {
            return Change(c => {
                if (removeOldParentFromCompositions) {
                    var oldParentComposition = c.ContentTypeComposition.FirstOrDefault(cm => cm.Id == c.ParentId);
                    if (oldParentComposition != null)
                        c.RemoveContentType(oldParentComposition.Alias);
                }

                if (parentContentType != null) {
                    c.ParentId = parentContentType.Id;
                    c.AddContentType(parentContentType);
                    Logger.Log("ContentType: '{0}', set parent to '{1}'.", c.Name, parentContentType.Name);
                }
                else {
                    c.ParentId = Constants.System.Root;
                    Logger.Log("ContentType: '{0}', set parent to none.", c.Name);
                }
            });
        }

        public IContentTypePropertyGroupSetSyntax PropertyGroup(string name) {
            if (name == null) {
                _currentPropertyGroupName = null;
                return this;
            }

            Ensure.That(Objects.Any(o => o.PropertyGroups.Contains(name)), "Property group '{0}' was not found on any content type.", name);
            _currentPropertyGroupName = name;
            return this;
        }

        public IContentTypePropertyGroupSetSyntax AddPropertyGroup(string name) {
            Change(c => AddPropertyGroupIfNeededInternal(c, name));
            _currentPropertyGroupName = name;
            return this;
        }

        private void AddPropertyGroupIfNeededInternal([NotNull] IContentType contentType, [CanBeNull] string name) {
            if (name == null)
                return;

            if (contentType.AddPropertyGroup(name))
                Logger.Log("ContentType: '{0}', added tab '{1}'.", contentType.Name, name);
        }

        public IContentTypeSetSyntax RenamePropertyGroup(string oldName, string newName) {
            ChangeWithManualSave(contentType => {
                var alreadyRenamed = GetPropertyGroupOrNull(contentType, newName);
                if (alreadyRenamed != null) {
                    HandleAlreadyRenamedPropertyGroup(contentType, oldName, alreadyRenamed);
                    return;
                }

                var group = EnsurePropertyGroup(contentType, oldName);
                Context.WorkaroundToRenamePropertyGroupAndSave(group, newName);
                Logger.Log("ContentType: '{0}', renamed tab '{1}' to '{2}'.", contentType.Name, oldName, newName);
            });

            return this;
        }

        private void HandleAlreadyRenamedPropertyGroup(IContentType contentType, string oldName, PropertyGroup groupWithNewName) {
            var oldGroup = GetPropertyGroupOrNull(contentType, oldName);
            if (oldGroup == null)
                return;

            if (oldGroup.PropertyTypes.Count > 0) {
                var propertiesToMove = oldGroup.PropertyTypes.ToArray();
                foreach (var property in propertiesToMove) {
                    MovePropertyInternal(contentType, property, groupWithNewName.Name);
                }
            }

            RemovePropertyGroupInternal(contentType, oldGroup.Name);
        }

        public IContentTypeSetSyntax SortPropertyGroups(Func<PropertyGroup, PropertyGroup, int> compare) {
            return Change(contentType => {
                var sorted = contentType.PropertyGroups.ToList();
                sorted.Sort((a, b) => compare(a, b));

                for (var i = 0; i < sorted.Count; i++) {
                    var group = sorted[i];
                    SetSortOrderIfRequired(group, i, contentType);
                }
            });
        }

        public IContentTypeSetSyntax SortPropertyGroups(params string[] sortedPropertyGroupNames) {
            return Change(contentType => {
                for (var i = 0; i < sortedPropertyGroupNames.Length; i++) {
                    var name = sortedPropertyGroupNames[i];
                    var propertyGroup = EnsurePropertyGroup(contentType, name);
                    SetSortOrderIfRequired(propertyGroup, i, contentType);
                }
            });
        }

        private void SetSortOrderIfRequired(PropertyGroup group, int newSortOrder, IContentType contentType) {
            if (group.SortOrder == newSortOrder)
                return;

            Logger.Log("ContentType: '{0}', tab '{1}', changing sort order: {2} => {3}.", contentType.Name, group.Name, group.SortOrder, newSortOrder);
            group.SortOrder = newSortOrder;
        }

        public IContentTypeSetSyntax RemovePropertyGroup(string name) {
            Argument.NotNullOrEmpty(nameof(name), name);
            return Change(contentType => RemovePropertyGroupInternal(contentType, name));
        }

        private void RemovePropertyGroupInternal(IContentType contentType, string name) {
            if (!contentType.PropertyGroups.Contains(name)) {
                Logger.Log($"ContentType: '{contentType.Name}', tab '{name}' does not exist — no need to remove.");
                return;
            }

            contentType.RemovePropertyGroup(name);
            Logger.Log($"ContentType: '{contentType.Name}', removed tab '{name}'.");
        }

        [Obsolete(ObsoleteMessages.UseAddPropertyFromPropertyGroup)]
        public IContentTypeSetSyntax AddProperty(string propertyAlias, string dataTypeName, string propertyGroupName = null, params Action<PropertyType>[] setups) {
            Argument.NotNullOrEmpty(nameof(propertyAlias), propertyAlias);
            Argument.NotNullOrEmpty(nameof(dataTypeName), dataTypeName);

            var dataType = Services.DataTypeService.GetAllDataTypeDefinitions().SingleOrDefault(t => t.Name == dataTypeName);
            Ensure.That(dataType != null, "Data type '{0}' was not found.", dataTypeName);

            return AddPropertyInternal(propertyAlias, propertyAlias, dataType, propertyGroupName, setups.InvokeAll);
        }

        public IContentTypePropertyGroupSetSyntax AddProperty(string propertyAlias, string propertyName, string dataTypeName, Action<PropertyType> setup = null) {
            Argument.NotNullOrEmpty(nameof(propertyAlias), propertyAlias);
            Argument.NotNullOrEmpty(nameof(propertyName), propertyName);
            Argument.NotNullOrEmpty(nameof(dataTypeName), dataTypeName);

            var dataType = Services.DataTypeService.GetAllDataTypeDefinitions().SingleOrDefault(t => t.Name == dataTypeName);
            Ensure.That(dataType != null, "Data type '{0}' was not found.", dataTypeName);

            return AddProperty(propertyAlias, propertyName, dataType, setup);
        }

        [Obsolete(ObsoleteMessages.UseAddPropertyFromPropertyGroup)]
        public IContentTypeSetSyntax AddProperty(string propertyAlias, IDataTypeDefinition dataType, string propertyGroupName = null, params Action<PropertyType>[] setups) {
            return AddPropertyInternal(propertyAlias, propertyAlias, dataType, propertyGroupName, setups.InvokeAll);
        }

        public IContentTypePropertyGroupSetSyntax AddProperty(string propertyAlias, string propertyName, IDataTypeDefinition dataType, Action<PropertyType> setup = null) {
            return AddPropertyInternal(propertyAlias, propertyName, dataType, null, setup);
        }

        private IContentTypePropertyGroupSetSyntax AddPropertyInternal([NotNull] string propertyAlias, [NotNull] string propertyName, [NotNull] IDataTypeDefinition dataType, [CanBeNull] string obsoletePropertyGroupNameOverride, Action<PropertyType> setup) {
            Argument.NotNullOrEmpty(nameof(propertyAlias), propertyAlias);
            Argument.NotNullOrEmpty(nameof(propertyName), propertyName);
            Argument.NotNull(nameof(dataType), dataType);

            return (IContentTypePropertyGroupSetSyntax)Change(contentType => {
                var propertyType = contentType.PropertyTypes.SingleOrDefault(t => t.Alias == propertyAlias);
                if (propertyType != null) {
                    UpdatePropertyInsteadOfAdding(contentType, propertyType, dataType, setup);
                    return;
                }

                var propertyGroupName = obsoletePropertyGroupNameOverride ?? _currentPropertyGroupName;
                propertyType = new PropertyType(dataType) {
                    Name = propertyName,
                    Alias = propertyAlias,
                    Description = "" // must not be null, or PackageService and/or uSync would crash
                };
                setup?.Invoke(propertyType);

                if (propertyGroupName != null) {
                    AddPropertyGroupIfNeededInternal(contentType, propertyGroupName);
                    contentType.AddPropertyType(propertyType, propertyGroupName);
                }
                else {
                    contentType.AddPropertyType(propertyType);
                }

                Logger.Log(
                    "ContentType: '{0}', added property '{1}' ('{2}') of type '{3}'{4}{5}.",
                    contentType.Name, propertyType.Name, propertyAlias, dataType.Name,
                    propertyGroupName != null ? " to tab '" + propertyGroupName + "'" : "",
                    propertyType.Mandatory ? ", mandatory" : ""
                );
            });
        }

        private void UpdatePropertyInsteadOfAdding(IContentType contentType, PropertyType propertyType, IDataTypeDefinition newDataType, Action<PropertyType> setup = null) {
            if (propertyType.DataTypeDefinitionId != newDataType.Id) {
                var message = $"Property '{propertyType.Alias}' already exists, but has type id '{propertyType.DataTypeDefinitionId}' instead of '{newDataType.Id}'. Changing property type through AddProperty is not supported.";
                throw new NotSupportedException(message);
            }

            var oldName = propertyType.Name;
            var oldMandatory = propertyType.Mandatory;

            setup?.Invoke(propertyType);

            Logger.Log(
                "ContentType: '{0}', updated property '{1}' ('{2}'){3}{4}.",
                contentType.Name, propertyType.Name, propertyType.Alias,
                oldName == propertyType.Name ? "" : $", renamed from '{oldName}' to '{propertyType.Name}'",
                oldMandatory == propertyType.Mandatory ? "" : $", mandatory {(oldMandatory ? "yes" : "no")} => {(propertyType.Mandatory ? "yes" : "no")}"
            );
        }

        public IContentTypeSetSyntax RemoveProperty(string propertyAlias) {
            return Change(contentType => contentType.RemovePropertyType(propertyAlias));
        }

        public IContentTypeSetSyntax ChangeProperties(Func<PropertyType, IContentType, bool> filter, Action<PropertyType, IContentType> change) {
            // using manual save here because there is no reason to save if no properties match filter
            ChangeWithManualSave(contentType => {
                var properties = contentType.PropertyTypes.Where(p => filter(p, contentType)).ToArray();
                if (properties.Length == 0)
                    return;

                properties.MigrateEach(p => change(p, contentType));
                Services.ContentTypeService.Save(contentType);
            });

            return this;
        }

        public IContentTypeSetSyntax ChangeProperty(string propertyAlias, Action<PropertyType> change) {
            return ChangeProperty(propertyAlias, (p, c) => change(p));
        }

        public IContentTypeSetSyntax ChangeProperty(string propertyAlias, Action<PropertyType, IContentType> change) {
            Change(contentType => {
                var property = EnsureProperty(contentType, propertyAlias);
                change(property, contentType);
            });

            return this;
        }

        public IContentTypeSetSyntax ChangeProperty<TFrom, TTo>(string propertyAlias, Action<PropertyType> change, Func<TFrom, TTo> convert) {
            return ChangeWithManualSave(t => ChangeProperty(t, propertyAlias, change, convert));
        }

        private void ChangeProperty<TFrom, TTo>(IContentType contentType, string propertyAlias, Action<PropertyType> change, Func<TFrom, TTo> convert) {
            var oldProperty = EnsureProperty(contentType, propertyAlias);
            var newProperty = oldProperty.CloneUsing(Services.DataTypeService);
            change(newProperty);

            if (oldProperty.Alias == newProperty.Alias)
                oldProperty.Alias += "Old" + DateTime.Now.ToString("yyyyMMddHHmmss");

            Services.ContentTypeService.Save(contentType);
            ChangeContents(contentType, c => true, c => {
                var oldValue = c.GetValue<TFrom>(oldProperty.Alias);
                var newValue = convert(oldValue);
                c.SetValue(newProperty.Alias, newValue);
            });

            contentType.RemovePropertyType(oldProperty.Alias);
            Services.ContentTypeService.Save(contentType);
        }

        public IContentTypeSetSyntax MoveProperty(string propertyAlias, string newPropertyGroupName) {
            return Change(contentType => {
                var property = EnsureProperty(contentType, propertyAlias);
                AddPropertyGroupIfNeededInternal(contentType, newPropertyGroupName);
                MovePropertyInternal(contentType, property, newPropertyGroupName);
            });
        }

        private void MovePropertyInternal([NotNull] IContentType contentType, [NotNull] PropertyType property, [CanBeNull] string newPropertyGroupName) {
            if (newPropertyGroupName == null) {
                // This does not seem to be supported by Umbraco API: http://issues.umbraco.org/issue/U4-6832.
                var previousPropertyGroup = contentType.PropertyGroups.SingleOrDefault(g => g.PropertyTypes.Contains(property));
                previousPropertyGroup?.PropertyTypes.Remove(property);

                // This is required as PropertyGroup reference would not be reset otherwise
                var propertyDataType = Context.Services.DataTypeService.GetDataTypeDefinitionById(property.DataTypeDefinitionId);
                var recreatedProperty = new PropertyType(propertyDataType) {
                    Id = property.Id,
                    Alias = property.Alias,
                    Key = property.Key,
                    CreateDate = property.CreateDate,
                    Description = property.Description,
                    Mandatory = property.Mandatory,
                    Name = property.Name,
                    SortOrder = property.SortOrder,
                    UpdateDate = property.UpdateDate,
                    ValidationRegExp = property.ValidationRegExp
                };

                contentType.AddPropertyType(recreatedProperty);
                Logger.Log("ContentType: '{0}', moved property '{1}' to default tab.", contentType.Name, property.Alias);
                return;
            }

            contentType.MovePropertyType(property.Alias, newPropertyGroupName);
            Logger.Log("ContentType: '{0}', moved property '{1}' to tab '{2}'.", contentType.Name, property.Alias, newPropertyGroupName);
        }

        public IContentTypeSetSyntax SortProperties(string propertyGroupName, Func<PropertyType, PropertyType, int> compare) {
            return Change(contentType => {
                var group = EnsurePropertyGroup(contentType, propertyGroupName);

                var propertiesSorted = group.PropertyTypes.ToList();
                propertiesSorted.Sort((a, b) => compare(a, b));

                for (var i = 0; i < propertiesSorted.Count; i++) {
                    var property = propertiesSorted[i];
                    SetSortOrderIfRequired(property, i, contentType);
                }
            });
        }

        public IContentTypeSetSyntax SortProperties(string propertyGroupName, params string[] sortedPropertyAliases) {
            return Change(contentType => {
                for (var i = 0; i < sortedPropertyAliases.Length; i++) {
                    var alias = sortedPropertyAliases[i];
                    var property = EnsureProperty(contentType, alias);
                    SetSortOrderIfRequired(property, i, contentType);
                }
            });
        }

        private void SetSortOrderIfRequired(PropertyType property, int newSortOrder, IContentType contentType) {
            if (property.SortOrder == newSortOrder)
                return;

            Logger.Log("ContentType: '{0}', property '{1}', changing sort order: {2} => {3}.", contentType.Name, property.Name, property.SortOrder, newSortOrder);
            property.SortOrder = newSortOrder;
        }

        public IContentTypeSetSyntax Change(Action<IContentType> change) {
            ChangeWithManualSave(change);
            Services.ContentTypeService.Save(Objects);
            Context.ClearRuntimeCache(typeof(IContentType));
            return this;
        }

        public IContentTypeSetSyntax AllowTemplate(ITemplate template) {
            return Change(c => {
                var list = c.AllowedTemplates.AsWriteableCollection();
                list.Add(template);
                c.AllowedTemplates = list;

                Logger.Log("ContentType: '{0}', allowed template '{1}'.", c.Name, template.Id);
            });
        }

        public IContentTypeSetSyntax DefaultTemplate(ITemplate template) {
            return Change(c => {
                c.SetDefaultTemplate(template);
                Logger.Log(
                    "ContentType: '{0}', set default template to '{1}'.",
                    c.Name, template != null ? template.Id.ToString(CultureInfo.InvariantCulture) : "none"
                );
            });
        }

        public IContentTypeFilteredSetSyntax Parent() {
            var parents = Objects
                .Where(o => o.ParentId != Constants.System.Root)
                .Select(o => Services.ContentTypeService.GetContentType(o.ParentId));

            return NewSet(parents);
        }

        public IContentTypeFilteredSetSyntax DescendantsAndSelf() {
            var newTypes = new List<IContentType>();

            var current = Objects;
            while (current.Count > 0) {
                newTypes.AddRange(current);
                current = current.SelectMany(t => Services.ContentTypeService.GetContentTypeChildren(t.Id)).ToArray();
            }

            return NewSet(newTypes);
        }

        public IEnumerable<IContent> GetAllContents() {
            return Objects.SelectMany(type => Services.ContentService.GetContentOfContentType(type.Id));
        }

        public IContentTypeSetSyntax ChangeContents(Func<IContent, bool> filter, Action<IContent> change) {
            return ChangeWithManualSave(type => ChangeContents(type, filter, change));
        }

        private void ChangeContents(IContentType contentType, Func<IContent, bool> filter, Action<IContent> change) {
            var savePublish = Context.Configuration.ContentSavePublish;
            var contents = Services.ContentService
                .GetContentOfContentType(contentType.Id)
                .SelectMany(c => savePublish.GetVersionsToChange(c, Context))
                .Where(filter);

            contents.MigrateEach(c => {
                change(c);
                savePublish.SaveAndOrPublish(c, Context);
            });
        }

        public IContentTypeSetSyntax Delete(string alias) {
            Argument.NotNull(nameof(alias), alias);
            var contentType = Services.ContentTypeService.GetContentType(alias);
            if (contentType == null) {
                Logger.Log("ContentType: '{0}' doesn't exist, no need to delete.", alias);
                return this;
            }

            NewSet(contentType).Delete();
            return this;
        }

        public void Delete() {
            ChangeWithManualSave(c => {
                Services.ContentTypeService.Delete(c);
                Logger.Log("ContentType: '{0}' deleted.", c.Name);
            });
        }

        [NotNull]
        private static PropertyGroup EnsurePropertyGroup(IContentType contentType, string propertyGroupName) {
            var group = GetPropertyGroupOrNull(contentType, propertyGroupName);
            Ensure.That(
                group != null,
                "Property group '{0}' was not found on '{1}'. Available property groups: '{2}'.",
                propertyGroupName, contentType.Name, string.Join("', '", contentType.PropertyGroups.Select(p => p.Name).OrderBy(n => n))
            );
            return group;
        }

        [CanBeNull]
        private static PropertyGroup GetPropertyGroupOrNull(IContentType contentType, string propertyGroupName) {
            if (propertyGroupName == null) {
                return new PropertyGroup(new PropertyTypeCollection(
                    contentType.PropertyTypes.Except(
                        contentType.PropertyGroups.SelectMany(g => g.PropertyTypes)
                    )
                ));
            }

            return contentType.PropertyGroups.SingleOrDefault(g => g.Name == propertyGroupName);
        }

        [NotNull]
        private static PropertyType EnsureProperty(IContentType contentType, string propertyAlias) {
            var properties = contentType.PropertyTypes.ToArray();
            var property = properties.SingleOrDefault(p => p.Alias == propertyAlias);
            Ensure.That(
                property != null,
                "Property '{0}' was not found on '{1}'. Available properties: '{2}'.",
                propertyAlias, contentType.Name, string.Join("', '", properties.Select(p => p.Alias).OrderBy(a => a))
            );
            return property;
        }
    }
}