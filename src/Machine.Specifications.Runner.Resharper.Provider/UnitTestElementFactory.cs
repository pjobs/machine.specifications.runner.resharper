﻿using System;
using System.Collections.Generic;
using System.Linq;
using JetBrains.Metadata.Reader.API;
using JetBrains.ProjectModel;
using JetBrains.ReSharper.UnitTestFramework;
using JetBrains.Util;
using JetBrains.Util.Dotnet.TargetFrameworkIds;
using Machine.Specifications.ReSharperProvider.Elements;

namespace Machine.Specifications.ReSharperProvider
{
    public class UnitTestElementFactory
    {
        private readonly MspecServiceProvider _serviceProvider;
        private readonly TargetFrameworkId _targetFrameworkId;
        private readonly Action<IUnitTestElement> _elementChangedAction;

        private readonly Dictionary<UnitTestElementId, IUnitTestElement> _elements = new Dictionary<UnitTestElementId, IUnitTestElement>();

        public UnitTestElementFactory(MspecServiceProvider serviceProvider, TargetFrameworkId targetFrameworkId, Action<IUnitTestElement> elementChangedAction = null)
        {
            _serviceProvider = serviceProvider;
            _targetFrameworkId = targetFrameworkId;
            _elementChangedAction = elementChangedAction;
        }

        public IUnitTestElement GetOrCreateContext(
            IProject project,
            IClrTypeName typeName,
            FileSystemPath assemblyLocation,
            string subject,
            string[] tags,
            bool ignored,
            UnitTestElementCategorySource categorySource,
            out bool tagsChanged)
        {
            lock (_elements)
            {
                var element = GetOrCreateElement(typeName.FullName, project, null, null, x =>
                    new ContextElement(x, typeName, _serviceProvider, subject, ignored));

                tagsChanged = UpdateCategories(element, tags, categorySource);

                element.AssemblyLocation = assemblyLocation;

                return element;
            }
        }

        public IUnitTestElement GetOrCreateBehavior(
            IProject project,
            IUnitTestElement parent,
            IClrTypeName typeName,
            string fieldName,
            bool ignored)
        {
            lock (_elements)
            {
                var id = $"{typeName.FullName}::{fieldName}";

                return GetOrCreateElement(id, project, parent, parent.OwnCategories, x =>
                    new BehaviorElement(x, parent, typeName, _serviceProvider, fieldName, ignored));
            }
        }

        public IUnitTestElement GetOrCreateContextSpecification(
            IProject project,
            IUnitTestElement parent,
            IClrTypeName typeName,
            string fieldName,
            bool ignored)
        {
            lock (_elements)
            {
                var id = $"{typeName.FullName}::{fieldName}";

                return GetOrCreateElement(id, project, parent, parent.OwnCategories, x =>
                    new ContextSpecificationElement(x, parent, typeName, _serviceProvider, fieldName, ignored || parent.Explicit));
            }
        }

        public IUnitTestElement GetOrCreateBehaviorSpecification(
            IProject project,
            IUnitTestElement parent,
            IClrTypeName typeName,
            string fieldName,
            bool isIgnored)
        {
            lock (_elements)
            {
                var id = $"{typeName.FullName}::{fieldName}";

                return GetOrCreateElement(id, project, parent, parent.OwnCategories, x =>
                    new BehaviorSpecificationElement(x, parent, typeName, _serviceProvider, fieldName, isIgnored || parent.Explicit));
            }
        }

        private T GetElementById<T>(UnitTestElementId id)
            where T : Element
        {
            if (_elements.TryGetValue(id, out var element))
                return element as T;

            return _serviceProvider.ElementManager.GetElementById(id) as T;
        }

        private T GetOrCreateElement<T>(string id, IProject project, IUnitTestElement parent, ISet<UnitTestElementCategory> categories, Func<UnitTestElementId, T> factory)
            where T : Element
        {
            var elementId = _serviceProvider.CreateId(project, _targetFrameworkId, id);

            var element = GetElementById<T>(elementId) ?? factory(elementId);

            var invalidChildren = element.Children.Where(x => x.State == UnitTestElementState.Invalid);
            _serviceProvider.ElementManager.RemoveElements(invalidChildren.ToSet());

            element.Parent = parent;
            element.OwnCategories = categories;

            _elements[elementId] = element;

            return element;
        }

        private bool UpdateCategories(Element element, string[] categories, UnitTestElementCategorySource categorySource)
        {
            using (UT.WriteLock())
            {
                var result = element.UpdateOwnCategoriesFrom(categories, categorySource);

                if (result)
                    _elementChangedAction?.Invoke(element);

                return result;
            }
        }
    }
}
