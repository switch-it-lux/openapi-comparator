// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using Microsoft.OpenApi;
using Newtonsoft.Json.Linq;

namespace Criteo.OpenApi.Comparator.Parser
{
    internal interface IJsonDocument
    {
        /// JSON object
        JToken Token { get; }
    }

    internal sealed class JsonDocument<T> : IJsonDocument
    {
        /// <summary>
        /// Untyped raw parsed JSON. The Token also includes information about
        /// file location of each item.
        /// </summary>
        public JToken Token { get; }

        /// <summary>
        /// Deserialized JSON of Type T
        /// </summary>
        public T Typed { get; }

        /// <summary>
        /// OpenAPI specification version of the source document
        /// </summary>
        public OpenApiSpecVersion SpecVersion { get; }

        public JsonDocument(JToken token, T typed, OpenApiSpecVersion specVersion = OpenApiSpecVersion.OpenApi3_0)
        {
            Token = token;
            Typed = typed;
            SpecVersion = specVersion;
        }
    }

    internal static class JsonDocument
    {
        /// <summary>
        /// Creates a `JsonDocument` object. It's a syntax sugar for `new JsonDocument`.
        /// </summary>
        public static JsonDocument<T> ToJsonDocument<T>(this JToken token, T typed,
            OpenApiSpecVersion specVersion = OpenApiSpecVersion.OpenApi3_0) =>
            new JsonDocument<T>(token, typed, specVersion);
    }
}
