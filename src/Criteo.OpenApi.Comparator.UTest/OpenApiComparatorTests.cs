// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.Collections.Generic;
using System.Linq;
using Criteo.OpenApi.Comparator.Comparators.Extensions;
using Microsoft.OpenApi;
using NUnit.Framework;

namespace Criteo.OpenApi.Comparator.UTest
{
    /// <summary>
    /// Tests of the comparison entry point that are not covered by the specification test cases (parsing errors).
    /// </summary>
    [TestFixture]
    public class OpenApiComparatorTests
    {
        private const string Header = "openapi: 3.0.3\ninfo: {title: t, version: '1.0'}\npaths: {}\n";

        private const string ValidPathItem = "get: {operationId: get, responses: {'200': {description: ok}}}";

        [Test]
        public void Compare_Should_Report_Invalid_Custom_Path_As_Parsing_Error_And_Ignore_It()
        {
            var oldSpec = Header + "x-ms-paths:\n"
                + "  /a?x=1: {" + ValidPathItem + "}\n"
                + "  /b?x=1: {" + ValidPathItem + "}\n";
            var newSpec = Header + "x-ms-paths:\n"
                + "  /a?x=1: {get: {operationId: renamed, responses: {'200': {description: ok}}}}\n"
                + "  /b?x=1: invalid\n";

            var messages = OpenApiComparator.Compare(oldSpec, newSpec, out var parsingErrors).ToList();

            var errors = parsingErrors.Select(error => error.ToString()).ToList();
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.StartWith("[new]").And.Contain("#/x-ms-paths/~1b?x=1"));

            // The invalid path is ignored in both documents: it is not reported as removed
            Assert.That(messages.Select(message => message.Code), Does.Not.Contain(ComparisonRules.RemovedPath.Code));
            // The valid paths are still compared
            Assert.That(messages.Select(message => message.Code), Does.Contain(ComparisonRules.ModifiedOperationId.Code));
        }

        [TestCase("{get: 5}")]
        [TestCase("{get: {responses: 5}}")]
        [TestCase("{parameters: 5}")]
        public void Compare_Should_Report_Custom_Path_Reader_Errors_As_Parsing_Errors(string invalidPathItem)
        {
            var oldSpec = Header + "x-ms-paths:\n  /b?x=1: " + invalidPathItem + "\n";
            var newSpec = Header + "x-ms-paths:\n  /b?x=1: {" + ValidPathItem + "}\n";

            var messages = OpenApiComparator.Compare(oldSpec, newSpec, out var parsingErrors).ToList();

            Assert.That(parsingErrors.Select(error => error.ToString()), Has.Some.StartWith("[old]").And.Contain("#/x-ms-paths/~1b?x=1"));
            Assert.That(messages.Select(message => message.Code), Does.Not.Contain(ComparisonRules.AddedPath.Code));
        }

        [Test]
        public void Compare_Should_Not_Fail_With_Invalid_Parameter_In_Custom_Path()
        {
            var spec = Header + "x-ms-paths:\n  /b?x=1: {parameters: [5], " + ValidPathItem + "}\n";

            var otherSpec = Header + "x-ms-paths:\n  /b?x=1: {parameters: [{name: id, in: query, required: true}], "
                + ValidPathItem + "}\n";

            Assert.DoesNotThrow(() => OpenApiComparator.Compare(spec, spec, out _).ToList());
            Assert.DoesNotThrow(() => OpenApiComparator.Compare(spec, otherSpec, out _).ToList());
            Assert.DoesNotThrow(() => OpenApiComparator.Compare(otherSpec, spec, out _).ToList());
        }

        [Test]
        public void Compare_Should_Report_Invalid_Custom_Paths_Format_As_Parsing_Error()
        {
            var oldSpec = Header + "x-ms-paths: [not, an, object]\n";
            var newSpec = Header + "x-ms-paths: {}\n";

            OpenApiComparator.Compare(oldSpec, newSpec, out var parsingErrors);

            var errors = parsingErrors.Select(error => error.ToString()).ToList();
            Assert.That(errors, Has.Count.EqualTo(1));
            Assert.That(errors[0], Does.StartWith("[old]").And.Contain("x-ms-paths"));
        }

        [Test]
        public void Compare_Should_Not_Fail_When_Schema_Is_Missing_With_Exclude_Extension()
        {
            var oldSpec = Header.Replace("paths: {}\n", "")
                + "paths:\n  /a:\n    get:\n      responses:\n        '200':\n          description: ok\n"
                + "          content:\n            application/json: {}\n";
            var newSpec = oldSpec.Replace("application/json: {}", "application/json: {schema: {type: string}}");

            var messages = OpenApiComparator.Compare(oldSpec, newSpec, out _, excludeExtensionKey: "x-exclude").ToList();

            Assert.That(messages.Select(message => message.Code), Does.Contain(ComparisonRules.AddedSchema.Code));
        }

        [Test]
        public void Compare_Should_Set_Parsing_Errors_When_A_Document_Cannot_Be_Read()
        {
            // The CLI reads the parsing errors in a finally block: they must not be null when the parser throws
            IEnumerable<ParsingError> parsingErrors = null;

            Assert.Throws<OpenApiReaderException>(() =>
                OpenApiComparator.Compare("not: [", Header, out parsingErrors));

            Assert.That(parsingErrors, Is.Not.Null);
        }

        private const string NullableOneOf30 = "openapi: 3.0.3\n" + NullableOneOfBody
            + "                nullable: true\n"
            + "                oneOf: [{$ref: '#/components/schemas/A'}, {$ref: '#/components/schemas/B'}]\n"
            + NullableOneOfComponents;

        private const string NullableOneOf31 = "openapi: 3.1.0\n" + NullableOneOfBody
            + "                oneOf: [{$ref: '#/components/schemas/A'}, {$ref: '#/components/schemas/B'}, {type: 'null'}]\n"
            + NullableOneOfComponents;

        private const string NotNullableOneOf31 = "openapi: 3.1.0\n" + NullableOneOfBody
            + "                oneOf: [{$ref: '#/components/schemas/A'}, {$ref: '#/components/schemas/B'}]\n"
            + NullableOneOfComponents;

        private const string NullableOneOfBody = "info: {title: t, version: '1.0'}\n"
            + "paths:\n  /a:\n    get:\n      responses:\n        '200':\n          description: ok\n"
            + "          content:\n            application/json:\n              schema:\n";

        private const string NullableOneOfComponents = "components:\n  schemas:\n"
            + "    A: {type: object, properties: {a: {type: string}}}\n"
            + "    B: {type: object, properties: {b: {type: string}}}\n";

        [Test]
        public void Compare_Should_Not_Report_Nullable_OneOf_Migrated_From_OpenApi30_To_OpenApi31()
        {
            // "nullable: true" + "oneOf: [A, B]" (3.0) is equivalent to "oneOf: [A, B, {type: 'null'}]" (3.1)
            var messages = OpenApiComparator.Compare(NullableOneOf30, NullableOneOf31, out _).ToList();

            Assert.That(messages.Select(message => message.Code),
                Does.Not.Contain(ComparisonRules.NullablePropertyChanged.Code));
        }

        [TestCase(NullableOneOf31, NotNullableOneOf31)]
        [TestCase(NotNullableOneOf31, NullableOneOf31)]
        [TestCase(NullableOneOf30, NotNullableOneOf31)]
        public void Compare_Should_Report_Nullable_OneOf_Change_Once(string oldSpec, string newSpec)
        {
            var messages = OpenApiComparator.Compare(oldSpec, newSpec, out _).ToList();

            Assert.That(messages.Count(message => message.Code == ComparisonRules.NullablePropertyChanged.Code),
                Is.EqualTo(1));
        }

        [Test]
        public void ShouldExclude_Should_Return_False_For_Null_Elements()
        {
            Assert.That(((Microsoft.OpenApi.IOpenApiSchema)null).ShouldExcludeSchema("x-exclude"), Is.False);
            Assert.That(((Microsoft.OpenApi.IOpenApiPathItem)null).ShouldExcludePath("x-exclude"), Is.False);
            Assert.That(((Microsoft.OpenApi.OpenApiOperation)null).ShouldExcludeOperation("x-exclude"), Is.False);
        }
    }
}
