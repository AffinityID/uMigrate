﻿using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Umbraco.Core;
using Umbraco.Core.Models;

namespace uMigrate.Tests.Integration {
    public class ContentTypeTests : IntegrationTestsBase {
        [Test]
        public void Add_SetsNameToProvidedValue() {
            RunMigration(m => m.ContentTypes.Add("test", "Test"));

            var contentType = Services.ContentTypeService.GetContentType("test");
            Assert.AreEqual("Test", contentType.Name);
        }

        [Test]
        public void AddChild_SetsNameToProvidedValue() {
            RunMigration(m => {
                m.ContentTypes.Add("parent", "Parent")
                 .AddChild("child", "Child");
            });

            var childType = Services.ContentTypeService.GetContentType("child");
            Assert.AreEqual("Child", childType.Name);
        }

        [Test]
        public void AddChild_CreatesChildWithInheritedProperty() {
            RunMigration(m => {
                m.ContentTypes
                 .Add("parent", "Parent")
                 .AddProperty("parentProperty", "Parent Property", m.DataTypes.Add("Test", Constants.PropertyEditors.TextboxAlias, null).Object)
                 .AddChild("child", "Child");
            });

            var childType = Services.ContentTypeService.GetContentType("child");
            Assert.IsTrue(childType.PropertyTypeExists("parentProperty"));
        }

        [Test]
        public void SetParent_ChangesParentToNewValue() {
            RunMigration(m => {
                m.ContentTypes.Add("parent", "Parent");
                m.ContentTypes.Add("child", "Child");
            });
            RunMigration(m => m.ContentType("child").SetParent("parent"));

            var childType = Services.ContentTypeService.GetContentType("child");
            var parentType = Services.ContentTypeService.GetContentType("parent");
            Assert.AreEqual(parentType.Id, childType.ParentId);
        }

        [Test]
        public void SetParent_RemovesOldParentFromCompositions_IfRemoveOldParentIsSet() {
            RunMigration(m => m.ContentTypes.Add("oldParent", "Old Parent").AddChild("child", "Child"));
            RunMigration(m => m.ContentType("child").SetParent((string)null, removeOldParentFromCompositions: true));

            var childType = Services.ContentTypeService.GetContentType("child");
            Assert.IsFalse(childType.ContentTypeCompositionExists("oldParent"));
        }

        [Test]
        public void SetParent_ChangesParentCorrectlySoThatPropertyTypesAreInherited() {
            RunMigration(m => {
                var stubDataType = StubDataType(m);
                m.ContentTypes.Add("parent", "Parent").AddProperty("parentProperty", "Parent Property", stubDataType);
                m.ContentTypes.Add("child", "Child").AddProperty("childProperty", "Child Property", stubDataType);
            });
            RunMigration(m => m.ContentType("child").SetParent("parent"));

            var childType = Services.ContentTypeService.GetContentType("child");
            Assert.IsTrue(childType.PropertyTypeExists("parentProperty"));
        }

        [Test]
        public void SetParent_RemovesParent_WhenSetToNull() {
            RunMigration(m => m.ContentTypes.Add("parent", "Parent").AddChild("child", "Child"));
            RunMigration(m => m.ContentType("child").SetParent((string)null));

            var childType = Services.ContentTypeService.GetContentType("child");
            Assert.AreEqual(Constants.System.Root, childType.ParentId);
        }

        [Test]
        public void SortPropertyGroups_ReordersPropertyGroups_WhenCurrentSortOrderDoesNotMatchComparison() {
            RunMigration(m => {
                m.ContentTypes
                    .Add("test", "Test")
                    .AddPropertyGroup("PropertyGroup2")
                    .AddPropertyGroup("PropertyGroup3")
                    .AddPropertyGroup("PropertyGroup1");
            });

            // ReSharper disable once StringCompareIsCultureSpecific.1
            RunMigration(m => m.ContentType("test").SortPropertyGroups((a, b) => string.Compare(a.Name, b.Name)));

            var contentType = Services.ContentTypeService.GetContentType("test");
            CollectionAssert.AreEqual(
                new[] { "PropertyGroup1", "PropertyGroup2", "PropertyGroup3" },
                contentType.PropertyGroups.OrderBy(p => p.SortOrder).Select(p => p.Name).ToArray()
            );
        }

        [Test]
        public void SortPropertyGroups_ReordersPropertyGroups_WhenCurrentSortOrderDoesNotMatchProvided() {
            RunMigration(m => {
                m.ContentTypes
                    .Add("test", "Test")
                    .AddPropertyGroup("PropertyGroup2")
                    .AddPropertyGroup("PropertyGroup3")
                    .AddPropertyGroup("PropertyGroup1");
            });

            // ReSharper disable once StringCompareIsCultureSpecific.1
            RunMigration(m => m.ContentType("test").SortPropertyGroups("PropertyGroup1", "PropertyGroup2", "PropertyGroup3"));

            var contentType = Services.ContentTypeService.GetContentType("test");
            CollectionAssert.AreEqual(
                new[] { "PropertyGroup1", "PropertyGroup2", "PropertyGroup3" },
                contentType.PropertyGroups.OrderBy(p => p.SortOrder).Select(p => p.Name).ToArray()
            );
        }

        [Test]
        public void AddProperty_SetsNameToProvidedValue() {
            RunMigration(m => m.ContentTypes.Add("test", "Test").AddProperty("testProperty", "Test Property", StubDataType(m)));

            var property = Services.ContentTypeService.GetContentType("test").PropertyTypes.First(p => p.Alias == "testProperty");
            Assert.AreEqual("Test Property", property.Name);
        }

        [Test]
        public void MoveProperty_MovesPropertyToDefaultGroup_IfGroupNameIsNull() {
            RunMigration(m => m.ContentTypes.Add("test", "Test").AddProperty("property", "Property", StubDataType(m), propertyGroupName: "OldGroup"));

            RunMigration(m => m.ContentType("test").MoveProperty("property", null));

            var contentType = Services.ContentTypeService.GetContentType("test");
            CollectionAssert.DoesNotContain(contentType.PropertyGroups["OldGroup"].PropertyTypes.Select(p => p.Alias), "property");
            CollectionAssert.Contains(contentType.PropertyTypes.Select(p => p.Alias), "property");
        }

        [Test]
        public void MoveProperty_MovesPropertyToSpecifiedGroup() {
            RunMigration(m => m.ContentTypes.Add("test", "Test").AddProperty("property", "Property", StubDataType(m)));

            RunMigration(m => m.ContentType("test").MoveProperty("property", "NewGroup"));

            var contentType = Services.ContentTypeService.GetContentType("test");
            CollectionAssert.Contains(contentType.PropertyGroups["NewGroup"].PropertyTypes.Select(p => p.Alias), "property");
        }

        [Test]
        public void SortProperties_ReordersProperties_WhenCurrentSortOrderDoesNotMatchComparison() {
            RunMigration(m => {
                var dataType = StubDataType(m);
                m.ContentTypes
                    .Add("test", "Test")
                    .AddProperty("property2", "Property 2", dataType)
                    .AddProperty("property3", "Property 3", dataType)
                    .AddProperty("property1", "Property 1", dataType);
            });

            // ReSharper disable once StringCompareIsCultureSpecific.1
            RunMigration(m => m.ContentType("test").SortProperties(null, (a, b) => string.Compare(a.Name, b.Name)));

            var contentType = Services.ContentTypeService.GetContentType("test");
            CollectionAssert.AreEqual(
                new[] { "property1", "property2", "property3" },
                contentType.PropertyTypes.OrderBy(p => p.SortOrder).Select(p => p.Alias).ToArray()
            );
        }

        [Test]
        public void SortProperties_ReordersProperties_WhenCurrentSortOrderDoesNotMatchProvided() {
            RunMigration(m => {
                var dataType = StubDataType(m);
                m.ContentTypes
                    .Add("test", "Test")
                    .AddProperty("property2", "Property 2", dataType)
                    .AddProperty("property3", "Property 3", dataType)
                    .AddProperty("property1", "Property 1", dataType);
            });

            // ReSharper disable once StringCompareIsCultureSpecific.1
            RunMigration(m => m.ContentType("test").SortProperties(null, "property1", "property2", "property3"));

            var contentType = Services.ContentTypeService.GetContentType("test");
            CollectionAssert.AreEqual(
                new[] { "property1", "property2", "property3" },
                contentType.PropertyTypes.OrderBy(p => p.SortOrder).Select(p => p.Alias).ToArray()
            );
        }

        [Test]
        public void Parent_ReturnsAllParents() {
            RunMigration(m => {
                m.ContentTypes
                    .Add("parent1", "Parent 1").AddChild("child1", "Child 1")
                    .Add("parent2", "Parent 2").AddChild("child2", "Child 2");
            });

            var parents = GetFluent(
                m => m.ContentTypes
                      .Where(t => t.Alias.StartsWith("child"))
                      .Parent().Objects
            );

            CollectionAssert.AreEquivalent(
                new[] { "parent1", "parent2" },
                parents.Select(p => p.Alias)
            );
        }

        [Test]
        public void Parent_ReturnsEmpty_IfContentTypeIsOnTopLevel() {
            RunMigration(m => m.ContentTypes.Add("top", "Top"));
            var parents = GetFluent(m => m.ContentType("top").Parent().Objects);

            CollectionAssert.IsEmpty(parents.Select(p => p.Name));
        }

        [Test]
        public void Delete_RemovesByAlias_WhenCalledOnRootSetWithAlias() {
            RunMigration(m => m.ContentTypes.Add("toDelete", "To Delete"));
            RunMigration(m => m.ContentTypes.Delete("toDelete"));

            var deleted = Services.ContentTypeService.GetContentType("toDelete");
            Assert.IsNull(deleted);
        }

        [Test]
        public void Delete_RemovesAllFiltered_WhenCalledOnFilteredSet() {
            RunMigration(m => m.ContentTypes.Add("toDelete", "To Delete"));
            RunMigration(m => m.ContentTypes.Where(t => t.Alias == "toDelete").Delete());

            var deleted = Services.ContentTypeService.GetContentType("toDelete");
            Assert.IsNull(deleted);
        }

        private static IDataTypeDefinition StubDataType(UmbracoMigrationBase m) {
            return m.DataTypes.Add("Test", Constants.PropertyEditors.TextboxAlias, null).Object;
        }
    }
}
