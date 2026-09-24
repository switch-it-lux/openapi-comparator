// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators.Extensions
{
    internal static class OpenApiReferenceExtensions
    {
        /// <summary>
        /// Get the reference of an element if it is a reference ($ref) to another element, null otherwise.
        /// </summary>
        internal static BaseOpenApiReference GetReference(this IOpenApiReferenceable element) =>
            element switch
            {
                OpenApiSchemaReference schemaReference => schemaReference.Reference,
                OpenApiParameterReference parameterReference => parameterReference.Reference,
                OpenApiResponseReference responseReference => responseReference.Reference,
                OpenApiRequestBodyReference requestBodyReference => requestBodyReference.Reference,
                OpenApiHeaderReference headerReference => headerReference.Reference,
                _ => null,
            };

        /// <summary>
        /// Get the reference path (e.g. #/components/schemas/XXX) of an element if it is a reference, null otherwise.
        /// </summary>
        internal static string GetReferenceV3(this IOpenApiReferenceable element) =>
            element.GetReference()?.ReferenceV3;

        /// <summary>
        /// Indicate if the element is a reference ($ref) to another element.
        /// </summary>
        internal static bool IsReference(this IOpenApiReferenceable element) =>
            !string.IsNullOrWhiteSpace(element.GetReferenceV3());

        /// <summary>
        /// Retrieve a parameter from the components/parameters section.
        /// </summary>
        /// <param name="reference">A document-relative reference object -- #/components/parameters/XXX</param>
        /// <param name="parameters">The parameters dictionary to use</param>
        internal static IOpenApiParameter Resolve(this BaseOpenApiReference reference,
            IDictionary<string, IOpenApiParameter> parameters) =>
            reference.Resolve(parameters, "parameters");

        /// <summary>
        /// Retrieve a schema from the components/schemas section.
        /// </summary>
        /// <param name="reference">A document-relative reference object -- #/components/schemas/XXX</param>
        /// <param name="schemas">The schemas dictionary to use</param>
        internal static IOpenApiSchema Resolve(this BaseOpenApiReference reference, IDictionary<string, IOpenApiSchema> schemas) =>
            reference.Resolve(schemas, "schemas");

        /// <summary>
        /// Retrieve a response from the components/responses section.
        /// </summary>
        /// <param name="reference">A document-relative reference object -- #/components/responses/XXX</param>
        /// <param name="responses">The responses dictionary to use</param>
        internal static IOpenApiResponse Resolve(this BaseOpenApiReference reference, IDictionary<string, IOpenApiResponse> responses) =>
            reference.Resolve(responses, "responses");

        /// <summary>
        /// Retrieve a requestBody from the components/requestBodies section.
        /// </summary>
        /// <param name="reference">A document-relative reference object -- #/components/requestBodies/XXX</param>
        /// <param name="requestBodies">The requestBodies dictionary to use</param>
        internal static IOpenApiRequestBody Resolve(this BaseOpenApiReference reference, IDictionary<string, IOpenApiRequestBody> requestBodies) =>
            reference.Resolve(requestBodies, "requestBodies");

        private static T Resolve<T>(this BaseOpenApiReference reference, IDictionary<string, T> components, string componentType)
            where T : class
        {
            if (reference == null || components == null || !reference.IsLocal || reference.ReferenceV3 == null)
                return null;

            if (!reference.IsPathTo(componentType))
                return null;

            return components.TryGetValue(reference.GetLastPathElement(), out var component) ? component : null;
        }

        private static bool IsPathTo(this BaseOpenApiReference reference, string componentType)
        {
            var parts = reference.ReferenceV3.Split('/');
            return parts.Length == 4 && parts[1].Equals("components") && parts[2].Equals(componentType);
        }

        private static string GetLastPathElement(this BaseOpenApiReference reference) =>
            reference.ReferenceV3.Split('/').Last();
    }
}
