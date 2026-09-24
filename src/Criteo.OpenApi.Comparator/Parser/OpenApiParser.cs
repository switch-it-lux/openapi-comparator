// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Criteo.OpenApi.Comparator.Comparators.Extensions;
using Microsoft.OpenApi;
using Microsoft.OpenApi.Reader;
using Microsoft.OpenApi.YamlReader;
using Newtonsoft.Json.Linq;
using SharpYaml.Serialization;

namespace Criteo.OpenApi.Comparator.Parser
{
    /// <summary>
    /// Converts a swagger into a C# object
    /// </summary>
    internal static class OpenApiParser
    {
        /// <summary>
        /// Keywords whose values are data, not schemas: they must not be altered.
        /// </summary>
        private static readonly string[] _dataKeywords = { "example", "examples", "default", "enum", "const" };

        /// <summary>
        /// Keywords whose values are maps of named elements (e.g. property names, status codes): their keys are not keywords.
        /// </summary>
        private static readonly string[] _nameMapKeywords =
        {
            "paths", "webhooks", "properties", "patternProperties", "responses", "schemas", "parameters", "requestBodies",
            "headers", "securitySchemes", "links", "callbacks", "pathItems", "content", "encoding", "definitions", "$defs",
            CustomPathsExtension,
        };

        /// <summary>
        /// The only extension walked through: it contains path items, compared like the paths (see ToOpenApiPaths).
        /// Other extensions (x-*) are data.
        /// </summary>
        private const string CustomPathsExtension = "x-ms-paths";

        /// <param name="openApiDocumentAsString">Swagger as string</param>
        /// <param name="diagnostic"></param>
        internal static JsonDocument<OpenApiDocument> Parse(string openApiDocumentAsString, out OpenApiDiagnostic diagnostic)
        {
            var openApiReaderSettings = new OpenApiReaderSettings();
            openApiReaderSettings.AddYamlReader();

            // The document is first read as is, so that the reader limits (size, depth, alias expansion...)
            // are enforced before any other processing.
            var readResult = OpenApiDocument.Parse(openApiDocumentAsString, settings: openApiReaderSettings);
            diagnostic = readResult.Diagnostic;

            if (readResult.Document == null)
            {
                var errors = string.Join("; ", diagnostic?.Errors.Select(e => e.ToString()) ?? Enumerable.Empty<string>());
                throw new OpenApiReaderException($"Unable to parse the OpenAPI document: {errors}");
            }

            // The document is only parsed again when it may contain the keyword (cheap check): most documents do not
            // need it, and parsing a large document several times is costly.
            if (diagnostic.SpecificationVersion == OpenApiSpecVersion.OpenApi3_0
                && openApiDocumentAsString.Contains("nullable")
                && TryPreserveNullableWithoutType(openApiDocumentAsString, out var preservedDocument))
            {
                var preservedReadResult = OpenApiDocument.Parse(preservedDocument, OpenApiConstants.Json, openApiReaderSettings);
                if (preservedReadResult.Document != null)
                {
                    readResult = preservedReadResult;
                    diagnostic = readResult.Diagnostic;
                }
            }

            var openApiDocument = readResult.Document;

            // The JSON representation is used to compute the location (JSON pointer) of each message.
            // It is written in the same version as the source document so that pointers match it.
            var specVersion = diagnostic.SpecificationVersion == OpenApiSpecVersion.OpenApi3_1
                ? OpenApiSpecVersion.OpenApi3_1
                : OpenApiSpecVersion.OpenApi3_0;

            var textWriter = new StringWriter(CultureInfo.InvariantCulture);
            var openApiWriter = new OpenApiJsonWriter(textWriter);
            openApiDocument.SerializeAs(specVersion, openApiWriter);
            var openApiDocumentAsJson = textWriter.ToString();
            var parsedJson = JToken.Parse(openApiDocumentAsJson);

            return parsedJson.ToJsonDocument(openApiDocument, specVersion);
        }

        /// <summary>
        /// The OpenAPI reader drops the OpenAPI 3.0 "nullable: true" keyword when the schema has no type
        /// (e.g. "nullable: true" + "allOf: [$ref]"). It is kept as an extension so that it can still be compared.
        /// It must only be called on a document already read by the OpenAPI reader (which enforces its limits).
        /// Returns false if the document cannot be converted here: it is then used as is.
        /// </summary>
        private static bool TryPreserveNullableWithoutType(string openApiDocumentAsString, out string preservedDocument)
        {
            preservedDocument = null;
            JsonNode document;
            try
            {
                document = ToJsonNode(openApiDocumentAsString);
            }
            catch (Exception)
            {
                return false;
            }

            if (document == null || !AddNullableExtension(document, isNameMap: false))
                return false;

            preservedDocument = document.ToJsonString();
            return true;
        }

        private static JsonNode ToJsonNode(string openApiDocumentAsString)
        {
            try
            {
                return JsonNode.Parse(openApiDocumentAsString);
            }
            catch (JsonException)
            {
                var yamlStream = new YamlStream();
                yamlStream.Load(new StringReader(openApiDocumentAsString));
                return yamlStream.Documents.First().ToJsonNode();
            }
        }

        /// <summary>
        /// Adds the nullable extension to the schemas with "nullable: true" and no type.
        /// Returns true if at least one extension was added.
        /// </summary>
        /// <param name="node">The current node</param>
        /// <param name="isNameMap">Whether the node is a map whose keys are names (e.g. properties, responses),
        /// in which case no key is a keyword</param>
        private static bool AddNullableExtension(JsonNode node, bool isNameMap)
        {
            var isAdded = false;
            switch (node)
            {
                case JsonObject jsonObject:
                    if (!isNameMap
                        && jsonObject.TryGetPropertyValue("nullable", out var nullable)
                        && nullable?.GetValueKind() == JsonValueKind.True
                        && !jsonObject.ContainsKey("type")
                        && !jsonObject.ContainsKey("$ref"))
                    {
                        jsonObject[OpenApiSchemaExtensions.NullableExtension] = true;
                        isAdded = true;
                    }

                    foreach (var property in jsonObject.ToList())
                    {
                        if (isNameMap)
                        {
                            isAdded |= AddNullableExtension(property.Value, isNameMap: false);
                        }
                        else if (!_dataKeywords.Contains(property.Key)
                                 && (!property.Key.StartsWith("x-") || property.Key == CustomPathsExtension))
                        {
                            isAdded |= AddNullableExtension(property.Value, _nameMapKeywords.Contains(property.Key));
                        }
                    }
                    break;
                case JsonArray jsonArray:
                    foreach (var item in jsonArray)
                        isAdded |= AddNullableExtension(item, isNameMap: false);
                    break;
            }
            return isAdded;
        }
    }
}
