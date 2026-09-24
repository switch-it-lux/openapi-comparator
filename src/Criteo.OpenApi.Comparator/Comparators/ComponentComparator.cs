// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System;
using Criteo.OpenApi.Comparator.Comparators.Extensions;
using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators
{
    internal static class ComponentComparator<T> where T : IOpenApiReferenceable
    {
        internal static void Compare(ComparisonContext context, T oldComponent, T newComponent)
        {
            if (oldComponent == null)
                throw new ArgumentNullException(nameof(oldComponent));

            if (newComponent == null)
                throw new ArgumentNullException(nameof(newComponent));

            CompareReference(context, oldComponent.GetReferenceV3(), newComponent.GetReferenceV3());
        }

        private static void CompareReference(ComparisonContext context,
            string oldReference,
            string newReference)
        {
            if (newReference != null && !newReference.Equals(oldReference))
            {
                context.LogBreakingChange(ComparisonRules.ReferenceRedirection);
            }
        }
    }
}
