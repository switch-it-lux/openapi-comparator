// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Criteo.OpenApi.Comparator.Comparators;
using Criteo.OpenApi.Comparator.Parser;
using Microsoft.OpenApi.Models;

namespace Criteo.OpenApi.Comparator
{
    /// <summary>
    /// OpenAPI Comparator base class
    /// </summary>
    public static class OpenApiComparator
    {
        /// <summary>
        /// Compares two OpenAPI specification.
        /// </summary>
        /// <param name="oldOpenApiSpec">The content of the old OpenAPI Specification</param>
        /// <param name="newOpenApiSpec">The content of the new OpenAPI Specification</param>
        /// <param name="parsingErrors">Parsing errors</param>
        /// <param name="strict">If true, then breaking changes are errors instead of warnings.</param>
        /// <param name="trackSchemasReference">If true, then schemas that are not used are not compared.</param>
        /// <param name="alwaysCompareSchemas">If true, schemas are always compared (even if it is compared in the context of a path response/request).</param>
        /// <param name="excludeExtensionKey">The name of a custom OpenAPI extension (e.g., x-exclude-from-api-diff) used to mark elements that should be excluded from the API comparison.</param>
        public static IEnumerable<ComparisonMessage> Compare(
            string oldOpenApiSpec,
            string newOpenApiSpec,
            out IEnumerable<ParsingError> parsingErrors,
            bool strict = false,
            bool trackSchemasReference = true, 
            bool alwaysCompareSchemas = false,
            string excludeExtensionKey = null)
        {
            var oldOpenApiDocument = OpenApiParser.Parse(oldOpenApiSpec, out var oldSpecDiagnostic);
            var newOpenApiDocument = OpenApiParser.Parse(newOpenApiSpec, out var newSpecDiagnostic);

            parsingErrors = oldSpecDiagnostic.Errors
                .Select(e => new ParsingError("old", e))
                .Concat(newSpecDiagnostic.Errors.Select(e => new ParsingError("new", e)));

            var context = new ComparisonContext(oldOpenApiDocument, newOpenApiDocument) { Strict = strict };

            var comparator = new OpenApiDocumentComparator(trackSchemasReference, alwaysCompareSchemas, excludeExtensionKey);
            var comparisonMessages = comparator.Compare(context, oldOpenApiDocument.Typed, newOpenApiDocument.Typed);

            return comparisonMessages;
        }
    }

    /// <summary>
    /// Represents an error that occurred while parsing an OpenAPI document.
    /// </summary>
    public class ParsingError
    {
        private readonly string _documentName;
        private readonly OpenApiError _error;

        /// <summary>
        /// Initializes a new instance of the <see cref="ParsingError"/> class.
        /// </summary>
        /// <param name="documentName"></param>
        /// <param name="error"></param>
        public ParsingError(string documentName, OpenApiError error)
        {
            _documentName = documentName;
            _error = error;
        }

        /// <inheritdoc/>
        public override string ToString() => $"[{_documentName}] {_error}";
    }
}
