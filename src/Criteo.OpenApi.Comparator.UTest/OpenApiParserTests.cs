// Copyright (c) Criteo Technology. All rights reserved.
// Licensed under the Apache 2.0 License. See LICENSE in the project root for license information.

using System.IO;
using System.Reflection;
using Criteo.OpenApi.Comparator.Comparators.Extensions;
using Criteo.OpenApi.Comparator.Parser;
using Microsoft.OpenApi;
using NUnit.Framework;

namespace Criteo.OpenApi.Comparator.UTest
{
    [TestFixture]
    public class OpenApiParserTests
    {
        private static string ReadOpenApiFile(string fileName)
        {
            var baseDir = Directory.GetParent(typeof(OpenApiParserTests).GetTypeInfo().Assembly.Location)
                .ToString();
            var filePath = Path.Combine(baseDir, "Resource", fileName);
            return File.ReadAllText(filePath);
        }

        /// <summary>
        /// Verifies that the Parser throws an Exception when an in valid json in given
        /// </summary>
        [Test]
        public void OpenApiParser_Should_Throw_Exception_When_Invalid_Json()
        {
            const string fileName = "invalid_json_file.txt";
            var documentAsString = ReadOpenApiFile(fileName);
            var exception = Assert.Throws<OpenApiReaderException>(() => OpenApiParser.Parse(documentAsString, out _));
            Assert.That(exception.Message, Does.StartWith("Unable to parse the OpenAPI document"));
        }

        [Test]
        public void OpenApiParser_Should_Throw_Exception_When_Unsupported_Version()
        {
            const string document = "openapi: 9.0.0\ninfo: {title: t, version: '1'}\npaths: {}\n";
            var exception = Assert.Throws<OpenApiUnsupportedSpecVersionException>(() => OpenApiParser.Parse(document, out _));
            Assert.That(exception.Message, Does.Contain("9.0.0"));
        }

        /// <summary>
        /// Verifies that the reader limits are enforced (here, the YAML alias expansion), even for OpenAPI 3.0 documents
        /// that are converted by the parser to keep "nullable: true".
        /// </summary>
        [Test]
        public void OpenApiParser_Should_Throw_Exception_When_Yaml_Aliases_Expand_Too_Much()
        {
            const string document = "openapi: 3.0.0\ninfo: {title: t, version: '1'}\npaths: {}\n"
                + "x-a: &a [x, x, x, x, x, x, x, x, x, x]\n"
                + "x-b: &b [*a, *a, *a, *a, *a, *a, *a, *a, *a, *a]\n"
                + "x-c: &c [*b, *b, *b, *b, *b, *b, *b, *b, *b, *b]\n"
                + "x-d: &d [*c, *c, *c, *c, *c, *c, *c, *c, *c, *c]\n"
                + "components:\n  schemas:\n    A: {nullable: true, allOf: [{type: string}]}\n";
            var exception = Assert.Throws<OpenApiReaderException>(() => OpenApiParser.Parse(document, out _));
            Assert.That(exception.Message, Does.Contain("aliases"));
        }

        /// <summary>
        /// Verifies that a valid JsonDocument object is parsed when input is a valid OpenApi
        /// </summary>
        [TestCase("openapi_specification.json")]
        [TestCase("openapi_specification.yaml")]
        public void OpenApiParser_Should_Return_Valid_OpenApi_Document_Object(string fileName)
        {
            var documentAsString = ReadOpenApiFile(fileName);
            var validOpenApiDocument = OpenApiParser.Parse(documentAsString, out _);
            Assert.That(validOpenApiDocument, Is.InstanceOf<JsonDocument<OpenApiDocument>>());
        }

        private const string NullableWithoutTypeDocument = @"openapi: 3.0.3
info: {title: t, version: '1'}
paths: {}
components:
  schemas:
    User: {type: object}
    Pet:
      type: object
      example: {nullable: true}
      properties:
        owner:
          nullable: true
          allOf: [{$ref: '#/components/schemas/User'}]
        default:
          nullable: true
          allOf: [{$ref: '#/components/schemas/User'}]
        vet:
          allOf: [{$ref: '#/components/schemas/User'}]
        name:
          type: string
          nullable: true
";

        [TestCase(false)]
        [TestCase(true)]
        public void OpenApiParser_Should_Keep_Nullable_Without_Type_In_OpenApi_3_0(bool asJson)
        {
            var document = asJson ? ToJson(NullableWithoutTypeDocument) : NullableWithoutTypeDocument;

            var parsed = OpenApiParser.Parse(document, out var diagnostic);

            Assert.That(diagnostic.Errors, Is.Empty);
            Assert.That(parsed.SpecVersion, Is.EqualTo(OpenApiSpecVersion.OpenApi3_0));
            var pet = parsed.Typed.Components.Schemas["Pet"];
            Assert.That(pet.Properties["owner"].IsNullable(), Is.True, "nullable: true without type");
            Assert.That(pet.Properties["default"].IsNullable(), Is.True, "property named like a keyword");
            Assert.That(pet.Properties["vet"].IsNullable(), Is.False);
            Assert.That(pet.Properties["name"].IsNullable(), Is.True, "nullable: true with type");
            Assert.That(pet.IsNullable(), Is.False);
        }

        [Test]
        public void OpenApiParser_Should_Not_Alter_Data_When_Keeping_Nullable()
        {
            var parsed = OpenApiParser.Parse(NullableWithoutTypeDocument, out _);

#pragma warning disable CS0618 // Example is obsolete but it is what the OpenAPI 3.0 "example" keyword is read into
            var example = parsed.Typed.Components.Schemas["Pet"].Example.ToJsonString();
#pragma warning restore CS0618
            Assert.That(example, Is.EqualTo("{\"nullable\":true}"));
        }

        [Test]
        public void OpenApiParser_Should_Not_Convert_OpenApi_3_1_Documents()
        {
            var document = NullableWithoutTypeDocument.Replace("openapi: 3.0.3", "openapi: 3.1.0");

            var parsed = OpenApiParser.Parse(document, out _);

            Assert.That(parsed.SpecVersion, Is.EqualTo(OpenApiSpecVersion.OpenApi3_1));
            var owner = parsed.Typed.Components.Schemas["Pet"].Properties["owner"];
            Assert.That(owner.Extensions?.ContainsKey(OpenApiSchemaExtensions.NullableExtension) ?? false, Is.False);
        }

        private static string ToJson(string yaml)
        {
            var yamlStream = new SharpYaml.Serialization.YamlStream();
            yamlStream.Load(new StringReader(yaml));
            return Microsoft.OpenApi.YamlReader.YamlConverter.ToJsonNode(yamlStream.Documents[0]).ToJsonString();
        }
    }
}
