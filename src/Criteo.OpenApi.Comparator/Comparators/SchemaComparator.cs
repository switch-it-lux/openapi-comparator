// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Criteo.OpenApi.Comparator.Comparators.Extensions;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators
{
    internal class SchemaComparator
    {
        private readonly bool _alwaysVisitSchemas;
        private readonly HashSet<IOpenApiSchema> _visitedSchemas;

        private readonly IDictionary<IOpenApiSchema, DataDirection> _compareDirections;

        private readonly string _excludeExtensionKey;

        internal SchemaComparator(bool alwaysCompareSchemas = false, string excludeExtensionKey = null)
        {
            _alwaysVisitSchemas = alwaysCompareSchemas;
            _visitedSchemas = new HashSet<IOpenApiSchema>();
            _compareDirections = new Dictionary<IOpenApiSchema, DataDirection>();
            _excludeExtensionKey = excludeExtensionKey;
        }

        internal void Compare(ComparisonContext context,
            IOpenApiSchema oldSchema,
            IOpenApiSchema newSchema,
            bool isSchemaReferenced = true,
            bool compareNullable = true)
        {
            if (oldSchema == null && newSchema == null)
                return;

            if (oldSchema.ShouldExcludeSchema(_excludeExtensionKey) || newSchema.ShouldExcludeSchema(_excludeExtensionKey))
                return;

            if (oldSchema == null)
            {
                context.LogError(ComparisonRules.AddedSchema);
                return;
            }

            if (newSchema == null)
            {
                context.LogBreakingChange(ComparisonRules.RemovedDefinition, default(string));
                return;
            }

            // Schemas are unwrapped only when a wrapper is used to make a schema nullable, and when the other schema
            // is not a composition itself: other allOf/oneOf/anyOf are compared as such.
            oldSchema.IsWrapper(out var oldWrapped);
            newSchema.IsWrapper(out var newWrapped);
            if ((oldWrapped?.IsNullable == true || newWrapped?.IsNullable == true)
                && (oldWrapped != null || !oldSchema.IsComposition())
                && (newWrapped != null || !newSchema.IsComposition()))
            {
                // The exclusion is checked on the wrapped schemas before comparing the nullability: the check at the
                // beginning of the method only sees the wrapper (its extensions and its allOf), not a oneOf/anyOf branch.
                if (oldWrapped?.Schema.ShouldExcludeSchema(_excludeExtensionKey) == true
                    || newWrapped?.Schema.ShouldExcludeSchema(_excludeExtensionKey) == true)
                    return;

                // The nullability is compared on the wrapper: the wrapped schema is often a referenced schema,
                // which may have already been compared (and is then skipped).
                CompareNullable(context,
                    oldWrapped?.IsNullable == true || Resolve(oldWrapped?.Schema ?? oldSchema, context.OldOpenApiDocument).IsNullable(),
                    newWrapped?.IsNullable == true || Resolve(newWrapped?.Schema ?? newSchema, context.NewOpenApiDocument).IsNullable(),
                    NullableKeyword(context.OldSpecVersion, oldWrapped),
                    NullableKeyword(context.NewSpecVersion, newWrapped));

                context.PushWrappedSchema(oldWrapped?.Keyword, oldWrapped?.Index ?? 0, newWrapped?.Keyword, newWrapped?.Index ?? 0);
                Compare(context, oldWrapped?.Schema ?? oldSchema, newWrapped?.Schema ?? newSchema, isSchemaReferenced,
                    compareNullable: false);
                context.Pop();
                return;
            }

            if (newSchema.GetReferenceV3() != null
                && !newSchema.GetReferenceV3().Equals(oldSchema.GetReferenceV3()))
            {
                context.LogBreakingChange(ComparisonRules.ReferenceRedirection);
            }

            // Schemas defined in the components section are handled like referenced schemas
            var areSchemasReferenced = context.IsComponent(oldSchema) || context.IsComponent(newSchema);
            if (newSchema.IsReference())
            {
                newSchema = newSchema.GetReference().Resolve(context.NewOpenApiDocument.Components?.Schemas);
                areSchemasReferenced = true;
                if (newSchema == null)
                    return;
            }

            if (oldSchema.IsReference())
            {
                oldSchema = oldSchema.GetReference().Resolve(context.OldOpenApiDocument.Components?.Schemas);
                areSchemasReferenced = true;
                if (oldSchema == null)
                    return;
            }

            if (context.Direction != DataDirection.None)
            {
                _compareDirections.TryGetValue(newSchema, out var savedDirection);

                // If this direction has already been checked, skip it
                if (context.Direction == savedDirection || savedDirection == DataDirection.Both)
                    return;

                context.Direction |= savedDirection;
                _compareDirections[newSchema] = context.Direction;
            }

            if (areSchemasReferenced)
            {
                if (_visitedSchemas.Contains(oldSchema) && context.Direction != DataDirection.Both)
                    return;

                // If direction is response or request and _alwaysVisitSchemas is true, do not add schema as visitedma itself
                if (!_alwaysVisitSchemas || context.Direction == DataDirection.None)
                    _visitedSchemas.Add(oldSchema);
            }

            CompareReadOnly(context, oldSchema.ReadOnly, newSchema.ReadOnly);

            CompareDiscriminator(context, oldSchema.Discriminator, newSchema.Discriminator);

            CompareDefault(context, oldSchema.Default, newSchema.Default);

            CompareConstraints(context, oldSchema, newSchema);

            CompareType(context, oldSchema.TypeName(), newSchema.TypeName());

            CompareItems(context, oldSchema.Items, newSchema.Items);

            IOpenApiExtension enumExtension = null;
            oldSchema.Extensions?.TryGetValue("x-ms-enum", out enumExtension);
            CompareEnum(context,
                oldSchema.EnumValues() ?? new List<JsonNode>(),
                newSchema.EnumValues() ?? new List<JsonNode>(),
                (enumExtension as JsonNodeExtension)?.Node as JsonObject,
                EnumKeyword(oldSchema),
                EnumKeyword(newSchema));

            CompareFormat(context, oldSchema, newSchema);

            CompareAllOf(context,
                oldSchema.AllOf ?? new List<IOpenApiSchema>(),
                newSchema.AllOf ?? new List<IOpenApiSchema>());

            // When a oneOf/anyOf has a {type: 'null'} schema in one of the documents, the nullability is compared (once)
            // on that composition, so that the message points to it. Otherwise, it is compared at the end.
            var nullBranchKeyword = oldSchema.NullBranchKeyword() ?? newSchema.NullBranchKeyword();

            CompareComposition(context, "oneOf", ComparisonRules.DifferentOneOf,
                oldSchema.OneOf ?? new List<IOpenApiSchema>(),
                newSchema.OneOf ?? new List<IOpenApiSchema>(),
                nullBranchKeyword == "oneOf" ? (oldSchema.AcceptsNull(), newSchema.AcceptsNull()) : default);

            CompareComposition(context, "anyOf", ComparisonRules.DifferentAnyOf,
                oldSchema.AnyOf ?? new List<IOpenApiSchema>(),
                newSchema.AnyOf ?? new List<IOpenApiSchema>(),
                nullBranchKeyword == "anyOf" ? (oldSchema.AcceptsNull(), newSchema.AcceptsNull()) : default);

            CompareProperties(context, oldSchema, newSchema, isSchemaReferenced);

            CompareRequired(context, oldSchema.Required, newSchema.Required);

            // Without null branch, AcceptsNull is the same as IsNullable
            if (compareNullable && nullBranchKeyword == null)
                CompareNullable(context, oldSchema.IsNullable(), newSchema.IsNullable(),
                    NullableKeyword(context.OldSpecVersion, null), NullableKeyword(context.NewSpecVersion, null));
        }

        private static IOpenApiSchema Resolve(IOpenApiSchema schema, OpenApiDocument document) =>
            schema.IsReference()
                ? schema.GetReference().Resolve(document.Components?.Schemas) ?? schema
                : schema;

        /// <summary>
        /// The keyword that makes a schema nullable in the document, used to locate the messages:
        /// "nullable" in OpenAPI 3.0, the type (or the oneOf/anyOf with a null schema) in OpenAPI 3.1.
        /// </summary>
        private static string NullableKeyword(OpenApiSpecVersion version, OpenApiSchemaExtensions.WrappedSchema wrapped)
        {
            if (version != OpenApiSpecVersion.OpenApi3_1)
                return "nullable";

            return wrapped != null && wrapped.Keyword != "allOf" ? wrapped.Keyword : "type";
        }

        private static void CompareNullable(ComparisonContext context,
            bool oldNullable,
            bool newNullable,
            string oldKeyword,
            string newKeyword)
        {
            if (oldNullable == newNullable) return;
            context.PushPropertyPerDocument(oldKeyword, newKeyword);
            context.LogBreakingChange(
                ComparisonRules.NullablePropertyChanged,
                oldNullable.ToString().ToLower(),
                newNullable.ToString().ToLower()
            );
            context.Pop();
        }

        private static void CompareReadOnly(ComparisonContext context,
            bool oldReadOnly,
            bool newReadOnly)
        {
            if (oldReadOnly != newReadOnly)
            {
                context.PushProperty("readOnly");
                context.LogBreakingChange(
                    ComparisonRules.ReadonlyPropertyChanged,
                    oldReadOnly.ToString().ToLower(),
                    newReadOnly.ToString().ToLower()
                );
                context.Pop();
            }
        }

        private static void CompareDiscriminator(ComparisonContext context,
            OpenApiDiscriminator oldDiscriminator, OpenApiDiscriminator newDiscriminator)
        {
            if (oldDiscriminator == null && newDiscriminator != null
                || oldDiscriminator?.PropertyName != null && !oldDiscriminator.PropertyName.Equals(newDiscriminator?.PropertyName))
            {
                context.PushProperty("discriminator");
                context.LogBreakingChange(ComparisonRules.DifferentDiscriminator);
                context.Pop();
            }
        }

        private static void CompareDefault(ComparisonContext context,
            JsonNode oldDefault,
            JsonNode newDefault)
        {
            if (oldDefault == null && newDefault == null)
                return;

            if (!oldDefault.DifferFrom(newDefault))
                return;

            context.PushProperty("default");
            context.LogBreakingChange(ComparisonRules.DefaultValueChanged);
            context.Pop();
        }

         private static void CompareConstraints(ComparisonContext context,
             IOpenApiSchema oldSchema, IOpenApiSchema newSchema)
        {
            var (oldMaximum, isOldMaximumExclusive) = oldSchema.UpperBound();
            var (newMaximum, isNewMaximumExclusive) = newSchema.UpperBound();
            if (oldMaximum.DifferFrom(newMaximum) || isOldMaximumExclusive != isNewMaximumExclusive)
            {
                CompareConstraint(context, oldMaximum, newMaximum, "maximum", false,
                    isOldMaximumExclusive != isNewMaximumExclusive,
                    BoundKeyword(context.OldSpecVersion, "maximum", isOldMaximumExclusive),
                    BoundKeyword(context.NewSpecVersion, "maximum", isNewMaximumExclusive));
            }

            var (oldMinimum, isOldMinimumExclusive) = oldSchema.LowerBound();
            var (newMinimum, isNewMinimumExclusive) = newSchema.LowerBound();
            if (oldMinimum.DifferFrom(newMinimum) || isOldMinimumExclusive != isNewMinimumExclusive)
            {
                CompareConstraint(context, oldMinimum, newMinimum, "minimum", true,
                    isOldMinimumExclusive != isNewMinimumExclusive,
                    BoundKeyword(context.OldSpecVersion, "minimum", isOldMinimumExclusive),
                    BoundKeyword(context.NewSpecVersion, "minimum", isNewMinimumExclusive));
            }

            if (oldSchema.MaxLength.DifferFrom(newSchema.MaxLength))
            {
                CompareConstraint(context, oldSchema.MaxLength, newSchema.MaxLength, "maxLength", false);
            }

            if (oldSchema.MinLength.DifferFrom(newSchema.MinLength))
            {
                CompareConstraint(context, oldSchema.MinLength, newSchema.MinLength, "minLength", true);
            }

            if (oldSchema.MaxItems.DifferFrom(newSchema.MaxItems))
            {
                CompareConstraint(context, oldSchema.MaxItems, newSchema.MaxItems, "maxItems", false);
            }

            if (oldSchema.MinItems.DifferFrom(newSchema.MinItems))
            {
                CompareConstraint(context, oldSchema.MinItems, newSchema.MinItems, "minItems", true);
            }

            if (oldSchema.MultipleOf.DifferFrom(newSchema.MultipleOf))
            {
                context.PushProperty("multipleOf");
                context.LogBreakingChange(ComparisonRules.ConstraintChanged, "multipleOf");
                context.Pop();
            }

            if (oldSchema.UniqueItems != newSchema.UniqueItems)
            {
                context.PushProperty("uniqueItems");
                context.LogBreakingChange(ComparisonRules.ConstraintChanged, "uniqueItems");
                context.Pop();
            }

            if (oldSchema.Pattern.DifferFrom(newSchema.Pattern))
            {
                context.PushProperty("pattern");
                context.LogBreakingChange(ComparisonRules.ConstraintChanged, "pattern");
                context.Pop();
            }
        }

         /// <summary>
         /// The keyword of a bound in the document, used to locate the messages: in OpenAPI 3.1, an exclusive bound is
         /// defined by exclusiveMinimum/exclusiveMaximum (in OpenAPI 3.0, by minimum/maximum + a boolean).
         /// </summary>
         private static string BoundKeyword(OpenApiSpecVersion version, string keyword, bool isExclusive) =>
             version == OpenApiSpecVersion.OpenApi3_1 && isExclusive
                 ? "exclusive" + char.ToUpperInvariant(keyword[0]) + keyword.Substring(1)
                 : keyword;

         private static void CompareConstraint(ComparisonContext context, decimal? oldConstraint,
             decimal? newConstraint, string attributeName, bool isLowerBound, bool additionalCondition = false,
             string oldKeyword = null, string newKeyword = null)
         {
             context.PushPropertyPerDocument(oldKeyword ?? attributeName, newKeyword ?? attributeName);
             if (additionalCondition)
             {
                 context.LogBreakingChange(ComparisonRules.ConstraintChanged, attributeName);
             }
             else if (Narrows(oldConstraint, newConstraint, isLowerBound))
             {
                 if (context.Direction == DataDirection.Request)
                    context.LogBreakingChange(ComparisonRules.ConstraintIsStronger, attributeName);
                 else
                    context.LogInfo(ComparisonRules.ConstraintIsStronger, attributeName);
             }
             else if (Widens(oldConstraint, newConstraint, isLowerBound))
             {
                 if (context.Direction == DataDirection.Response)
                    context.LogBreakingChange(ComparisonRules.ConstraintIsWeaker, attributeName);
                 else
                    context.LogInfo(ComparisonRules.ConstraintIsWeaker, attributeName);
             }
             context.Pop();
         }

        private static bool Narrows(decimal? oldConstraint, decimal? newConstraint, bool isLowerBound)
        {
            if (oldConstraint == null && newConstraint == null)
                return false;

            if (oldConstraint == null)
                return true;

            if (newConstraint == null)
                return false;

            return isLowerBound
                ? newConstraint > oldConstraint
                : newConstraint < oldConstraint;
        }

        private static bool Widens(decimal? oldConstraint, decimal? newConstraint, bool isLowerBound)
        {
            if (oldConstraint == null && newConstraint == null)
                return false;

            if (oldConstraint == null)
                return false;

            if (newConstraint == null)
                return true;

            return isLowerBound
                ? newConstraint < oldConstraint
                : newConstraint > oldConstraint;
        }

        private static void CompareType(ComparisonContext context, string oldType, string newType)
        {
            if (oldType == null && newType == null)
                return;

            // Are the types the same?
            if (oldType == null || newType == null || !oldType.Equals(newType))
            {
                var oldTypeString = oldType == null ? "" : oldType.ToLower();
                var newTypeString = newType == null ? "" : newType.ToLower();

                context.PushProperty("type");
                context.LogBreakingChange(ComparisonRules.TypeChanged, newTypeString, oldTypeString);
                context.Pop();
            }
        }

        private void CompareItems(ComparisonContext context,
            IOpenApiSchema oldItems,
            IOpenApiSchema newItems)
        {
            if (oldItems == null || newItems == null) return;

            context.PushProperty("items");
            Compare(context, oldItems, newItems);
            context.Pop();
        }

        private static void CompareEnum(ComparisonContext context,
            ICollection<JsonNode> oldEnum,
            ICollection<JsonNode> newEnum,
            JsonObject enumExtension,
            string oldKeyword,
            string newKeyword)
        {
            if (oldEnum == null && newEnum == null) return;

            var relaxes = newEnum == null;
            var constrains = oldEnum == null;

            context.PushPropertyPerDocument(oldKeyword, newKeyword);

            if (!relaxes && !constrains)
            {
                // 1. Look for removed elements (constraining).
                var removedEnums = oldEnum.Where(oldEnumElement => newEnum.All(oldEnumElement.DifferFrom)).ToList();
                constrains = removedEnums.Any();

                // 2. Look for added elements (relaxing).
                var addedEnums = newEnum.Where(newEnumElement => oldEnum.All(newEnumElement.DifferFrom)).ToList();
                relaxes = addedEnums.Any();

                if (constrains)
                {
                    LogAction logger = context.Direction == DataDirection.Response ? context.LogWarning : context.LogBreakingChange;
                    logger(ComparisonRules.RemovedEnumValue, string.Join(", ", removedEnums.Select(e => e.StringValue())));
                }

                if (relaxes && !IsEnumModelAsString(enumExtension))
                {
                    LogAction logger = context.Direction == DataDirection.Request ? context.LogWarning : context.LogBreakingChange;
                    logger(ComparisonRules.AddedEnumValue, string.Join(", ", addedEnums.Select(e => e.StringValue())));
                }
            }

            if (relaxes && constrains)
                context.LogInfo(ComparisonRules.ConstraintChanged, "enum");
            else if (relaxes)
                context.LogInfo(ComparisonRules.ConstraintIsWeaker, "enum");
            else if (constrains)
                context.LogInfo(ComparisonRules.ConstraintIsStronger, "enum");

            context.Pop();
        }

        /// <summary>
        /// The keyword of the enum values in the document, used to locate the messages: "const" for a single value.
        /// </summary>
        private static string EnumKeyword(IOpenApiSchema schema) =>
            schema.Enum?.Count > 0 || schema.Const == null ? "enum" : "const";

        private static bool IsEnumModelAsString(JsonObject enumExtension) =>
            enumExtension != null
            && enumExtension.TryGetPropertyValue("modelAsString", out var modelAsString)
            && modelAsString?.GetValueKind() == JsonValueKind.True;

        private static void CompareFormat(ComparisonContext context,
            IOpenApiSchema oldSchema,
            IOpenApiSchema newSchema)
        {
            if (!oldSchema.Format.DifferFrom(newSchema.Format)
                || IsFormatChangeAllowed(context, oldSchema, newSchema))
                return;

            context.PushProperty("format");
            context.LogBreakingChange(ComparisonRules.TypeFormatChanged, newSchema.Format ?? "", oldSchema.Format ?? "");
            context.Pop();
        }

        private static bool IsFormatChangeAllowed(ComparisonContext context,
            IOpenApiSchema oldSchema,
            IOpenApiSchema newSchema)
        {
            if (newSchema.TypeName() != "integer" || context.Strict
                || oldSchema.Format == null || newSchema.Format == null)
                return false;

            var formatChangedFromInt32ToInt64 = oldSchema.Format.Equals("int32") && newSchema.Format.Equals("int64");
            var formatChangedFromInt64ToInt32 = oldSchema.Format.Equals("int64") && newSchema.Format.Equals("int32");

            return context.Direction == DataDirection.Request && formatChangedFromInt32ToInt64
                || context.Direction == DataDirection.Response && formatChangedFromInt64ToInt32;
        }

        private static void CompareAllOf(ComparisonContext context,
            IList<IOpenApiSchema> oldAllOf, IList<IOpenApiSchema> newAllOf)
        {
            if (oldAllOf == null && newAllOf == null)
                return;

            context.PushProperty("allOf");
            if (oldAllOf == null || newAllOf == null)
            {
                context.LogBreakingChange(ComparisonRules.DifferentAllOf);
                context.Pop();
                return;
            }

            var newAllOfReferences = newAllOf.Where(schema => schema.IsReference())
                .Select(schema => schema.GetReferenceV3()).ToList();
            var oldAllOfReferences = oldAllOf.Where(schema => schema.IsReference())
                .Select(schema => schema.GetReferenceV3()).ToList();

            var differenceCount = newAllOfReferences.Except(oldAllOfReferences).Count();
            differenceCount += oldAllOfReferences.Except(newAllOfReferences).Count();

            if (differenceCount > 0)
            {
                context.LogBreakingChange(ComparisonRules.DifferentAllOf);
            }
            context.Pop();
        }

        /// <summary>
        /// Compares the schemas of a oneOf or an anyOf: the referenced schemas must be the same, and they are compared.
        /// The nullability of the old and new schemas (see OpenApiSchemaExtensions.AcceptsNull) is given when it must be
        /// compared on this composition, null otherwise.
        /// </summary>
        private void CompareComposition(ComparisonContext context, string keyword, ComparisonRule differentRule,
            IList<IOpenApiSchema> oldSchemas, IList<IOpenApiSchema> newSchemas,
            (bool isOldNullable, bool isNewNullable)? nullability)
        {
            if (oldSchemas == null && newSchemas == null)
                return;

            context.PushProperty(keyword);
            if (oldSchemas == null || newSchemas == null)
            {
                context.LogBreakingChange(differentRule);
                context.Pop();
                return;
            }

            var newReferences = newSchemas.Where(schema => schema.IsReference())
                .Select(schema => schema.GetReferenceV3()).ToList();
            var oldReferences = oldSchemas.Where(schema => schema.IsReference())
                .Select(schema => schema.GetReferenceV3()).ToList();

            var differenceCount = newReferences.Except(oldReferences).Count();
            differenceCount += oldReferences.Except(newReferences).Count();

            if (differenceCount > 0)
            {
                context.LogBreakingChange(differentRule);
            }

            // A {type: 'null'} schema in the composition makes it nullable (OpenAPI 3.1). The nullability of the whole
            // schema is compared (not only the null branches): "nullable: true" (OpenAPI 3.0) is equivalent.
            if (nullability is var (isOldNullable, isNewNullable) && isOldNullable != isNewNullable)
            {
                context.LogBreakingChange(ComparisonRules.NullablePropertyChanged,
                    isOldNullable.ToString().ToLower(), isNewNullable.ToString().ToLower());
            }

            // The schemas are matched by reference: their positions may differ, and the composition may also contain
            // schemas that are not references (e.g. {type: 'null'}).
            var commonReferences = oldReferences.Distinct().Where(newReferences.Contains);
            foreach (var reference in commonReferences)
            {
                context.PushItemByReference(reference);
                Compare(context,
                    oldSchemas.First(schema => schema.GetReferenceV3() == reference),
                    newSchemas.First(schema => schema.GetReferenceV3() == reference));
                context.Pop();
            }

            context.Pop();
        }

        private void CompareProperties(ComparisonContext context,
            IOpenApiSchema oldSchema,
            IOpenApiSchema newSchema,
            bool isSchemaReferenced)
        {
            CompareAdditionalProperties(context, oldSchema.AdditionalProperties, newSchema.AdditionalProperties);

            context.PushProperty("properties");

            CompareRemovedProperties(context, oldSchema, newSchema);

            CompareAddedProperties(context, oldSchema, newSchema, isSchemaReferenced);

            CompareCommonProperties(context, oldSchema, newSchema);

            context.Pop();
        }

        private static void CompareRemovedProperties(ComparisonContext context,
            IOpenApiSchema oldSchema, IOpenApiSchema newSchema)
        {
            if (oldSchema.Properties == null)
                return;

            var removedProperties = newSchema.Properties == null
                ? oldSchema.Properties.Keys
                : oldSchema.Properties.Keys.Where(propertyName => !newSchema.Properties.ContainsKey(propertyName));
            foreach (var propertyName in removedProperties)
            {
                context.PushProperty(propertyName);
                context.LogBreakingChange(ComparisonRules.RemovedProperty, propertyName);
                context.Pop();
            }
        }

        private static void CompareAddedProperties(ComparisonContext context,
            IOpenApiSchema oldSchema, IOpenApiSchema newSchema, bool isSchemaReferenced)
        {
            if (newSchema.Properties == null)
                return;

            var addedProperties = oldSchema.Properties == null
                ? newSchema.Properties
                : newSchema.Properties.Where(property =>
                    !oldSchema.Properties.TryGetValue(property.Key, out var oldProperty) || oldProperty == null);

            foreach (var property in addedProperties)
            {
                context.PushProperty(property.Key);

                if (oldSchema.IsPropertyRequired(property.Key))
                {
                    context.LogBreakingChange(ComparisonRules.AddedRequiredProperty, property.Key);
                }

                if (context.Direction == DataDirection.Response)
                {
                    if (property.Value.ReadOnly)
                        context.LogInfo(ComparisonRules.AddedReadOnlyPropertyInResponse, property.Key);
                    else
                        context.LogBreakingChange(ComparisonRules.AddedPropertyInResponse, property.Key);
                }
                else if (isSchemaReferenced && !newSchema.IsPropertyRequired(property.Key))
                {
                    context.LogBreakingChange(ComparisonRules.AddedOptionalProperty, property.Key);
                }

                context.Pop();
            }
        }

        private void CompareCommonProperties(ComparisonContext context,
            IOpenApiSchema oldSchema, IOpenApiSchema newSchema)
        {
            if (oldSchema.Properties == null || newSchema.Properties == null)
                return;

            var commonProperties =
                oldSchema.Properties.Where(property => newSchema.Properties.ContainsKey(property.Key));
            foreach (var property in commonProperties)
            {
                context.PushProperty(property.Key);
                Compare(context, property.Value, newSchema.Properties[property.Key]);
                context.Pop();
            }
        }

        private void CompareAdditionalProperties(ComparisonContext context,
            IOpenApiSchema oldAdditionalProperties, IOpenApiSchema newAdditionalProperties)
        {
            context.PushProperty("additionalProperties");
            if (oldAdditionalProperties == null && newAdditionalProperties != null)
            {
                context.LogBreakingChange(ComparisonRules.AddedAdditionalProperties);
            }
            else if (oldAdditionalProperties != null && newAdditionalProperties == null)
            {
                context.LogBreakingChange(ComparisonRules.RemovedAdditionalProperties);
            }
            else if (newAdditionalProperties != null)
            {
                Compare(context, oldAdditionalProperties, newAdditionalProperties);
            }
            context.Pop();
        }

        /// <summary>
        /// Compares list of required properties of this model
        /// </summary>
        /// <param name="context">Comparision Context</param>
        /// <param name="oldRequired">A set of old required properties</param>
        /// <param name="newRequired">A set of new required properties</param>
        private static void CompareRequired(ComparisonContext context,
            ISet<string> oldRequired,
            ISet<string> newRequired)
        {
            if (newRequired == null)
                return;

            if (oldRequired == null)
            {
                context.LogBreakingChange(ComparisonRules.AddedRequiredProperty, string.Join(", ", newRequired));
                return;
            }

            List<string> addedRequiredProperties = newRequired.Except(oldRequired).ToList();
            if (addedRequiredProperties.Any())
            {
                context.LogBreakingChange(ComparisonRules.AddedRequiredProperty,
                    string.Join(", ", addedRequiredProperties));
            }
        }
    }
}
