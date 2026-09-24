// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;

namespace Criteo.OpenApi.Comparator.Comparators.Extensions
{
    internal static class OpenApiPathsExtensions
    {
        /// <summary>
        /// Converts a raw paths object (e.g. the content of the x-ms-paths extension) into OpenApiPaths.
        /// Invalid path items are ignored and reported in <paramref name="errors"/>.
        /// </summary>
        /// <param name="rawPaths">The raw paths object</param>
        /// <param name="hostDocument">The document hosting the paths, used to resolve references</param>
        /// <param name="specVersion">The version of the host document</param>
        /// <param name="pointer">The JSON pointer of the raw paths object, used in errors</param>
        /// <param name="errors">The list where errors are added</param>
        internal static OpenApiPaths ToOpenApiPaths(this JsonObject rawPaths, OpenApiDocument hostDocument,
            OpenApiSpecVersion specVersion, string pointer, IList<OpenApiError> errors)
        {
            var paths = new OpenApiPaths();

            foreach (var path in rawPaths)
            {
                var pathPointer = $"{pointer}/{path.Key.Replace("~", "~0").Replace("/", "~1")}";

                if (!(path.Value is JsonObject rawPathItem))
                {
                    errors.Add(new OpenApiError(pathPointer, "Invalid path item: it should be an object. It is ignored."));
                    continue;
                }

                OpenApiPathItem pathItem;
                OpenApiDiagnostic diagnostic;
                try
                {
                    pathItem = OpenApiModelFactory.Parse<OpenApiPathItem>(
                        rawPathItem.ToJsonString(), specVersion, hostDocument, out diagnostic, OpenApiConstants.Json);
                }
                catch (Exception exception) when (!(exception is OutOfMemoryException))
                {
                    // Any reader failure only invalidates this path item
                    errors.Add(new OpenApiError(pathPointer, $"Invalid path item: {exception.Message} It is ignored."));
                    continue;
                }

                if (pathItem == null || diagnostic?.Errors.Any() == true)
                {
                    var details = string.Join("; ", diagnostic?.Errors ?? Enumerable.Empty<OpenApiError>());
                    errors.Add(new OpenApiError(pathPointer, $"Invalid path item: {details} It is ignored."));
                    continue;
                }

                paths.Add(path.Key, pathItem);
            }
            return paths;
        }
    }
}
