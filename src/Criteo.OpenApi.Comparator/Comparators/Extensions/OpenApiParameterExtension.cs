// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators.Extensions
{
    internal static class OpenApiParameterExtension
    {
        internal static bool IsConstant(this IOpenApiParameter parameter) =>
            parameter.IsRequired() && parameter.HasEnumWithSingleValue();

        internal static bool IsRequired(this IOpenApiParameter parameter) =>
            parameter.Required || parameter.In == ParameterLocation.Path;

        // Only inline schemas are considered: enum changes of referenced schemas are reported by the schema comparison.
        private static bool HasEnumWithSingleValue(this IOpenApiParameter parameter) =>
            parameter.Schema != null && !parameter.Schema.IsReference() && parameter.Schema.EnumValues() is { Count: 1 };
    }
}
