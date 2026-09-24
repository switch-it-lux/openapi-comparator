// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators.Extensions
{
    internal static class OpenApiSchemaExtensions
    {
        /// <summary>
        /// Extension added by the parser to keep the OpenAPI 3.0 "nullable: true" keyword of schemas without type,
        /// which is dropped by the OpenAPI reader.
        /// </summary>
        internal const string NullableExtension = "x-criteo-comparator-nullable";

        internal static bool IsPropertyRequired(this IOpenApiSchema schema, string propertyName) =>
            schema.Required != null && schema.Required.Contains(propertyName);

        internal static string StringValue(this JsonNode jsonNode)
        {
            if (jsonNode == null || jsonNode.IsJsonNullSentinel())
                return "null";

            return jsonNode.GetValueKind() == JsonValueKind.String
                ? jsonNode.GetValue<string>()
                : jsonNode.ToJsonString();
        }

        /// <summary>
        /// Indicate if the schema accepts null values:
        /// "nullable: true" in OpenAPI 3.0, "null" in the list of types in OpenAPI 3.1.
        /// </summary>
        internal static bool IsNullable(this IOpenApiSchema schema) =>
            schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.Null)
            || schema.Extensions != null
            && schema.Extensions.TryGetValue(NullableExtension, out var extension)
            && (extension as JsonNodeExtension)?.Node?.GetValueKind() == JsonValueKind.True;

        /// <summary>
        /// Type of the schema without the "null" type, which is handled as the nullable property.
        /// Multiple types (OpenAPI 3.1) are joined with '|'. Returns null when no type is defined.
        /// </summary>
        internal static string TypeName(this IOpenApiSchema schema)
        {
            if (schema.Type == null)
                return null;

            // A schema that only accepts null (type: 'null') has no other type: its nullability is compared instead.
            // It is also how an OpenAPI 3.0 schema without type but "nullable: true" is converted to OpenAPI 3.1.
            var type = schema.Type.Value & ~JsonSchemaType.Null;
            if (type == 0)
                return null;

            return string.Join("|", type.ToIdentifiers());
        }

        /// <summary>
        /// Enum values of the schema. The OpenAPI 3.1 "const" keyword is handled as an enum with a single value.
        /// When the schema is nullable, the null value is ignored: it is handled as the nullable property
        /// (OpenAPI 3.1 requires null in the enum of a nullable schema, OpenAPI 3.0 does not).
        /// </summary>
        internal static IList<JsonNode> EnumValues(this IOpenApiSchema schema)
        {
            IList<JsonNode> values;
            if (schema.Enum != null && schema.Enum.Count > 0)
                values = schema.Enum;
            else if (schema.Const != null)
                values = new List<JsonNode> { schema.ConstValue() };
            else
                return null;

            return schema.IsNullable()
                ? values.Where(value => value != null && !value.IsJsonNullSentinel()).ToList()
                : values;
        }

        /// <summary>
        /// The reader stores "const" as a string: it is converted back to a number or a boolean
        /// when the schema is not a string.
        /// </summary>
        private static JsonNode ConstValue(this IOpenApiSchema schema)
        {
            var isString = schema.Type.HasValue && schema.Type.Value.HasFlag(JsonSchemaType.String);
            if (!isString)
            {
                try
                {
                    var value = JsonNode.Parse(schema.Const);
                    var kind = value?.GetValueKind();
                    if (kind == JsonValueKind.Number || kind == JsonValueKind.True || kind == JsonValueKind.False)
                        return value;
                }
                catch (JsonException)
                {
                    // Not a JSON literal: handled as a string
                }
            }

            return JsonValue.Create(schema.Const);
        }

        /// <summary>
        /// A schema wrapped by another one (see <see cref="IsWrapper"/>).
        /// </summary>
        internal sealed class WrappedSchema
        {
            internal WrappedSchema(IOpenApiSchema schema, bool isNullable, string keyword, int index)
            {
                Schema = schema;
                IsNullable = isNullable;
                Keyword = keyword;
                Index = index;
            }

            /// The wrapped schema
            internal IOpenApiSchema Schema { get; }

            /// Whether the wrapper makes the wrapped schema nullable
            internal bool IsNullable { get; }

            /// The composition keyword of the wrapper: allOf, oneOf or anyOf
            internal string Keyword { get; }

            /// The index of the wrapped schema in the composition
            internal int Index { get; }
        }

        /// <summary>
        /// Detect a schema that only wraps another schema, to make it nullable or to add sibling keywords to a $ref:
        /// "nullable: true" + "allOf: [X]" (OpenAPI 3.0), "oneOf/anyOf: [X, {type: 'null'}]" (OpenAPI 3.1), or a
        /// single element allOf/oneOf/anyOf.
        /// </summary>
        /// <param name="schema">The schema</param>
        /// <param name="wrapped">The wrapped schema (X), null if the schema is not a wrapper</param>
        internal static bool IsWrapper(this IOpenApiSchema schema, out WrappedSchema wrapped)
        {
            wrapped = null;

            // Cheap check first: it is called for every compared schema, and most of them have no composition
            if (schema == null || !(schema.AllOf?.Count > 0 || schema.OneOf?.Count > 0 || schema.AnyOf?.Count > 0))
                return false;

            if (schema.IsReference() || schema.TypeName() != null
                || schema.Properties?.Count > 0 || schema.Items != null || schema.AdditionalProperties != null
                || schema.EnumValues()?.Count > 0 || schema.Discriminator != null || schema.Not != null
                || schema.ReadOnly || schema.Default != null)
                return false;

            var compositions = new[] { ("allOf", schema.AllOf), ("oneOf", schema.OneOf), ("anyOf", schema.AnyOf) }
                .Where(composition => composition.Item2 != null && composition.Item2.Count > 0)
                .ToList();
            if (compositions.Count != 1)
                return false;

            var (keyword, branches) = compositions[0];
            var nonNullIndexes = Enumerable.Range(0, branches.Count).Where(index => !branches[index].IsNullSchema()).ToList();
            var nullBranchCount = branches.Count - nonNullIndexes.Count;

            if (nonNullIndexes.Count != 1 || nullBranchCount > 1 || nullBranchCount == 1 && keyword == "allOf")
                return false;

            var index = nonNullIndexes[0];
            wrapped = new WrappedSchema(branches[index], schema.IsNullable() || nullBranchCount == 1, keyword, index);
            return true;
        }

        /// <summary>
        /// Indicate if the schema is an inline allOf, oneOf or anyOf composition.
        /// </summary>
        internal static bool IsComposition(this IOpenApiSchema schema) =>
            !schema.IsReference() && (schema.AllOf?.Count > 0 || schema.OneOf?.Count > 0 || schema.AnyOf?.Count > 0);

        /// <summary>
        /// Returns the wrapped schema if the schema is a wrapper (see <see cref="IsWrapper"/>), the schema itself otherwise.
        /// </summary>
        internal static IOpenApiSchema Unwrap(this IOpenApiSchema schema) =>
            schema.IsWrapper(out var wrapped) ? wrapped.Schema : schema;

        /// <summary>
        /// Indicate if the schema only accepts null: {type: 'null'}
        /// </summary>
        internal static bool IsNullSchema(this IOpenApiSchema schema) =>
            !schema.IsReference() && schema.Type == JsonSchemaType.Null
            && !(schema.Properties?.Count > 0) && !(schema.AllOf?.Count > 0) && !(schema.OneOf?.Count > 0)
            && !(schema.AnyOf?.Count > 0);

        /// <summary>
        /// The composition keyword (oneOf, then anyOf) that contains a {type: 'null'} schema, null if there is none.
        /// It is how OpenAPI 3.1 makes a composition nullable.
        /// </summary>
        internal static string NullBranchKeyword(this IOpenApiSchema schema)
        {
            if (schema.OneOf != null && schema.OneOf.Any(branch => branch.IsNullSchema()))
                return "oneOf";
            if (schema.AnyOf != null && schema.AnyOf.Any(branch => branch.IsNullSchema()))
                return "anyOf";
            return null;
        }

        /// <summary>
        /// Indicate if the schema accepts null values, whatever the way it is expressed: "nullable: true" (OpenAPI 3.0),
        /// "null" in the list of types, or a {type: 'null'} schema in a oneOf/anyOf (OpenAPI 3.1).
        /// Both documents must use this definition, otherwise an equivalent migration from OpenAPI 3.0
        /// ("nullable: true" + "oneOf: [A, B]") to OpenAPI 3.1 ("oneOf: [A, B, {type: 'null'}]") is reported as a change.
        /// </summary>
        internal static bool AcceptsNull(this IOpenApiSchema schema) =>
            schema.IsNullable() || schema.NullBranchKeyword() != null;

        /// <summary>
        /// Lower bound of the schema and whether it is exclusive or not.
        /// OpenAPI 3.0 "exclusiveMinimum: true" is converted by the reader to an OpenAPI 3.1 numeric exclusiveMinimum.
        /// </summary>
        internal static (decimal? value, bool isExclusive) LowerBound(this IOpenApiSchema schema) =>
            Bound(schema.Minimum, schema.ExclusiveMinimum, isLowerBound: true);

        /// <summary>
        /// Upper bound of the schema and whether it is exclusive or not.
        /// OpenAPI 3.0 "exclusiveMaximum: true" is converted by the reader to an OpenAPI 3.1 numeric exclusiveMaximum.
        /// </summary>
        internal static (decimal? value, bool isExclusive) UpperBound(this IOpenApiSchema schema) =>
            Bound(schema.Maximum, schema.ExclusiveMaximum, isLowerBound: false);

        private static (decimal? value, bool isExclusive) Bound(string inclusive, string exclusive, bool isLowerBound)
        {
            var inclusiveValue = inclusive.ToDecimal();
            var exclusiveValue = exclusive.ToDecimal();

            if (exclusiveValue == null)
                return (inclusiveValue, false);

            if (inclusiveValue == null)
                return (exclusiveValue, true);

            // Both are defined (only possible in OpenAPI 3.1): the most constraining one applies.
            var isExclusiveMoreConstraining = isLowerBound
                ? exclusiveValue >= inclusiveValue
                : exclusiveValue <= inclusiveValue;
            return isExclusiveMoreConstraining ? (exclusiveValue, true) : (inclusiveValue, false);
        }
    }
}
