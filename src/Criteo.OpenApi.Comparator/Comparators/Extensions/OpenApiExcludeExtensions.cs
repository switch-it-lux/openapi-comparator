// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators.Extensions
{
    /// <summary>
    /// Extensions for excluding schemas, path and operations.
    /// </summary>
    public static class OpenApiExcludeExtensions
    {
        /// <summary>
        /// Indicate if the schema is marked with the exclude extension property.
        /// </summary>
        public static bool ShouldExcludeSchema(this IOpenApiSchema schema, string excludeExtensionKey)
        {
            if (string.IsNullOrEmpty(excludeExtensionKey) || schema == null)
                return false;

            if (schema.Extensions.ShouldExclude(excludeExtensionKey))
                return true;

            if (schema.AllOf != null && schema.AllOf.Any(s => s.Extensions.ShouldExclude(excludeExtensionKey)))
                return true;

            return false;
        }

        /// <summary>
        /// Indicate if the path is marked with the exclude extension property.
        /// </summary>
        public static bool ShouldExcludePath(this IOpenApiPathItem path, string excludeExtensionKey)
        {
            if (string.IsNullOrEmpty(excludeExtensionKey) || path == null) return false;

            if (path.Extensions.ShouldExclude(excludeExtensionKey))
                return true;

            if (path.Operations != null && path.Operations.All(x => x.Value.ShouldExcludeOperation(excludeExtensionKey)))
                return true;

            return false;
        }

        /// <summary>
        /// Indicate if the operation is marked with the exclude extension property.
        /// </summary>
        public static bool ShouldExcludeOperation(this OpenApiOperation operation, string excludeExtensionKey)
        {
            if (string.IsNullOrEmpty(excludeExtensionKey) || operation == null) return false;
            return operation.Extensions.ShouldExclude(excludeExtensionKey);
        }

        internal static void RemoveExcludedPathsAndOperations(OpenApiPaths oldPaths, OpenApiPaths newPaths, string excludeExtensionKey)
        {
            if (string.IsNullOrEmpty(excludeExtensionKey) || oldPaths == null || newPaths == null) return;

            foreach (var path in newPaths.Union(oldPaths).ToArray())
            {
                if (path.Value.ShouldExcludePath(excludeExtensionKey))
                {
                    newPaths.Remove(path.Key);
                    oldPaths.Remove(path.Key);
                }
                else
                {
                    foreach (var operation in path.Value.Operations?.ToArray() ?? new KeyValuePair<HttpMethod, OpenApiOperation>[0])
                        if (operation.Value.ShouldExcludeOperation(excludeExtensionKey))
                        {
                            if (newPaths.ContainsKey(path.Key))
                                newPaths[path.Key].Operations?.Remove(operation.Key);
                            if (oldPaths.ContainsKey(path.Key))
                                oldPaths[path.Key].Operations?.Remove(operation.Key);
                        }

                    if (newPaths.ContainsKey(path.Key) && newPaths[path.Key].Operations?.Count is null or 0)
                        newPaths.Remove(path.Key);
                    if (oldPaths.ContainsKey(path.Key) && oldPaths[path.Key].Operations?.Count is null or 0)
                        oldPaths.Remove(path.Key);
                }
            }
        }

        private static bool ShouldExclude(this IEnumerable<KeyValuePair<string, IOpenApiExtension>> extensions, string excludeExtensionKey)
        {
            if (string.IsNullOrEmpty(excludeExtensionKey))
                return false;

            if (extensions != null && extensions.Any(x => x.Key == excludeExtensionKey && !(x.Value is JsonNodeExtension { Node: { } node } && node.GetValueKind() == JsonValueKind.False)))
                return true;

            return false;
        }
    }
}
