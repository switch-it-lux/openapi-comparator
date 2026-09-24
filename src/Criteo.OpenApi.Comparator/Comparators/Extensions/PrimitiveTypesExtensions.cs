// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Criteo.OpenApi.Comparator.Comparators.Extensions
{
    internal static class PrimitiveTypesExtensions
    {
        internal static bool DifferFrom(this string oldString, string newString) =>
            oldString == null && newString != null || oldString != null && !oldString.Equals(newString);

        internal static bool DifferFrom(this decimal? oldDecimal, decimal? newDecimal) =>
            oldDecimal == null && newDecimal != null || oldDecimal != null && !oldDecimal.Equals(newDecimal);

        internal static bool DifferFrom(this int? oldDecimal, int? newDecimal) =>
            oldDecimal == null && newDecimal != null || oldDecimal != null && !oldDecimal.Equals(newDecimal);

        /// <summary>
        /// Parse a numeric value stored as a string (e.g. minimum, maximum), using the invariant culture.
        /// Values outside of the decimal range (e.g. double.MaxValue) are clamped to decimal.MinValue/MaxValue.
        /// </summary>
        internal static decimal? ToDecimal(this string value)
        {
            if (decimal.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var result))
                return result;

            if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
                && !double.IsNaN(doubleValue))
                return doubleValue > 0 ? decimal.MaxValue : decimal.MinValue;

            return null;
        }

        internal static bool DifferFrom(this JsonNode oldJsonNode, JsonNode newJsonNode)
        {
            if (oldJsonNode == null || newJsonNode == null)
                return true;

            var oldKind = oldJsonNode.GetValueKind();
            var newKind = newJsonNode.GetValueKind();

            if (IsBoolean(oldKind) && IsBoolean(newKind))
                return oldKind != newKind;

            if (oldKind != newKind)
                return true;

            switch (oldKind)
            {
                case JsonValueKind.String:
                    return oldJsonNode.GetValue<string>().DifferFrom(newJsonNode.GetValue<string>());
                case JsonValueKind.Number:
                    return oldJsonNode.ToJsonString().ToDecimal().DifferFrom(newJsonNode.ToJsonString().ToDecimal());
                case JsonValueKind.Object:
                    var oldObject = oldJsonNode.AsObject();
                    var newObject = newJsonNode.AsObject();
                    return oldObject.Count != newObject.Count
                        || oldObject.Any(property => !newObject.TryGetPropertyValue(property.Key, out var newValue)
                            || DifferFromNested(property.Value, newValue));
                case JsonValueKind.Array:
                    var oldArray = oldJsonNode.AsArray();
                    var newArray = newJsonNode.AsArray();
                    return oldArray.Count != newArray.Count
                        || oldArray.Where((item, index) => DifferFromNested(item, newArray[index])).Any();
                default:
                    return false;
            }
        }

        /// <summary>
        /// Values nested in an object or an array: a JSON null is represented as a null node.
        /// </summary>
        private static bool DifferFromNested(JsonNode oldJsonNode, JsonNode newJsonNode) =>
            oldJsonNode == null || newJsonNode == null
                ? oldJsonNode != newJsonNode
                : oldJsonNode.DifferFrom(newJsonNode);

        private static bool IsBoolean(JsonValueKind kind) => kind == JsonValueKind.True || kind == JsonValueKind.False;
    }
}
