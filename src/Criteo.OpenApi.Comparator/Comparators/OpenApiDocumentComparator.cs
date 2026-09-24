// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System;
using System.Linq;
using System.Collections.Generic;
using Criteo.OpenApi.Comparator.Comparators.Extensions;
using Criteo.OpenApi.Comparator.Logging;
using System.Net.Http;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators
{
    internal class OpenApiDocumentComparator
    {
        private readonly string _excludeExtensionKey;
        private readonly OperationComparator _operationComparator;
        private readonly SchemaComparator _schemaComparator;
        private readonly ParameterComparator _parameterComparator;
        private readonly ResponseComparator _responseComparator;

        private readonly IDictionary<IOpenApiSchema, bool> _isSchemaReferenced;

        internal OpenApiDocumentComparator(bool trackSchemasReference = true, bool alwaysCompareSchemas = false, string excludeExtensionKey = null)
        {
            _excludeExtensionKey = excludeExtensionKey;
            _schemaComparator = new SchemaComparator(alwaysCompareSchemas, excludeExtensionKey);
            var contentComparator = new ContentComparator(_schemaComparator);
            _parameterComparator = new ParameterComparator(_schemaComparator, contentComparator);
            var requestBodyComparator = new RequestBodyComparator(contentComparator);
            _responseComparator = new ResponseComparator(contentComparator);
            _operationComparator = new OperationComparator(_parameterComparator, requestBodyComparator, _responseComparator);

            if (trackSchemasReference)
                _isSchemaReferenced = new Dictionary<IOpenApiSchema, bool>();
        }

        /// <summary>
        /// Compare a modified document node (this) to a previous one and look for breaking as well as non-breaking changes.
        /// </summary>
        /// <param name="context">The modified document context.</param>
        /// <param name="oldDocument">The original document model.</param>
        /// <param name="newDocument">The new document model.</param>
        /// <returns>A list of messages from the comparison.</returns>
        internal IEnumerable<ComparisonMessage> Compare(
            ComparisonContext context,
            OpenApiDocument oldDocument,
            OpenApiDocument newDocument
        )
        {
            if (context == null)
                throw new ArgumentException(nameof(context));

            if (oldDocument == null)
                throw new ArgumentNullException(nameof(oldDocument));

            if (newDocument == null)
                throw new ArgumentNullException(nameof(newDocument));

            if (context.NewOpenApiDocument != newDocument)
                throw new ArgumentException("context.NewOpenApiDocument != newDocument");

            if (context.OldOpenApiDocument != oldDocument)
                throw new ArgumentException("context.OldOpenApiDocument != oldDocument");

            CompareVersions(context, oldDocument.Info?.Version, newDocument.Info?.Version);

            CompareServers(context, oldDocument.Servers, newDocument.Servers);

            ComparePaths(context, oldDocument.Paths, newDocument.Paths);

            CompareCustomPaths(context, oldDocument, newDocument);

            CompareWebhooks(context, oldDocument, newDocument);

            CompareComponents(context, oldDocument, newDocument);

            context.Pop();

            return context.Messages;
        }

        /// <summary>
        /// Since some services may rely on semantic versioning, comparing versions is fairly complex.
        /// </summary>
        /// <param name="context">A comparison context.</param>
        /// <param name="newVersion">The new version string.</param>
        /// <param name="oldVersion">The old version string</param>
        /// <remarks>
        /// In semantic versioning schemes, only the major and minor version numbers are considered when comparing versions.
        /// Build numbers are ignored.
        /// </remarks>
        private static void CompareVersions(ComparisonContext context, string oldVersion, string newVersion)
        {
            if (newVersion == null || oldVersion == null)
                return;

            context.PushProperty("info");
            context.PushProperty("version");

            var newVersionAsList = newVersion.Split('.');
            var oldVersionAsList = oldVersion.Split('.');

            // If the version consists only of numbers separated by '.', we'll consider it semantic versioning.
            if (!context.Strict && oldVersionAsList.Length > 0 && newVersionAsList.Length > 0)
            {
                var isVersionChanged = false;

                // Situation 1: The versioning scheme is semantic, i.e. it uses a major.minor.build-number scheme, where each component is an integer.
                //              In this case, we care about the major/minor numbers, but not the build number. In other words, if all that is different
                //              is the build number, it will not be treated as a version change.

                var newMajor = 0;
                var areIntegers = int.TryParse(oldVersionAsList[0], out var oldMajor) && int.TryParse(newVersionAsList[0], out newMajor);

                if (areIntegers && oldMajor != newMajor)
                {
                    isVersionChanged = true;

                    if (oldMajor > newMajor)
                    {
                        context.LogError(ComparisonRules.VersionsReversed, oldVersion, newVersion);
                    }
                }

                if (!isVersionChanged && areIntegers && oldVersionAsList.Length > 1 && newVersionAsList.Length > 1)
                {
                    var newMinor = 0;
                    areIntegers = int.TryParse(oldVersionAsList[1], out var oldMinor) && int.TryParse(newVersionAsList[1], out newMinor);

                    if (areIntegers && oldMinor != newMinor)
                    {
                        isVersionChanged = true;

                        if (oldMinor > newMinor)
                        {
                            context.LogError(ComparisonRules.VersionsReversed, oldVersion, newVersion);
                        }
                    }
                }

                // Situation 2: The versioning scheme is something else, maybe a date or just a label?
                //              Regardless of what it is, we just check whether the two strings are equal or not.

                if (!isVersionChanged && !areIntegers)
                {
                    isVersionChanged = !oldVersion.ToLower().Equals(newVersion.ToLower());
                }

                context.Strict = !isVersionChanged;
            }

            if (context.Strict)
            {
                // There was no version change between the documents. This is not an error, but noteworthy.
                context.LogInfo(ComparisonRules.NoVersionChange);
            }

            context.Pop();
            context.Pop();
        }

        private static void CompareServers(ComparisonContext context,
            IList<OpenApiServer> oldServers,
            IList<OpenApiServer> newServers)
        {
            if (oldServers == null && newServers == null)
                return;

            context.PushProperty("servers");
            var oldServerUrls = oldServers == null ? new List<string>() : oldServers.Select(server => server.Url);
            var newServerUrls = newServers == null ? new List<string>() : newServers.Select(server => server.Url);

            foreach (var oldServerUrl in oldServerUrls.Except(newServerUrls))
            {
                context.PushServerByUrl(oldServerUrl);
                context.LogBreakingChange(ComparisonRules.ServerNoLongerSupported, oldServerUrl);
                context.Pop();
            }
            context.Pop();
        }

        /// <summary>
        /// Check that no paths were removed, and compare the paths that are still there.
        /// Check whether any new paths are being added
        /// </summary>
        private void ComparePaths(ComparisonContext context,
            OpenApiPaths oldPaths, OpenApiPaths newPaths,
            string pathsProperty = "paths",
            bool arePathsProperties = false)
        {
            if (oldPaths == null && newPaths == null)
                return;

            oldPaths ??= new OpenApiPaths();
            newPaths ??= new OpenApiPaths();

            OpenApiExcludeExtensions.RemoveExcludedPathsAndOperations(oldPaths, newPaths, _excludeExtensionKey);

            var oldPathsWithVariables = oldPaths.Keys;

            oldPaths = RemovePathVariables(oldPaths);
            newPaths = RemovePathVariables(newPaths);

            var commonPaths = newPaths.Keys.Where(oldPaths.Keys.Contains).ToList();

            context.PushProperty(pathsProperty);
            foreach (var removedPath in oldPaths.Keys.Except(commonPaths))
            {
                context.PushPathProperty(removedPath, arePathsProperties);
                var removedPathWithVariables =
                    oldPathsWithVariables.First(oldPath => ObjectPath.OpenApiPathName(oldPath) == removedPath);
                context.LogBreakingChange(ComparisonRules.RemovedPath, removedPathWithVariables);
                context.Pop();
            }

            foreach (var addedPath in newPaths.Keys.Except(commonPaths))
            {
                context.PushPathProperty(addedPath, arePathsProperties);
                context.LogInfo(ComparisonRules.AddedPath);
                context.Pop();
            }

            foreach (var commonPath in commonPaths)
            {
                var oldPathItem = oldPaths[commonPath];
                var newPathItem = newPaths[commonPath];

                context.PushPathProperty(commonPath, arePathsProperties);
                _operationComparator.CompareParameters(context, oldPathItem.Parameters, newPathItem.Parameters);
                CompareOperations(context, oldPathItem.Operations, newPathItem.Operations);
                context.Pop();
            }
            context.Pop();
        }

        private void CompareOperations(ComparisonContext context,
            IDictionary<HttpMethod, OpenApiOperation> oldOperations,
            IDictionary<HttpMethod, OpenApiOperation> newOperations)
        {
            oldOperations = oldOperations ?? new Dictionary<HttpMethod, OpenApiOperation>();
            newOperations = newOperations ?? new Dictionary<HttpMethod, OpenApiOperation>();

            var commonOperations = newOperations.Keys.Where(oldOperations.Keys.Contains).ToList();

            foreach (var removedOperationName in oldOperations.Keys.Except(commonOperations))
            {
                context.PushProperty(removedOperationName.Method.ToLowerInvariant());
                context.LogBreakingChange(ComparisonRules.RemovedOperation, oldOperations[removedOperationName].OperationId);
                context.Pop();
            }

            foreach (var addedOperationName in newOperations.Keys.Except(commonOperations))
            {
                context.PushProperty(addedOperationName.Method.ToLowerInvariant());
                context.LogInfo(ComparisonRules.AddedOperation);
                context.Pop();
            }

            foreach (var operationName in commonOperations)
            {
                context.PushProperty(operationName.Method.ToLowerInvariant());
                _operationComparator.Compare(context, oldOperations[operationName], newOperations[operationName]);
                context.Pop();
            }
        }

        /// <summary>
        /// Since renaming a path parameter doesn't logically alter the path, we must remove the parameter names
        /// before comparing paths using string comparison.
        /// </summary>
        /// <param name="paths">A dictionary of paths, potentially with embedded parameter names.</param>
        /// <returns>A transformed dictionary, where paths do not embed parameter names.</returns>
        private static OpenApiPaths RemovePathVariables(OpenApiPaths paths)
        {
            var pathsResult = new OpenApiPaths();
            foreach (var path in paths)
            {
                pathsResult[ObjectPath.OpenApiPathName(path.Key)] = path.Value;
            }
            return pathsResult;
        }

        private void CompareCustomPaths(ComparisonContext context, OpenApiDocument oldDocument,
            OpenApiDocument newDocument)
        {
            const string customPathsName = "x-ms-paths";
            IOpenApiExtension oldCustomPathsAsObject = null;
            IOpenApiExtension newCustomPathsAsObject = null;
            oldDocument.Extensions?.TryGetValue(customPathsName, out oldCustomPathsAsObject);
            newDocument.Extensions?.TryGetValue(customPathsName, out newCustomPathsAsObject);

            if (oldCustomPathsAsObject == null || newCustomPathsAsObject == null)
                return;

            const string pointer = "#/" + customPathsName;
            const string invalidFormatMessage =
                "Invalid format for " + customPathsName + " extension. It should have an OpenApi path format. It is ignored.";
            var oldCustomPaths = (oldCustomPathsAsObject as JsonNodeExtension)?.Node as JsonObject;
            var newCustomPaths = (newCustomPathsAsObject as JsonNodeExtension)?.Node as JsonObject;
            if (oldCustomPaths == null)
                context.OldDocumentErrors.Add(new OpenApiError(pointer, invalidFormatMessage));
            if (newCustomPaths == null)
                context.NewDocumentErrors.Add(new OpenApiError(pointer, invalidFormatMessage));
            if (oldCustomPaths == null || newCustomPaths == null)
                return;

            var oldPaths = oldCustomPaths.ToOpenApiPaths(oldDocument, context.OldSpecVersion, pointer, context.OldDocumentErrors);
            var newPaths = newCustomPaths.ToOpenApiPaths(newDocument, context.NewSpecVersion, pointer, context.NewDocumentErrors);

            // A path that is invalid in one of the documents is ignored in both, so that it is not reported as added or removed
            var invalidPaths = oldCustomPaths.Select(path => path.Key).Where(path => !oldPaths.ContainsKey(path))
                .Concat(newCustomPaths.Select(path => path.Key).Where(path => !newPaths.ContainsKey(path)))
                .ToList();
            foreach (var invalidPath in invalidPaths)
            {
                oldPaths.Remove(invalidPath);
                newPaths.Remove(invalidPath);
            }

            ComparePaths(context,
                oldPaths,
                newPaths,
                customPathsName,
                arePathsProperties: true);
        }

        /// <summary>
        /// Webhooks (OpenAPI 3.1) are compared like paths, but the API sends the requests and receives the responses:
        /// the data directions are inverted.
        /// </summary>
        private void CompareWebhooks(ComparisonContext context, OpenApiDocument oldDocument, OpenApiDocument newDocument)
        {
            if (oldDocument.Webhooks == null && newDocument.Webhooks == null)
                return;

            context.InvertDirections = true;
            try
            {
                ComparePaths(context, ToOpenApiPaths(oldDocument.Webhooks), ToOpenApiPaths(newDocument.Webhooks),
                    "webhooks", arePathsProperties: true);
            }
            finally
            {
                context.InvertDirections = false;
            }
        }

        private static OpenApiPaths ToOpenApiPaths(IDictionary<string, IOpenApiPathItem> pathItems)
        {
            var paths = new OpenApiPaths();
            foreach (var pathItem in pathItems ?? new Dictionary<string, IOpenApiPathItem>())
                paths.Add(pathItem.Key, pathItem.Value);
            return paths;
        }

        private void CompareComponents(ComparisonContext context,
            OpenApiDocument oldDocument,
            OpenApiDocument newDocument)
        {
            if (oldDocument.Components == null && newDocument.Components == null)
                return;

            InitComponents(oldDocument);
            InitComponents(newDocument);

            if (_isSchemaReferenced != null) 
            {
                TrackSchemasReference(oldDocument);
                TrackSchemasReference(newDocument);
            }

            context.PushProperty("components");

            CompareSchemas(context, oldDocument.Components.Schemas, newDocument.Components.Schemas);

            CompareParameters(context, oldDocument.Components.Parameters, newDocument.Components.Parameters);

            CompareResponses(context, oldDocument.Components.Responses, newDocument.Components.Responses);
        }

        /// <summary>
        /// Components sections are null when they are not defined in the document.
        /// </summary>
        private static void InitComponents(OpenApiDocument document)
        {
            document.Components ??= new OpenApiComponents();
            document.Components.Schemas ??= new Dictionary<string, IOpenApiSchema>();
            document.Components.Parameters ??= new Dictionary<string, IOpenApiParameter>();
            document.Components.Responses ??= new Dictionary<string, IOpenApiResponse>();
            document.Components.RequestBodies ??= new Dictionary<string, IOpenApiRequestBody>();
        }

        /// <summary>
        /// In order to avoid comparing schemas that are not used, we go through all references that are
        /// found in operations, global parameters, and global responses.
        /// Schemas that are referenced from other schemas are included only by transitive closure.
        /// </summary>
        private void TrackSchemasReference(OpenApiDocument document)
         {
             if (document.Components.Schemas == null)
                 return;

             InitSchemasReference(document);

             TrackSchemasReferenceInPaths(document);

             TrackSchemasReferenceInComponents(document);
         }

         private void InitSchemasReference(OpenApiDocument document)
         {
             foreach (var schema in document.Components.Schemas.Values)
             {
                 if (!_isSchemaReferenced.ContainsKey(schema))
                 {
                     _isSchemaReferenced[schema] = false;
                 }
             }
         }

         private void TrackSchemasReferenceInPaths(OpenApiDocument document)
         {
             var pathItems = (document.Paths?.Values ?? Enumerable.Empty<IOpenApiPathItem>())
                 .Concat(document.Webhooks?.Values ?? Enumerable.Empty<IOpenApiPathItem>());
             foreach (var pathItem in pathItems)
             {
                 if (pathItem.Operations == null)
                     continue;

                 foreach (var operation in pathItem.Operations.Values)
                 {
                     TrackSchemasReferenceInParameters(operation.Parameters, document.Components.Schemas);

                     TrackSchemasReferenceInRequestBody(operation.RequestBody, document.Components.Schemas);

                     TrackSchemasReferenceInResponses(operation.Responses?.Values, document.Components.Schemas);
                 }
             }
         }

         private void TrackSchemasReferenceInComponents(OpenApiDocument document)
         {
             TrackSchemasReferenceInParameters(document.Components.Parameters.Values, document.Components.Schemas);

             foreach (var requestBody in document.Components.RequestBodies.Values)
             {
                 TrackSchemasReferenceInRequestBody(requestBody, document.Components.Schemas);
             }

             TrackSchemasReferenceInResponses(document.Components.Responses.Values, document.Components.Schemas);

             DetectPolymorphicSchemas(document.Components.Schemas);
         }

         private void TrackSchemasReferenceInParameters(IEnumerable<IOpenApiParameter> parameters,
             IDictionary<string, IOpenApiSchema> commonSchemas)
         {
             if (parameters == null)
                 return;

             foreach (var parameter in parameters)
             {
                 var parameterSchema = parameter.Schema?.Unwrap();
                 if (parameterSchema != null && parameterSchema.IsReference())
                 {
                     var schema = parameterSchema.GetReference().Resolve(commonSchemas);
                     _isSchemaReferenced[schema ?? parameterSchema] = true;
                 }
             }
         }

         private void TrackSchemasReferenceInRequestBody(IOpenApiRequestBody requestBody,
             IDictionary<string, IOpenApiSchema> commonSchemas)
         {
             if (requestBody?.Content == null)
                 return;

             foreach (var requestBodyType in requestBody.Content.Values)
             {
                 var requestBodySchema = requestBodyType.Schema?.Unwrap();
                 if (requestBodySchema != null && requestBodySchema.IsReference())
                 {
                     var schema = requestBodySchema.GetReference().Resolve(commonSchemas);
                     _isSchemaReferenced[schema ?? requestBodySchema] = true;
                 }
             }
         }

         private void TrackSchemasReferenceInResponses(IEnumerable<IOpenApiResponse> responses,
             IDictionary<string, IOpenApiSchema> commonSchemas)
         {
             if (responses == null)
                 return;

             foreach (var responseStatus in responses)
             {
                 foreach (var responseMediaType in responseStatus.Content?.Values ?? Enumerable.Empty<OpenApiMediaType>())
                 {
                     var responseSchema = responseMediaType?.Schema?.Unwrap();
                     if (responseSchema != null && responseSchema.IsReference())
                     {
                         var schema = responseSchema.GetReference().Resolve(commonSchemas);
                         _isSchemaReferenced[schema ?? responseSchema] = true;
                     }
                 }
             }
         }

         /// <summary>
         /// If a schema has an allOf property which has a discriminator,
         /// then the schema is considered has referenced after all.
         /// </summary>
         private void DetectPolymorphicSchemas(IDictionary<string, IOpenApiSchema> schemas)
         {
             var unReferencedSchemas = schemas.Values
                 .Where(schema => !_isSchemaReferenced[schema]);
             foreach (var schema in unReferencedSchemas)
             {
                 if (schema.Extensions != null && schema.Extensions.ContainsKey("x-ms-discriminator-value"))
                 {
                     _isSchemaReferenced[schema] = true;
                     continue;
                 }

                 if (schema.AllOf == null)
                     continue;

                 var referencedAllOffProperties = schema.AllOf.Where(property =>
                     property.IsReference());
                 foreach (var property in referencedAllOffProperties)
                 {
                     _isSchemaReferenced[schema] = FindDiscriminator(property.GetReference(), schemas);

                     if (_isSchemaReferenced[schema])
                         break;
                 }
             }
         }

         /// <summary>
         /// Detect if a schema's hierarchy contains a discriminator.
         /// </summary>
         /// <param name="reference">A document-relative reference object</param>
         /// <param name="schemas">The schemas dictionary to use</param>
         /// <returns></returns>
         private static bool FindDiscriminator(BaseOpenApiReference reference, IDictionary<string, IOpenApiSchema> schemas)
         {
             var schema = reference.Resolve(schemas);
             if (schema?.Discriminator != null)
                 return true;

             if (schema?.AllOf == null)
                 return false;

             return schema.AllOf.Any(subSchema =>
                 subSchema.GetReference() != null && FindDiscriminator(subSchema.GetReference(), schemas));
         }

         private void CompareSchemas(ComparisonContext context,
             IDictionary<string, IOpenApiSchema> oldSchemas,
             IDictionary<string, IOpenApiSchema> newSchemas)
         {
             context.PushProperty("schemas");
             foreach (var oldSchema in oldSchemas)
             {
                 context.PushProperty(oldSchema.Key);
                 if (!newSchemas.TryGetValue(oldSchema.Key, out var newSchema))
                 {
                     if (_isSchemaReferenced == null || !_isSchemaReferenced[oldSchema.Value])
                     {
                         // It's only an error if the schema is referenced in the old service.
                         context.LogBreakingChange(ComparisonRules.RemovedDefinition, oldSchema.Key);
                     }
                 }
                 else
                 {
                     _schemaComparator.Compare(context, oldSchema.Value, newSchema, _isSchemaReferenced != null ? _isSchemaReferenced[oldSchema.Value] : true);
                 }
                 context.Pop();
             }
             context.Pop();
         }

         private void CompareParameters(ComparisonContext context,
             IDictionary<string, IOpenApiParameter> oldParameters,
             IDictionary<string, IOpenApiParameter> newParameters)
         {
             context.PushProperty("parameters");
             foreach (var oldParameterName in oldParameters.Keys)
             {
                 context.PushProperty(oldParameterName);
                 if (!newParameters.TryGetValue(oldParameterName, out var newParameter))
                 {
                     context.LogBreakingChange(ComparisonRules.RemovedClientParameter, oldParameterName);
                 }
                 else
                 {
                     _parameterComparator.Compare(context, oldParameters[oldParameterName], newParameter);
                 }
                 context.Pop();
             }
             context.Pop();
         }

         private void CompareResponses(ComparisonContext context,
             IDictionary<string, IOpenApiResponse> oldResponses,
             IDictionary<string, IOpenApiResponse> newResponses)
         {
             context.PushProperty("responses");
             foreach (var oldDefinition in oldResponses.Keys)
             {
                 if (!newResponses.TryGetValue(oldDefinition, out var response))
                 {
                     context.LogBreakingChange(ComparisonRules.RemovedDefinition, oldDefinition);
                 }
                 else
                 {
                     context.PushProperty(oldDefinition);
                     _responseComparator.Compare(context, oldResponses[oldDefinition], response);
                     context.Pop();
                 }
             }
             context.Pop();
         }
    }
}
