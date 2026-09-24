// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using Criteo.OpenApi.Comparator.Comparators.Extensions;
using Microsoft.OpenApi;

namespace Criteo.OpenApi.Comparator.Comparators
{
    internal class ParameterComparator
    {
        private readonly SchemaComparator _schemaComparator;
        private readonly ContentComparator _contentComparator;

        /// Referenced parameters already compared, with the direction they were compared in
        private readonly HashSet<(IOpenApiParameter, DataDirection)> _visitedParameters;

        internal ParameterComparator(SchemaComparator schemaComparator, ContentComparator contentComparator)
        {
            _schemaComparator = schemaComparator;
            _contentComparator = contentComparator;
            _visitedParameters = new HashSet<(IOpenApiParameter, DataDirection)>();
        }

        internal void Compare(
            ComparisonContext context,
            IOpenApiParameter oldParameter,
            IOpenApiParameter newParameter)
        {
            ComponentComparator<IOpenApiParameter>.Compare(context, oldParameter, newParameter);

            using (context.WithDirection(DataDirection.Request))
            {
                // Parameters defined in the components section are handled like referenced parameters
                var areParametersReferenced = context.IsComponent(oldParameter) || context.IsComponent(newParameter);

                if (oldParameter.IsReference())
                {
                    oldParameter = oldParameter.GetReference().Resolve(context.OldOpenApiDocument.Components?.Parameters);
                    areParametersReferenced = true;
                    if (oldParameter == null)
                        return;
                }
                if (newParameter.IsReference())
                {
                    newParameter = newParameter.GetReference().Resolve(context.NewOpenApiDocument.Components?.Parameters);
                    areParametersReferenced = true;
                    if (newParameter == null)
                        return;
                }

                // A referenced parameter is compared once per direction: the same change may be breaking in one
                // direction only (e.g. a parameter shared by a path and a webhook, where the direction is inverted).
                if (areParametersReferenced && !_visitedParameters.Add((oldParameter, context.Direction)))
                    return;

                CompareIn(context, oldParameter.In, newParameter.In);

                CompareConstantStatus(context, oldParameter, newParameter);

                CompareRequiredStatus(context, oldParameter, newParameter);

                CompareStyle(context, oldParameter, newParameter);

                CompareSchema(context, oldParameter.Schema, newParameter.Schema);

                _contentComparator.Compare(context, oldParameter.Content, newParameter.Content);
            }
        }

        private static void CompareIn(ComparisonContext context,
            ParameterLocation? oldIn,
            ParameterLocation? newIn)
        {
            if (oldIn != newIn)
            {
                context.PushProperty("in");
                context.LogBreakingChange(ComparisonRules.ParameterInHasChanged,
                    oldIn.ToString().ToLower(),
                    newIn.ToString().ToLower()
                );
                context.Pop();
            }
        }

        private static void CompareConstantStatus(ComparisonContext context,
            IOpenApiParameter oldParameter,
            IOpenApiParameter newParameter)
        {
            if (newParameter.IsConstant() != oldParameter.IsConstant())
            {
                context.PushProperty("enum");
                context.LogBreakingChange(ComparisonRules.ConstantStatusHasChanged);
                context.Pop();
            }
        }

        private static void CompareRequiredStatus(ComparisonContext context,
            IOpenApiParameter oldParameter, IOpenApiParameter newParameter)
        {
            if (oldParameter.IsRequired() == newParameter.IsRequired())
                return;

            // In the response direction (webhooks), the parameters are sent by the API: a parameter that is no longer
            // required may be missing for the consumer, whereas a newly required parameter is always sent.
            var isBreaking = context.Direction == DataDirection.Response
                ? !newParameter.IsRequired()
                : newParameter.IsRequired();
            LogAction logger = isBreaking ? context.LogBreakingChange : context.LogInfo;

            context.PushProperty("required");
            logger(ComparisonRules.RequiredStatusChange, oldParameter.IsRequired(), newParameter.IsRequired());
            context.Pop();
        }

        private static void CompareStyle(ComparisonContext context,
            IOpenApiParameter oldParameter,
            IOpenApiParameter newParameter)
        {
            if (oldParameter.Style == newParameter.Style)
                return;

            // The reader sets a default style depending on the location (form for query and cookie, simple for path
            // and header): when the location changes, the change between the two default styles is implied by it
            // (and already reported), whereas an explicit style change is still reported.
            var isImpliedByLocationChange = oldParameter.In != newParameter.In
                && oldParameter.Style == DefaultStyle(oldParameter.In)
                && newParameter.Style == DefaultStyle(newParameter.In);
            if (!isImpliedByLocationChange)
            {
                context.PushProperty("style");
                context.LogBreakingChange(ComparisonRules.ParameterStyleChanged, oldParameter.Name);
                context.Pop();
            }
        }

        private static ParameterStyle? DefaultStyle(ParameterLocation? location) =>
            location switch
            {
                ParameterLocation.Query => ParameterStyle.Form,
                ParameterLocation.Cookie => ParameterStyle.Form,
                ParameterLocation.Path => ParameterStyle.Simple,
                ParameterLocation.Header => ParameterStyle.Simple,
                _ => null,
            };

        private void CompareSchema(ComparisonContext context,
            IOpenApiSchema oldSchema, IOpenApiSchema newSchema)
        {
            if (oldSchema == null || newSchema == null)
                return;

            context.PushProperty("schema");
            _schemaComparator.Compare(context, oldSchema, newSchema);
            context.Pop();
        }
    }
}
